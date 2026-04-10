using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace NewSchool.Google;

/// <summary>
/// Google Calendar REST API v3 클라이언트
/// HttpClient + System.Text.Json (AOT 안전)
/// </summary>
public class GoogleCalendarApiClient
{
    private const string BaseUrl = "https://www.googleapis.com/calendar/v3";
    private static readonly HttpClient _httpClient = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        UseCookies = false
    }) { Timeout = TimeSpan.FromSeconds(30) };
    private readonly GoogleAuthService _authService;

    public GoogleCalendarApiClient(GoogleAuthService authService)
    {
        _authService = authService;
    }

    #region Calendar List

    /// <summary>사용자의 캘린더 목록 조회</summary>
    public async Task<List<GoogleCalendarListEntry>> GetCalendarListAsync()
    {
        var allItems = new List<GoogleCalendarListEntry>();
        string? pageToken = null;

        do
        {
            string url = $"{BaseUrl}/users/me/calendarList?maxResults=250";
            if (!string.IsNullOrEmpty(pageToken))
                url += $"&pageToken={Uri.EscapeDataString(pageToken)}";

            var request = await CreateAuthRequestAsync(HttpMethod.Get, url);
            var response = await SendWithRetryAsync(request);
            var json = await response.Content.ReadAsStringAsync();

            var result = JsonSerializer.Deserialize(json, GoogleCalendarJsonContext.Default.GoogleCalendarListResponse);
            if (result?.Items != null)
                allItems.AddRange(result.Items);

            pageToken = result?.NextPageToken;
        } while (!string.IsNullOrEmpty(pageToken));

        Debug.WriteLine($"[GoogleCalAPI] 캘린더 목록: {allItems.Count}개");
        return allItems;
    }

    /// <summary>새 캘린더 생성 (POST /calendars)</summary>
    public async Task<GoogleCalendarResource?> InsertCalendarAsync(string summary, string? description = null)
    {
        string url = $"{BaseUrl}/calendars";
        var request = await CreateAuthRequestAsync(HttpMethod.Post, url);
        var body = new GoogleCalendarInsertRequest
        {
            Summary = summary,
            Description = description,
            TimeZone = "Asia/Seoul"
        };
        request.Content = new StringContent(
            JsonSerializer.Serialize(body, GoogleCalendarJsonContext.Default.GoogleCalendarInsertRequest),
            Encoding.UTF8, "application/json");

        var response = await SendWithRetryAsync(request);
        var json = await response.Content.ReadAsStringAsync();
        var created = JsonSerializer.Deserialize(json, GoogleCalendarJsonContext.Default.GoogleCalendarResource);
        Debug.WriteLine($"[GoogleCalAPI] 캘린더 생성: {created?.Id} ({created?.Summary})");
        return created;
    }

    #endregion

    #region Events

    /// <summary>이벤트 목록 조회 (증분 동기화 지원)</summary>
    public async Task<GoogleEventsListResponse> ListEventsAsync(
        string calendarId,
        DateTime? timeMin = null,
        DateTime? timeMax = null,
        string? syncToken = null,
        string? pageToken = null)
    {
        var sb = new StringBuilder(256);
        sb.Append($"{BaseUrl}/calendars/{Uri.EscapeDataString(calendarId)}/events");
        sb.Append("?maxResults=2500&singleEvents=true&orderBy=startTime");

        if (!string.IsNullOrEmpty(syncToken))
        {
            // 증분 동기화 — syncToken 사용 시 timeMin/timeMax 무시
            sb.Append($"&syncToken={Uri.EscapeDataString(syncToken)}");
        }
        else
        {
            if (timeMin.HasValue)
                sb.Append($"&timeMin={Uri.EscapeDataString(timeMin.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"))}");
            if (timeMax.HasValue)
                sb.Append($"&timeMax={Uri.EscapeDataString(timeMax.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"))}");
        }

        if (!string.IsNullOrEmpty(pageToken))
            sb.Append($"&pageToken={Uri.EscapeDataString(pageToken)}");

        var request = await CreateAuthRequestAsync(HttpMethod.Get, sb.ToString());
        var response = await SendWithRetryAsync(request);
        var json = await response.Content.ReadAsStringAsync();

        var result = JsonSerializer.Deserialize(json, GoogleCalendarJsonContext.Default.GoogleEventsListResponse);
        Debug.WriteLine($"[GoogleCalAPI] 이벤트 조회: {result?.Items?.Count ?? 0}개");
        return result ?? new GoogleEventsListResponse();
    }

    /// <summary>이벤트 생성</summary>
    public async Task<GoogleEvent?> InsertEventAsync(string calendarId, GoogleEvent ev)
    {
        string url = $"{BaseUrl}/calendars/{Uri.EscapeDataString(calendarId)}/events";
        var request = await CreateAuthRequestAsync(HttpMethod.Post, url);
        request.Content = new StringContent(
            JsonSerializer.Serialize(ev, GoogleCalendarJsonContext.Default.GoogleEvent),
            Encoding.UTF8, "application/json");

        var response = await SendWithRetryAsync(request);
        var json = await response.Content.ReadAsStringAsync();
        var created = JsonSerializer.Deserialize(json, GoogleCalendarJsonContext.Default.GoogleEvent);
        Debug.WriteLine($"[GoogleCalAPI] 이벤트 생성: {created?.Id}");
        return created;
    }

    /// <summary>이벤트 수정</summary>
    public async Task<GoogleEvent?> UpdateEventAsync(string calendarId, string eventId, GoogleEvent ev)
    {
        string url = $"{BaseUrl}/calendars/{Uri.EscapeDataString(calendarId)}/events/{Uri.EscapeDataString(eventId)}";
        var request = await CreateAuthRequestAsync(HttpMethod.Put, url);
        request.Content = new StringContent(
            JsonSerializer.Serialize(ev, GoogleCalendarJsonContext.Default.GoogleEvent),
            Encoding.UTF8, "application/json");

        var response = await SendWithRetryAsync(request);
        var json = await response.Content.ReadAsStringAsync();
        var updated = JsonSerializer.Deserialize(json, GoogleCalendarJsonContext.Default.GoogleEvent);
        Debug.WriteLine($"[GoogleCalAPI] 이벤트 수정: {updated?.Id}");
        return updated;
    }

    /// <summary>이벤트 삭제</summary>
    public async Task<bool> DeleteEventAsync(string calendarId, string eventId)
    {
        string url = $"{BaseUrl}/calendars/{Uri.EscapeDataString(calendarId)}/events/{Uri.EscapeDataString(eventId)}";
        var request = await CreateAuthRequestAsync(HttpMethod.Delete, url);

        var response = await SendWithRetryAsync(request);
        bool ok = response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Gone;
        Debug.WriteLine($"[GoogleCalAPI] 이벤트 삭제: {eventId} → {(ok ? "성공" : "실패")}");
        return ok;
    }

    #endregion

    #region HTTP Helpers

    private async Task<HttpRequestMessage> CreateAuthRequestAsync(HttpMethod method, string url)
    {
        string? token = await _authService.GetValidAccessTokenAsync();
        if (string.IsNullOrEmpty(token))
            throw new InvalidOperationException("Google 인증이 필요합니다. 설정에서 Google 계정을 연동해 주세요.");

        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    /// <summary>401 자동 재시도 + 429 백오프</summary>
    private async Task<HttpResponseMessage> SendWithRetryAsync(HttpRequestMessage request, int retryCount = 0)
    {
        var response = await _httpClient.SendAsync(request);

        // 401 Unauthorized → 토큰 갱신 후 재시도
        if (response.StatusCode == HttpStatusCode.Unauthorized && retryCount < 1)
        {
            Debug.WriteLine("[GoogleCalAPI] 401 — 토큰 갱신 시도");
            if (await _authService.RefreshTokenAsync())
            {
                var retryRequest = await CreateAuthRequestAsync(request.Method, request.RequestUri!.ToString());
                if (request.Content != null)
                {
                    // Content는 한 번만 읽을 수 있으므로 새로 만들어야 할 수 있음
                    // 여기서는 이미 전송된 request의 content를 재사용 시도
                    retryRequest.Content = request.Content;
                }
                return await SendWithRetryAsync(retryRequest, retryCount + 1);
            }
        }

        // 429 Too Many Requests → 백오프
        if (response.StatusCode == (HttpStatusCode)429 && retryCount < 3)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Pow(2, retryCount + 1));
            Debug.WriteLine($"[GoogleCalAPI] 429 — {retryAfter.TotalSeconds}초 대기 후 재시도");
            await Task.Delay(retryAfter);
            var retryRequest = await CreateAuthRequestAsync(request.Method, request.RequestUri!.ToString());
            return await SendWithRetryAsync(retryRequest, retryCount + 1);
        }

        // 410 Gone → syncToken 만료 (전체 동기화 필요)
        if (response.StatusCode == HttpStatusCode.Gone)
        {
            Debug.WriteLine("[GoogleCalAPI] 410 Gone — syncToken 만료, 전체 동기화 필요");
            // 호출자에서 처리하도록 그대로 반환
        }

        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.Gone)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            Debug.WriteLine($"[GoogleCalAPI] HTTP {(int)response.StatusCode}: {errorBody}");
            response.EnsureSuccessStatusCode(); // throw
        }

        return response;
    }

    #endregion
}

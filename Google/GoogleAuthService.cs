using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NewSchool.Google;

/// <summary>
/// Google OAuth 2.0 인증 서비스
/// - Loopback redirect + PKCE 방식
/// - 토큰 DPAPI 암호화 후 Settings.db에 저장
/// </summary>
public sealed class GoogleAuthService : IDisposable
{
    private const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string RevokeEndpoint = "https://oauth2.googleapis.com/revoke";

    // 전체 권한(auth/calendar)이 아니라 실제로 호출하는 API 에 맞춘 세분화 스코프만 요청한다.
    // (공유 설정 변경·캘린더 삭제 권한은 앱이 쓰지 않으므로 요청하지 않음 — OAuth 검증 대응)
    private const string ScopeCalendarList = "https://www.googleapis.com/auth/calendar.calendarlist.readonly";
    private const string ScopeCalendars = "https://www.googleapis.com/auth/calendar.calendars";
    private const string ScopeEvents = "https://www.googleapis.com/auth/calendar.events";

    /// <summary>이전 버전이 요청하던 전체 권한 스코프 — 세분화 스코프 3종을 모두 포함한다</summary>
    private const string ScopeFullCalendar = "https://www.googleapis.com/auth/calendar";

    private static readonly string[] RequiredScopes = [ScopeCalendarList, ScopeCalendars, ScopeEvents];

    private const string Scope = $"{ScopeCalendarList} {ScopeCalendars} {ScopeEvents}";

    /// <summary>스코프별 사용자 안내 문구 — 부분 동의로 빠졌을 때 무엇이 안 되는지 알려준다</summary>
    private static string DescribeScope(string scope) => scope switch
    {
        ScopeCalendarList => "캘린더 목록 조회",
        ScopeCalendars => "학교 전용 캘린더 생성",
        ScopeEvents => "일정 조회·등록·수정·삭제",
        _ => scope
    };

    // ──────────────────────────────────────────────────────────────
    // Google Cloud Console에서 발급받은 OAuth 2.0 인증 정보
    // secrets.json 의 google_oauth 섹션에서 로드 (SecretsService, git 제외)
    // ──────────────────────────────────────────────────────────────
    internal static string ClientId => Services.SecretsService.GoogleClientId;
    internal static string ClientSecret => Services.SecretsService.GoogleClientSecret;

    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private bool _disposed;

    /// <summary>OAuth 자격증명이 설정되어 있는지 확인</summary>
    public static bool HasCredentials =>
        ClientId != "YOUR_CLIENT_ID_HERE" && !string.IsNullOrEmpty(ClientId);

    /// <summary>
    /// 인증 완료 여부.
    ///
    /// <para>저장값이 아니라 <b>복호화 결과</b>를 본다. 토큰은 DPAPI 로 현재 PC·사용자에 묶여
    /// 암호화되므로, 포터블로 다른 PC 에서 실행하면 문자열은 남아 있어도 복호화가 실패해
    /// 빈 값이 된다. 저장값만 검사하면 UI 는 "연결됨" 으로 보이는데 모든 동기화는 조용히
    /// 실패한다 — 실제로 토큰을 쓰는 <see cref="GetValidAccessTokenAsync"/> 등이 전부
    /// <c>Decrypt</c> 를 거치므로 여기서도 같은 기준을 쓴다.</para>
    /// </summary>
    public bool IsAuthenticated =>
        HasCredentials && !string.IsNullOrEmpty(Decrypt(Settings.GoogleRefreshToken.Value));

    /// <summary>
    /// 마지막 인증 시도가 실패한 이유(사용자에게 보여줄 문구). 성공했거나 시도 전이면 null.
    /// AuthenticateAsync 가 false 를 돌려줄 때 호출자가 읽어 쓴다.
    /// </summary>
    public string? LastAuthError { get; private set; }

    /// <summary>
    /// 유효한 Access Token 반환 (만료 시 자동 갱신)
    /// </summary>
    public async Task<string?> GetValidAccessTokenAsync()
    {
        if (!IsAuthenticated) return null;

        // 만료 확인
        string expiryStr = Decrypt(Settings.GoogleTokenExpiry.Value);
        if (DateTime.TryParse(expiryStr, out var expiry) && DateTime.UtcNow < expiry.AddMinutes(-2))
        {
            // 아직 유효
            string token = Decrypt(Settings.GoogleAccessToken.Value);
            if (!string.IsNullOrEmpty(token))
                return token;
        }

        // 갱신 필요
        if (await RefreshTokenAsync())
            return Decrypt(Settings.GoogleAccessToken.Value);

        return null;
    }

    /// <summary>
    /// 전체 OAuth 플로우 실행 (브라우저 팝업)
    /// </summary>
    public async Task<bool> AuthenticateAsync(CancellationToken ct = default)
    {
        string clientId = ClientId;
        string clientSecret = ClientSecret;
        LastAuthError = null;

        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
        {
            LastAuthError = "Google 인증 정보(Client ID/Secret)가 설정되어 있지 않습니다.";
            Debug.WriteLine("[GoogleAuth] Client ID/Secret이 설정되지 않았습니다.");
            return false;
        }

        try
        {
            // 1. PKCE 생성
            string codeVerifier = GenerateCodeVerifier();
            string codeChallenge = GenerateCodeChallenge(codeVerifier);

            // 2. 사용 가능한 포트 찾기
            int port = FindAvailablePort();
            string redirectUri = $"http://127.0.0.1:{port}/callback/";

            // 3. HttpListener 시작
            using var listener = new HttpListener();
            listener.Prefixes.Add(redirectUri);
            listener.Start();
            Debug.WriteLine($"[GoogleAuth] Listener 시작: {redirectUri}");

            // 4. 인증 URL 생성 + 브라우저 열기
            string authUrl = $"{AuthEndpoint}" +
                $"?client_id={Uri.EscapeDataString(clientId)}" +
                $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                $"&response_type=code" +
                $"&scope={Uri.EscapeDataString(Scope)}" +
                $"&code_challenge={Uri.EscapeDataString(codeChallenge)}" +
                $"&code_challenge_method=S256" +
                $"&access_type=offline" +
                $"&prompt=consent";

            Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });
            Debug.WriteLine("[GoogleAuth] 브라우저 열림");

            // 5. 콜백 대기 (5분 타임아웃)
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromMinutes(5));

            var context = await listener.GetContextAsync().WaitAsync(timeoutCts.Token);
            string? code = context.Request.QueryString["code"];
            string? error = context.Request.QueryString["error"];

            // 6. 브라우저에 응답 보내기
            string responseHtml = error == null
                ? "<html><body><h2>인증 완료!</h2><p>이 창을 닫아도 됩니다.</p></body></html>"
                : $"<html><body><h2>인증 실패</h2><p>{error}</p></body></html>";
            byte[] responseBytes = Encoding.UTF8.GetBytes(responseHtml);
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = responseBytes.Length;
            await context.Response.OutputStream.WriteAsync(responseBytes, ct);
            context.Response.Close();
            listener.Stop();

            if (string.IsNullOrEmpty(code))
            {
                LastAuthError = error == "access_denied"
                    ? "Google 동의 화면에서 권한 허용이 취소되었습니다."
                    : $"인증에 실패했습니다. ({error})";
                Debug.WriteLine($"[GoogleAuth] 인증 실패: {error}");
                return false;
            }

            // 7. Authorization code → Token 교환
            return await ExchangeCodeForTokenAsync(code, codeVerifier, redirectUri, clientId, clientSecret);
        }
        catch (OperationCanceledException)
        {
            LastAuthError = "인증 시간이 초과되었거나 취소되었습니다.";
            Debug.WriteLine("[GoogleAuth] 인증 시간 초과 또는 취소");
            return false;
        }
        catch (Exception ex)
        {
            LastAuthError = ex.Message;
            Debug.WriteLine($"[GoogleAuth] 인증 실패: {ex.Message}");
            return false;
        }
    }

    /// <summary>Refresh Token으로 Access Token 갱신</summary>
    public async Task<bool> RefreshTokenAsync()
    {
        try
        {
            string refreshToken = Decrypt(Settings.GoogleRefreshToken.Value);
            string clientId = ClientId;
            string clientSecret = ClientSecret;

            if (string.IsNullOrEmpty(refreshToken) || string.IsNullOrEmpty(clientId))
                return false;

            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("client_id", clientId),
                new KeyValuePair<string, string>("client_secret", clientSecret),
                new KeyValuePair<string, string>("refresh_token", refreshToken),
                new KeyValuePair<string, string>("grant_type", "refresh_token")
            });

            var response = await _httpClient.PostAsync(TokenEndpoint, content);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Debug.WriteLine($"[GoogleAuth] 토큰 갱신 실패: {json}");

                // invalid_grant = 리프레시 토큰이 만료·취소됨(6개월 미사용, 계정 비번 변경,
                // 토큰 재발급 한도 초과 등). 이 토큰으로는 영원히 갱신이 안 되는데도 저장돼 있으면
                // IsAuthenticated 가 계속 true 라 앱은 "연결됨"인 채 모든 동기화가 조용히 실패한다.
                // → 죽은 토큰을 지워 IsAuthenticated 를 false 로 만들고 재인증을 유도한다.
                // (일시적 네트워크 오류나 OneDrive 포터블의 DPAPI 복호화 실패로는 여기 오지 않는다
                //  — 복호화 실패 시엔 위에서 refreshToken 이 빈 문자열이라 HTTP 호출 전에 반환됨.)
                if (response.StatusCode == HttpStatusCode.BadRequest && json.Contains("invalid_grant"))
                {
                    Settings.GoogleAccessToken.Set("");
                    Settings.GoogleRefreshToken.Set("");
                    Settings.GoogleTokenExpiry.Set("");
                    Debug.WriteLine("[GoogleAuth] invalid_grant — 저장된 토큰 삭제(재인증 필요)");
                }
                return false;
            }

            var tokenResp = JsonSerializer.Deserialize(json, GoogleCalendarJsonContext.Default.GoogleTokenResponse);
            if (tokenResp == null) return false;

            // Access Token 저장 (Refresh Token은 갱신 응답에 포함되지 않을 수 있음)
            SaveAccessToken(tokenResp.AccessToken, tokenResp.ExpiresIn);
            Debug.WriteLine("[GoogleAuth] 토큰 갱신 완료");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GoogleAuth] 토큰 갱신 실패: {ex.Message}");
            return false;
        }
    }

    /// <summary>연결 해제 (토큰 revoke + 삭제)</summary>
    public async Task SignOutAsync()
    {
        await RevokeTokenAsync(Decrypt(Settings.GoogleAccessToken.Value));

        // 토큰 삭제
        Settings.GoogleAccessToken.Set("");
        Settings.GoogleRefreshToken.Set("");
        Settings.GoogleTokenExpiry.Set("");
        Debug.WriteLine("[GoogleAuth] 로그아웃 완료");
    }

    #region Token Exchange

    private async Task<bool> ExchangeCodeForTokenAsync(
        string code, string codeVerifier, string redirectUri,
        string clientId, string clientSecret)
    {
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("code", code),
            new KeyValuePair<string, string>("client_id", clientId),
            new KeyValuePair<string, string>("client_secret", clientSecret),
            new KeyValuePair<string, string>("redirect_uri", redirectUri),
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("code_verifier", codeVerifier)
        });

        var response = await _httpClient.PostAsync(TokenEndpoint, content);
        var json = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            LastAuthError = $"토큰 발급에 실패했습니다. (HTTP {(int)response.StatusCode})";
            Debug.WriteLine($"[GoogleAuth] 토큰 교환 실패: {json}");
            return false;
        }

        var tokenResp = JsonSerializer.Deserialize(json, GoogleCalendarJsonContext.Default.GoogleTokenResponse);
        if (tokenResp == null)
        {
            LastAuthError = "Google 응답을 해석하지 못했습니다.";
            return false;
        }

        // 부분 동의(granular consent) 확인 — 세분화 스코프는 동의 화면에서 체크박스가 따로 뜨므로
        // 사용자가 일부만 허용할 수 있다. 그대로 토큰을 저장하면 앱은 "연동됨"인데 동기화만
        // 403 으로 조용히 실패한다. 부족하면 토큰을 버리고 재연동을 유도한다.
        var missing = FindMissingScopes(tokenResp.Scope);
        if (missing.Count > 0)
        {
            LastAuthError = "동기화에 필요한 권한이 모두 허용되지 않았습니다(" +
                string.Join(", ", missing.ConvertAll(DescribeScope)) +
                "). 다시 연동하면서 모든 항목에 체크해 주세요.";
            Debug.WriteLine($"[GoogleAuth] 부분 동의 — 누락 스코프: {string.Join(" ", missing)}");

            // 반쪽짜리 권한의 토큰은 남기지 않는다. 저장 전이라 로컬은 건드릴 게 없고,
            // 이미 발급된 액세스 토큰만 Google 쪽에서 무효화한다.
            await RevokeTokenAsync(tokenResp.AccessToken);
            return false;
        }

        // 토큰 저장
        SaveAccessToken(tokenResp.AccessToken, tokenResp.ExpiresIn);

        if (!string.IsNullOrEmpty(tokenResp.RefreshToken))
        {
            Settings.GoogleRefreshToken.Set(Encrypt(tokenResp.RefreshToken));
        }

        Debug.WriteLine("[GoogleAuth] 토큰 교환 완료");
        return true;
    }

    /// <summary>
    /// 토큰 응답의 scope 문자열에서 빠진 필수 스코프를 찾는다.
    /// 전체 권한(auth/calendar)이 부여됐다면 세분화 3종을 모두 포함하므로 통과시킨다
    /// (전체 권한 시절에 연동한 사용자가 재연동할 때 Google 이 상위 스코프만 돌려줄 수 있음).
    /// scope 필드 자체가 없으면(구형 응답) 판단 근거가 없으므로 통과시킨다 — 실제 실패는
    /// API 호출 시 403 으로 드러난다.
    /// </summary>
    internal static List<string> FindMissingScopes(string? grantedScopes)
    {
        if (string.IsNullOrWhiteSpace(grantedScopes)) return [];

        var granted = grantedScopes.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (Array.IndexOf(granted, ScopeFullCalendar) >= 0) return [];

        var missing = new List<string>();
        foreach (var required in RequiredScopes)
        {
            if (Array.IndexOf(granted, required) < 0)
                missing.Add(required);
        }
        return missing;
    }

    /// <summary>액세스 토큰을 Google 쪽에서 무효화 (실패해도 무시)</summary>
    private static async Task RevokeTokenAsync(string accessToken)
    {
        if (string.IsNullOrEmpty(accessToken)) return;
        try
        {
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("token", accessToken)
            });
            await _httpClient.PostAsync(RevokeEndpoint, content);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GoogleAuth] Revoke 실패 (무시): {ex.Message}");
        }
    }

    private static void SaveAccessToken(string accessToken, int expiresIn)
    {
        Settings.GoogleAccessToken.Set(Encrypt(accessToken));
        var expiry = DateTime.UtcNow.AddSeconds(expiresIn);
        Settings.GoogleTokenExpiry.Set(Encrypt(expiry.ToString("o")));
    }

    #endregion

    #region PKCE

    private static string GenerateCodeVerifier()
    {
        byte[] bytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Base64UrlEncode(bytes);
    }

    private static string GenerateCodeChallenge(string codeVerifier)
    {
        using var sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(Encoding.ASCII.GetBytes(codeVerifier));
        return Base64UrlEncode(hash);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    #endregion

    #region DPAPI 암호화

    private static string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return string.Empty;
        try
        {
            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
            byte[] encryptedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encryptedBytes);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GoogleAuth] 암호화 실패: {ex.Message}");
            return string.Empty;
        }
    }

    private static string Decrypt(string encryptedBase64)
    {
        if (string.IsNullOrEmpty(encryptedBase64)) return string.Empty;
        try
        {
            byte[] encryptedBytes = Convert.FromBase64String(encryptedBase64);
            byte[] plainBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GoogleAuth] 복호화 실패: {ex.Message}");
            return string.Empty;
        }
    }

    #endregion

    #region Helpers

    private static int FindAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    #endregion

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }
}

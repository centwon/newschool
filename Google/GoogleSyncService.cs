using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Scheduler;

namespace NewSchool.Google;

/// <summary>
/// Google Calendar 양방향 동기화 서비스
/// Pull (Google→로컬) + Push (로컬→Google), 증분 동기화(syncToken)
/// ✅ Ktask 통합: extendedProperties로 IsDone/ItemType 동기화
/// 충돌 시 last-write-wins
/// </summary>
public class GoogleSyncService : IDisposable
{
    private readonly GoogleCalendarApiClient _apiClient;
    private readonly GoogleAuthService _authService;
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private PeriodicTimer? _periodicTimer;
    private CancellationTokenSource? _periodicCts;
    private bool _disposed;

    public GoogleSyncService(GoogleAuthService authService, GoogleCalendarApiClient apiClient)
    {
        _authService = authService;
        _apiClient = apiClient;
    }

    /// <summary>동기화 가능한 모든 캘린더 동기화</summary>
    public async Task<SyncResult> SyncAllAsync()
    {
        var totalResult = new SyncResult();

        if (!_authService.IsAuthenticated)
        {
            totalResult.ErrorMessages.Add("Google 인증이 필요합니다.");
            return totalResult;
        }

        if (!Functions.IsNetworkAvailable())
        {
            totalResult.ErrorMessages.Add("네트워크에 연결되어 있지 않습니다.");
            return totalResult;
        }

        try
        {
            using var service = Scheduler.Scheduler.CreateService();
            var calendars = await service.GetSyncableCalendarsAsync();

            if (calendars.Count == 0)
            {
                Debug.WriteLine("[GoogleSync] 동기화 대상 캘린더 없음");
                totalResult.Success = true;
                return totalResult;
            }

            foreach (var calendar in calendars)
            {
                try
                {
                    var result = await SyncCalendarAsync(calendar);
                    totalResult.Created += result.Created;
                    totalResult.Updated += result.Updated;
                    totalResult.Deleted += result.Deleted;
                    totalResult.Errors += result.Errors;
                    totalResult.ErrorMessages.AddRange(result.ErrorMessages);
                }
                catch (Exception ex)
                {
                    totalResult.Errors++;
                    totalResult.ErrorMessages.Add($"{calendar.Title}: {ex.Message}");
                    Debug.WriteLine($"[GoogleSync] {calendar.Title} 동기화 실패: {ex.Message}");
                }
            }

            totalResult.Success = totalResult.Errors == 0;
            totalResult.SyncedAt = DateTime.Now;

            // 마지막 동기화 시각 저장
            Settings.GoogleLastSyncTime.Set(DateTime.UtcNow.ToString("o"));

            Debug.WriteLine($"[GoogleSync] 전체 동기화 완료: {totalResult.Summary}");
        }
        catch (Exception ex)
        {
            totalResult.ErrorMessages.Add(ex.Message);
            Debug.WriteLine($"[GoogleSync] 전체 동기화 실패: {ex.Message}");
        }

        return totalResult;
    }

    /// <summary>단일 캘린더 동기화</summary>
    public async Task<SyncResult> SyncCalendarAsync(KCalendarList calendar)
    {
        if (!await _syncLock.WaitAsync(TimeSpan.FromSeconds(5)))
            return new SyncResult { ErrorMessages = { "동기화가 이미 진행 중입니다." } };

        var result = new SyncResult();

        try
        {
            if (string.IsNullOrEmpty(calendar.GoogleId))
            {
                result.ErrorMessages.Add($"캘린더 '{calendar.Title}'에 Google ID가 없습니다.");
                return result;
            }

            Debug.WriteLine($"[GoogleSync] '{calendar.Title}' 동기화 시작 (SyncMode={calendar.SyncMode})");

            try
            {
                // Pull: Google → 로컬
                await PullFromGoogleAsync(calendar, result);

                // Push: 로컬 → Google (TwoWay일 때만)
                if (calendar.SyncMode == "TwoWay")
                {
                    await PushToGoogleAsync(calendar, result);
                }
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                // Google Calendar가 서버에서 삭제됨 → GoogleId와 SyncToken 초기화
                Debug.WriteLine($"[GoogleSync] 캘린더 '{calendar.Title}' 404 — Google에서 삭제됨, GoogleId 초기화");
                calendar.GoogleId = string.Empty;
                calendar.SyncToken = string.Empty;
                result.ErrorMessages.Add($"'{calendar.Title}' Google 캘린더가 삭제되어 연동이 해제되었습니다. 설정에서 다시 연동해 주세요.");
            }

            // SyncToken 저장
            using var service = Scheduler.Scheduler.CreateService();
            await service.UpdateCalendarAsync(calendar);

            result.Success = result.Errors == 0;
            Debug.WriteLine($"[GoogleSync] '{calendar.Title}' 동기화 완료: {result.Summary}");
        }
        catch (Exception ex)
        {
            result.Errors++;
            result.ErrorMessages.Add(ex.Message);
            Debug.WriteLine($"[GoogleSync] '{calendar.Title}' 동기화 실패: {ex.Message}");
        }
        finally
        {
            _syncLock.Release();
        }

        return result;
    }

    #region Pull: Google → 로컬

    private async Task PullFromGoogleAsync(KCalendarList calendar, SyncResult result)
    {
        using var service = Scheduler.Scheduler.CreateService();
        string? syncToken = string.IsNullOrEmpty(calendar.SyncToken) ? null : calendar.SyncToken;
        string? pageToken = null;

        // syncToken이 없으면 최근 1년 데이터만 가져오기
        DateTime? timeMin = syncToken == null ? DateTime.Today.AddYears(-1) : null;

        do
        {
            GoogleEventsListResponse response;
            try
            {
                response = await _apiClient.ListEventsAsync(
                    calendar.GoogleId, timeMin: timeMin, syncToken: syncToken, pageToken: pageToken);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Gone)
            {
                // syncToken 만료 → 전체 동기화
                Debug.WriteLine("[GoogleSync] syncToken 만료 — 전체 동기화로 전환");
                calendar.SyncToken = string.Empty;
                syncToken = null;
                timeMin = DateTime.Today.AddYears(-1);
                response = await _apiClient.ListEventsAsync(calendar.GoogleId, timeMin: timeMin);
            }

            if (response.Items != null)
            {
                foreach (var gEvent in response.Items)
                {
                    try
                    {
                        await ProcessPulledEventAsync(service, gEvent, calendar.No, result);
                    }
                    catch (Exception ex)
                    {
                        result.Errors++;
                        Debug.WriteLine($"[GoogleSync] Pull 이벤트 처리 실패: {gEvent.Id} — {ex.Message}");
                    }
                }
            }

            pageToken = response.NextPageToken;

            // 마지막 페이지에서 nextSyncToken 저장
            if (!string.IsNullOrEmpty(response.NextSyncToken))
            {
                calendar.SyncToken = response.NextSyncToken;
            }

        } while (!string.IsNullOrEmpty(pageToken));
    }

    private static async Task ProcessPulledEventAsync(
        SchedulerService service, GoogleEvent gEvent, int calendarId, SyncResult result)
    {
        if (string.IsNullOrEmpty(gEvent.Id)) return;

        var localEvent = await service.GetEventByGoogleIdAsync(gEvent.Id);

        if (gEvent.Status == "cancelled")
        {
            // Google에서 삭제됨
            if (localEvent != null && localEvent.Status != "cancelled")
            {
                localEvent.Status = "cancelled";
                localEvent.Updated = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                await service.UpdateEventAsync(localEvent);
                result.Deleted++;
            }
            return;
        }

        if (localEvent == null)
        {
            // 신규 이벤트
            var newEvent = GoogleEventToLocal(gEvent, calendarId);
            await service.CreateEventAsync(newEvent);
            result.Created++;
        }
        else
        {
            // 기존 이벤트 — 충돌 확인 (last-write-wins)
            if (IsGoogleNewer(gEvent.Updated, localEvent.Updated))
            {
                UpdateLocalFromGoogle(localEvent, gEvent);
                await service.UpdateEventAsync(localEvent);
                result.Updated++;
            }
        }
    }

    #endregion

    #region Push: 로컬 → Google

    private async Task PushToGoogleAsync(KCalendarList calendar, SyncResult result)
    {
        using var service = Scheduler.Scheduler.CreateService();

        // 1. 미동기화 이벤트 (GoogleId 없는 것) → Insert
        var unsynced = await service.GetUnsyncedEventsAsync(calendar.No);
        foreach (var localEvent in unsynced)
        {
            try
            {
                var gEvent = LocalToGoogleEvent(localEvent);
                var created = await _apiClient.InsertEventAsync(calendar.GoogleId, gEvent);
                if (created?.Id != null)
                {
                    localEvent.GoogleId = created.Id;
                    localEvent.Updated = created.Updated ?? DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                    await service.UpdateEventAsync(localEvent);
                    result.Created++;
                }
            }
            catch (Exception ex)
            {
                result.Errors++;
                Debug.WriteLine($"[GoogleSync] Push Insert 실패: {localEvent.Title} — {ex.Message}");
            }
        }

        // 2. 로컬 수정된 이벤트 → Update
        string lastSync = Settings.GoogleLastSyncTime.Value;
        if (!string.IsNullOrEmpty(lastSync))
        {
            var modified = await service.GetModifiedEventsSinceAsync(calendar.No, lastSync);
            foreach (var localEvent in modified)
            {
                try
                {
                    var gEvent = LocalToGoogleEvent(localEvent);
                    var updated = await _apiClient.UpdateEventAsync(calendar.GoogleId, localEvent.GoogleId, gEvent);
                    if (updated != null)
                    {
                        localEvent.Updated = updated.Updated ?? DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                        await service.UpdateEventAsync(localEvent);
                        result.Updated++;
                    }
                }
                catch (Exception ex)
                {
                    result.Errors++;
                    Debug.WriteLine($"[GoogleSync] Push Update 실패: {localEvent.Title} — {ex.Message}");
                }
            }
        }

        // 3. 로컬 삭제(cancelled)된 이벤트 → Delete
        var deleted = await service.GetDeletedEventsWithGoogleIdAsync(calendar.No);
        foreach (var localEvent in deleted)
        {
            try
            {
                await _apiClient.DeleteEventAsync(calendar.GoogleId, localEvent.GoogleId);
                result.Deleted++;
            }
            catch (Exception ex)
            {
                result.Errors++;
                Debug.WriteLine($"[GoogleSync] Push Delete 실패: {localEvent.GoogleId} — {ex.Message}");
            }
        }
    }

    #endregion

    #region Mapping

    private static KEvent GoogleEventToLocal(GoogleEvent ge, int calendarId)
    {
        bool isAllday = ge.Start?.Date != null;

        DateTime start, end;
        if (isAllday)
        {
            start = DateTime.Parse(ge.Start!.Date!);
            end = DateTime.Parse(ge.End!.Date!);
        }
        else
        {
            start = DateTime.Parse(ge.Start?.DateTime ?? DateTime.Now.ToString("o"));
            end = DateTime.Parse(ge.End?.DateTime ?? DateTime.Now.AddHours(1).ToString("o"));
        }

        var ev = new KEvent
        {
            GoogleId = ge.Id ?? string.Empty,
            CalendarId = calendarId,
            Title = ge.Summary ?? string.Empty,
            Notes = ge.Description ?? string.Empty,
            Start = start,
            End = end,
            IsAllday = isAllday,
            Location = ge.Location ?? string.Empty,
            Status = ge.Status ?? "confirmed",
            ColorId = ge.ColorId ?? string.Empty,
            Recurrence = ge.Recurrence != null ? string.Join("\n", ge.Recurrence) : string.Empty,
            Updated = ge.Updated ?? DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            User = Settings.User.Value
        };

        // extendedProperties에서 Ktask 통합 데이터 읽기
        ReadExtendedProperties(ge, ev);

        return ev;
    }

    private static GoogleEvent LocalToGoogleEvent(KEvent ke)
    {
        var ge = new GoogleEvent
        {
            Summary = ke.Title,
            Description = string.IsNullOrEmpty(ke.Notes) ? null : ke.Notes,
            Location = string.IsNullOrEmpty(ke.Location) ? null : ke.Location,
            Status = ke.Status,
            ColorId = string.IsNullOrEmpty(ke.ColorId) ? null : ke.ColorId,
        };

        if (ke.IsAllday)
        {
            ge.Start = new GoogleEventDateTime { Date = ke.Start.ToString("yyyy-MM-dd") };
            ge.End = new GoogleEventDateTime { Date = ke.End.ToString("yyyy-MM-dd") };
        }
        else
        {
            ge.Start = new GoogleEventDateTime
            {
                DateTime = ke.Start.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
                TimeZone = "Asia/Seoul"
            };
            ge.End = new GoogleEventDateTime
            {
                DateTime = ke.End.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
                TimeZone = "Asia/Seoul"
            };
        }

        if (!string.IsNullOrEmpty(ke.Recurrence))
        {
            ge.Recurrence = ke.Recurrence.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        // extendedProperties에 Ktask 통합 데이터 쓰기
        WriteExtendedProperties(ke, ge);

        return ge;
    }

    private static void UpdateLocalFromGoogle(KEvent local, GoogleEvent google)
    {
        bool isAllday = google.Start?.Date != null;

        local.Title = google.Summary ?? string.Empty;
        local.Notes = google.Description ?? string.Empty;
        local.Location = google.Location ?? string.Empty;
        local.Status = google.Status ?? "confirmed";
        local.ColorId = google.ColorId ?? string.Empty;
        local.IsAllday = isAllday;
        local.Recurrence = google.Recurrence != null ? string.Join("\n", google.Recurrence) : string.Empty;
        local.Updated = google.Updated ?? DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

        if (isAllday)
        {
            local.Start = DateTime.Parse(google.Start!.Date!);
            local.End = DateTime.Parse(google.End!.Date!);
        }
        else
        {
            local.Start = DateTime.Parse(google.Start?.DateTime ?? local.Start.ToString("o"));
            local.End = DateTime.Parse(google.End?.DateTime ?? local.End.ToString("o"));
        }

        // extendedProperties에서 Ktask 통합 데이터 업데이트
        ReadExtendedProperties(google, local);
    }

    /// <summary>Google 이벤트가 로컬보다 최신인지 비교</summary>
    private static bool IsGoogleNewer(string? googleUpdated, string? localUpdated)
    {
        if (string.IsNullOrEmpty(googleUpdated)) return false;
        if (string.IsNullOrEmpty(localUpdated)) return true;

        if (DateTime.TryParse(googleUpdated, out var gTime) &&
            DateTime.TryParse(localUpdated, out var lTime))
        {
            return gTime.ToUniversalTime() > lTime.ToUniversalTime();
        }
        return false;
    }

    #endregion

    #region ExtendedProperties (Ktask 통합)

    /// <summary>Google Event의 extendedProperties에서 ItemType/IsDone/Completed 읽기</summary>
    private static void ReadExtendedProperties(GoogleEvent ge, KEvent local)
    {
        var props = ge.ExtendedProperties?.Private;
        if (props == null) return;

        if (props.TryGetValue("itemType", out var itemType))
            local.ItemType = itemType;

        if (props.TryGetValue("isDone", out var isDoneStr))
            local.IsDone = isDoneStr == "true";

        if (props.TryGetValue("completed", out var completed))
            local.Completed = completed;
    }

    /// <summary>KEvent의 ItemType/IsDone/Completed를 Google Event의 extendedProperties에 쓰기</summary>
    private static void WriteExtendedProperties(KEvent ke, GoogleEvent ge)
    {
        // task이거나 IsDone이 설정된 경우에만 extendedProperties 기록
        if (ke.ItemType != "task" && !ke.IsDone) return;

        ge.ExtendedProperties = new GoogleExtendedProperties
        {
            Private = new Dictionary<string, string>
            {
                ["itemType"] = ke.ItemType,
                ["isDone"] = ke.IsDone ? "true" : "false",
                ["completed"] = ke.Completed ?? string.Empty
            }
        };
    }

    #endregion

    #region 학사일정 일괄 등록

    /// <summary>
    /// SchoolSchedule 목록을 Google Calendar에 종일 이벤트로 일괄 등록
    /// 학교 캘린더(학교 이름)에 등록됨
    /// </summary>
    public async Task<SyncResult> UploadSchoolSchedulesAsync(List<SchoolSchedule> schedules)
    {
        var result = new SyncResult();

        if (!_authService.IsAuthenticated)
        {
            result.ErrorMessages.Add("Google 인증이 필요합니다.");
            return result;
        }

        if (!Functions.IsNetworkAvailable())
        {
            result.ErrorMessages.Add("네트워크에 연결되어 있지 않습니다.");
            return result;
        }

        if (schedules.Count == 0)
        {
            result.Success = true;
            return result;
        }

        // 학교 캘린더(수업/담임/업무가 매핑된 Google 캘린더) 찾기
        using var service = Scheduler.Scheduler.CreateService();
        var schoolCalendar = (await service.GetAllCalendarsAsync())
            .FirstOrDefault(c => c.Title == "수업" && !string.IsNullOrEmpty(c.GoogleId));

        if (schoolCalendar == null)
        {
            result.ErrorMessages.Add("학교 캘린더가 Google에 연동되지 않았습니다. 먼저 Google 계정을 연동하세요.");
            return result;
        }

        string googleCalendarId = schoolCalendar.GoogleId;

        // 연속 날짜 같은 이벤트 그룹핑 (예: 여름방학 7/22~8/25 → 하나의 이벤트)
        var groups = SchoolScheduleGroupHelper.GroupSchedules(schedules);

        Debug.WriteLine($"[GoogleSync] 학사일정 {schedules.Count}건 → {groups.Count}개 그룹으로 등록 시작");

        foreach (var group in groups)
        {
            try
            {
                var gEvent = new GoogleEvent
                {
                    Summary = group.EventName,
                    Description = group.IsVacation ? "휴업일" : null,
                    Start = new GoogleEventDateTime
                    {
                        Date = group.StartDate.ToString("yyyy-MM-dd")
                    },
                    End = new GoogleEventDateTime
                    {
                        // Google Calendar 종일 이벤트는 end를 exclusive로 처리 (+1일)
                        Date = group.EndDate.AddDays(1).ToString("yyyy-MM-dd")
                    },
                    Status = "confirmed"
                };

                var created = await _apiClient.InsertEventAsync(googleCalendarId, gEvent);
                if (created?.Id != null)
                {
                    result.Created++;
                    Debug.WriteLine($"[GoogleSync] 학사일정 등록: {group.EventName} ({group.StartDate:M/d}~{group.EndDate:M/d})");
                }
            }
            catch (Exception ex)
            {
                result.Errors++;
                result.ErrorMessages.Add($"{group.EventName}: {ex.Message}");
                Debug.WriteLine($"[GoogleSync] 학사일정 등록 실패: {group.EventName} — {ex.Message}");
            }
        }

        result.Success = result.Errors == 0;
        Debug.WriteLine($"[GoogleSync] 학사일정 일괄 등록 완료: {result.Summary}");
        return result;
    }

    #endregion

    #region 주기적 동기화

    public void StartPeriodicSync(TimeSpan interval)
    {
        StopPeriodicSync();
        _periodicCts = new CancellationTokenSource();
        _periodicTimer = new PeriodicTimer(interval);

        _ = Task.Run(async () =>
        {
            try
            {
                while (await _periodicTimer.WaitForNextTickAsync(_periodicCts.Token))
                {
                    try
                    {
                        Debug.WriteLine("[GoogleSync] 주기적 동기화 실행");
                        await SyncAllAsync();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[GoogleSync] 주기적 동기화 오류: {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException) { /* 정상 종료 */ }
        });

        Debug.WriteLine($"[GoogleSync] 주기적 동기화 시작: {interval.TotalMinutes}분 간격");
    }

    public void StopPeriodicSync()
    {
        _periodicCts?.Cancel();
        _periodicTimer?.Dispose();
        _periodicTimer = null;
        _periodicCts?.Dispose();
        _periodicCts = null;
    }

    #endregion

    public void Dispose()
    {
        if (!_disposed)
        {
            StopPeriodicSync();
            _syncLock.Dispose();
            _disposed = true;
        }
    }
}

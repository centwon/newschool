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
public sealed class GoogleSyncService : IDisposable
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
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Gone
                || ex.StatusCode == HttpStatusCode.BadRequest)
            {
                // syncToken 만료(410) 또는 요청 거부(400, 예: 파라미터 비호환) → 전체 동기화로 폴백
                Debug.WriteLine($"[GoogleSync] syncToken 사용 불가({ex.StatusCode}) — 전체 동기화로 전환");
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
            // 학사일정을 UploadSchoolSchedulesAsync 로 올린 뒤 되돌아오는 종일 이벤트는 school.db 에
            // 이미 있으므로 로컬 KEvent 로 다시 만들면 달력에 날짜 옆(SchoolSchedule)과 목록(KEvent)
            // 양쪽에 중복 표시된다. 제목+날짜가 학사일정과 일치하면 Pull 을 건너뛴다.
            if (await IsSchoolScheduleDuplicateAsync(gEvent))
            {
                Debug.WriteLine($"[GoogleSync] 학사일정과 중복되어 Pull 스킵: {gEvent.Summary}");
                return;
            }

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

    /// <summary>Google 종일 이벤트가 school.db 의 학사일정(같은 제목+날짜)과 겹치는지 확인</summary>
    private static async Task<bool> IsSchoolScheduleDuplicateAsync(GoogleEvent gEvent)
    {
        var dateStr = gEvent.Start?.Date;
        if (string.IsNullOrEmpty(dateStr) || string.IsNullOrEmpty(gEvent.Summary)) return false;
        if (!DateTime.TryParse(dateStr, out var date)) return false;

        try
        {
            using var scheduleService = new NewSchool.Services.SchoolScheduleService(Settings.SchoolDB.Value);
            // GetByDateRangeAsync는 [start, end) 반개구간이므로 해당 하루만 조회하려면 end=date+1일
            var result = await scheduleService.GetSchedulesByDataRangeAsync(Settings.SchoolCode, date, date.AddDays(1));
            return result.Success && result.Schedules.Any(s => s.EVENT_NM == gEvent.Summary);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GoogleSync] 학사일정 중복 확인 실패: {ex.Message}");
            return false;
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
        // 최초 동기화(lastSync 없음)에도 GoogleId가 있는 기존 이벤트의 오프라인 수정분이 유실되지 않도록
        // epoch 이후 전체를 "수정됨"으로 간주해 비교 대상에 포함
        string lastSync = Settings.GoogleLastSyncTime.Value;
        {
            string sinceUtc = string.IsNullOrEmpty(lastSync)
                ? "0001-01-01T00:00:00.000Z"
                : lastSync;
            var modified = await service.GetModifiedEventsSinceAsync(calendar.No, sinceUtc);
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
                // Google 에서 삭제 완료 → 로컬 cancelled 행 영구 삭제(누적/재전송 방지)
                await service.PurgeEventAsync(localEvent.No);
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
            start = DateTime.Parse(ge.Start!.Date!, null, System.Globalization.DateTimeStyles.RoundtripKind);
            // Google Calendar의 종일 이벤트 End는 exclusive(배타적)이므로
            // inclusive(포함)로 변환: 1일 빼기
            end = DateTime.Parse(ge.End!.Date!, null, System.Globalization.DateTimeStyles.RoundtripKind).AddDays(-1);
        }
        else
        {
            start = DateTime.Parse(ge.Start?.DateTime ?? DateTime.UtcNow.ToString("o"),
                null, System.Globalization.DateTimeStyles.RoundtripKind).ToLocalTime();
            end = DateTime.Parse(ge.End?.DateTime ?? DateTime.UtcNow.AddHours(1).ToString("o"),
                null, System.Globalization.DateTimeStyles.RoundtripKind).ToLocalTime();
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

    /// <summary>KEvent → GoogleEvent 변환 (외부에서도 즉시 Push에 사용)</summary>
    public static GoogleEvent ConvertToGoogleEvent(KEvent ke) => LocalToGoogleEvent(ke);

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
            // 로컬에서는 inclusive end로 저장하므로, Google에 보낼 때는
            // exclusive로 변환: 1일 더하기
            // 예: 로컬 "3/25~4/1(inclusive)" → Google "3/25~4/2(exclusive)"
            ge.End = new GoogleEventDateTime { Date = ke.End.AddDays(1).ToString("yyyy-MM-dd") };
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
            local.Start = DateTime.Parse(google.Start!.Date!, null, System.Globalization.DateTimeStyles.RoundtripKind);
            // Google Calendar의 종일 이벤트 End는 exclusive(배타적)이므로
            // inclusive(포함)로 변환: 1일 빼기
            local.End = DateTime.Parse(google.End!.Date!, null, System.Globalization.DateTimeStyles.RoundtripKind).AddDays(-1);
        }
        else
        {
            local.Start = DateTime.Parse(google.Start?.DateTime ?? local.Start.ToString("o"),
                null, System.Globalization.DateTimeStyles.RoundtripKind).ToLocalTime();
            local.End = DateTime.Parse(google.End?.DateTime ?? local.End.ToString("o"),
                null, System.Globalization.DateTimeStyles.RoundtripKind).ToLocalTime();
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

    #region 학사일정 동기화 (학교 전용 캘린더, 재조정)

    private const string ScheduleCalendarTitle = "학사일정";
    private const string ScheduleItemType = "schoolschedule";
    private const string ScheduleCalendarColor = "#757575";

    /// <summary>
    /// SchoolSchedule 목록을 로컬 "학사일정" 캘린더(학교별로 분리)와 재조정(reconcile)한 뒤 Google Calendar에 반영.
    /// 단순 업로드가 아니라 신규/변경/삭제를 전부 비교해서 처리하므로, school.db 에서 학사일정을 수정한 뒤
    /// 이 메서드를 다시 실행하면 구글 쪽도 그대로 맞춰진다. 로컬 KEvent에 GoogleId를 저장해두기 때문에
    /// 이후 어떤 경로로 Pull이 일어나도 중복 생성되지 않는다.
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
            // 빈 목록으로 재조정을 돌리면 "학교에 등록된 일정이 없다"고 오판해 이미 동기화된 항목을
            // 전부 삭제 대상으로 취급하게 된다(NEIS 조회 실패 등으로 빈 리스트가 들어올 수 있음).
            // 안전하게 조회 실패로 간주하고 아무것도 건드리지 않는다.
            result.ErrorMessages.Add("학사일정이 없습니다. 삭제로 오인되지 않도록 동기화를 건너뜁니다.");
            return result;
        }

        string schoolCode = Settings.SchoolCode;
        if (string.IsNullOrEmpty(schoolCode))
        {
            result.ErrorMessages.Add("학교 설정이 필요합니다.");
            return result;
        }

        using var service = Scheduler.Scheduler.CreateService();
        KCalendarList calendar;
        try
        {
            calendar = await EnsureScheduleCalendarAsync(service);
        }
        catch (Exception ex)
        {
            result.ErrorMessages.Add($"학사일정 캘린더 준비 실패: {ex.Message}");
            Debug.WriteLine($"[GoogleSync] 학사일정 캘린더 준비 실패: {ex.Message}");
            return result;
        }

        // 연속 날짜 같은 이벤트 그룹핑 (예: 여름방학 7/22~8/25 → 하나의 이벤트). 이름을 자연키로 사용.
        var groups = SchoolScheduleGroupHelper.GroupSchedules(schedules);
        var groupsByTitle = groups.ToDictionary(g => g.EventName);

        var existing = await service.GetEventsByCalendarAndTypeAsync(calendar.No, ScheduleItemType);
        var existingByTitle = existing.ToDictionary(e => e.Title);

        Debug.WriteLine($"[GoogleSync] 학사일정 재조정 시작: 현재 {groups.Count}개, 기존 등록 {existing.Count}개");

        // 1) 신규 등록 + 날짜 변경분 수정
        foreach (var group in groups)
        {
            if (!existingByTitle.TryGetValue(group.EventName, out var localEvent))
            {
                try
                {
                    var ev = new KEvent
                    {
                        CalendarId = calendar.No,
                        Title = group.EventName,
                        ItemType = ScheduleItemType,
                        Start = group.StartDate,
                        End = group.EndDate,
                        IsAllday = true,
                        Status = "confirmed",
                        Notes = group.IsVacation ? "휴업일" : string.Empty,
                        Updated = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                        User = Environment.UserName
                    };
                    ev.No = await service.CreateEventAsync(ev);

                    var created = await _apiClient.InsertEventAsync(calendar.GoogleId, BuildScheduleGoogleEvent(group));
                    if (created?.Id != null)
                    {
                        ev.GoogleId = created.Id;
                        ev.Updated = created.Updated ?? ev.Updated;
                        await service.UpdateEventAsync(ev);
                    }

                    result.Created++;
                    Debug.WriteLine($"[GoogleSync] 학사일정 신규: {group.EventName} ({group.StartDate:M/d}~{group.EndDate:M/d})");
                }
                catch (Exception ex)
                {
                    result.Errors++;
                    result.ErrorMessages.Add($"{group.EventName}: {ex.Message}");
                    Debug.WriteLine($"[GoogleSync] 학사일정 신규 등록 실패: {group.EventName} — {ex.Message}");
                }
            }
            else if (localEvent.Start.Date != group.StartDate.Date || localEvent.End.Date != group.EndDate.Date)
            {
                try
                {
                    localEvent.Start = group.StartDate;
                    localEvent.End = group.EndDate;
                    localEvent.Notes = group.IsVacation ? "휴업일" : string.Empty;
                    localEvent.Updated = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                    await service.UpdateEventAsync(localEvent);

                    if (!string.IsNullOrEmpty(localEvent.GoogleId))
                        await _apiClient.UpdateEventAsync(calendar.GoogleId, localEvent.GoogleId, BuildScheduleGoogleEvent(group));

                    result.Updated++;
                    Debug.WriteLine($"[GoogleSync] 학사일정 수정: {group.EventName} ({group.StartDate:M/d}~{group.EndDate:M/d})");
                }
                catch (Exception ex)
                {
                    result.Errors++;
                    result.ErrorMessages.Add($"{group.EventName}: {ex.Message}");
                    Debug.WriteLine($"[GoogleSync] 학사일정 수정 실패: {group.EventName} — {ex.Message}");
                }
            }
        }

        // 2) school.db 에서 사라진 학사일정 → 로컬+구글 정리
        foreach (var stale in existing.Where(e => !groupsByTitle.ContainsKey(e.Title)))
        {
            try
            {
                if (!string.IsNullOrEmpty(stale.GoogleId))
                    await _apiClient.DeleteEventAsync(calendar.GoogleId, stale.GoogleId);

                await service.PurgeEventAsync(stale.No);
                result.Deleted++;
                Debug.WriteLine($"[GoogleSync] 학사일정 삭제: {stale.Title}");
            }
            catch (Exception ex)
            {
                result.Errors++;
                result.ErrorMessages.Add($"{stale.Title} 삭제: {ex.Message}");
                Debug.WriteLine($"[GoogleSync] 학사일정 삭제 실패: {stale.Title} — {ex.Message}");
            }
        }

        result.Success = result.Errors == 0;
        Debug.WriteLine($"[GoogleSync] 학사일정 재조정 완료: {result.Summary}");
        return result;
    }

    private static GoogleEvent BuildScheduleGoogleEvent(SchoolScheduleGroup group) => new()
    {
        Summary = group.EventName,
        Description = group.IsVacation ? "휴업일" : null,
        Start = new GoogleEventDateTime { Date = group.StartDate.ToString("yyyy-MM-dd") },
        // Google Calendar 종일 이벤트는 end를 exclusive로 처리 (+1일)
        End = new GoogleEventDateTime { Date = group.EndDate.AddDays(1).ToString("yyyy-MM-dd") },
        Status = "confirmed"
    };

    /// <summary>
    /// 현재 학교의 "학사일정" 캘린더를 로컬(KCalendarList)+구글(Calendar) 양쪽에서 확보한다.
    /// 학교 코드가 바뀌면 새 캘린더가 생성되어 예전 학교 학사일정과 섞이지 않는다.
    /// SyncMode는 항상 "None"으로 고정 — 이 캘린더는 이 메서드로만 관리되며 일반 Pull 대상이 아니다
    /// (그래도 GoogleId를 로컬에 저장해두므로, 혹시 Pull이 돌더라도 중복 생성되지 않는다).
    /// </summary>
    private async Task<KCalendarList> EnsureScheduleCalendarAsync(SchedulerService service)
    {
        string schoolCode = Settings.SchoolCode;
        string schoolName = string.IsNullOrWhiteSpace(Settings.SchoolName.Value) ? "학교" : Settings.SchoolName.Value;

        var calendar = await service.GetOrCreateCalendarForSchoolAsync(
            ScheduleCalendarTitle, schoolCode, ScheduleCalendarColor);

        if (string.IsNullOrEmpty(calendar.GoogleId))
        {
            var created = await _apiClient.InsertCalendarAsync(
                $"{schoolName} 학사일정", "NewSchool 앱에서 자동 관리되는 학사일정 캘린더입니다.");
            if (created?.Id == null)
                throw new InvalidOperationException("Google Calendar 생성 실패");

            calendar.GoogleId = created.Id;
            calendar.SyncMode = "None";
            calendar.Updated = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            await service.UpdateCalendarAsync(calendar);
        }

        return calendar;
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

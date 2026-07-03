using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Repositories;

namespace NewSchool.Scheduler.Repositories;

/// <summary>
/// KEvent 리포지토리 — Google Calendar Events DB 접근
/// </summary>
public class KEventRepository : BaseRepository
{
    public KEventRepository(string dbPath) : base(dbPath) { }

    /// <summary>UnitOfWork 공유 연결 생성자.</summary>
    public KEventRepository(Microsoft.Data.Sqlite.SqliteConnection connection) : base(connection) { }

    #region Create

    public async Task<int> CreateAsync(KEvent ev)
    {
        const string query = @"
            INSERT INTO KEvent (GoogleId, CalendarId, Title, Notes, Start, End,
                                IsAllday, Location, Status, ColorId, Recurrence, Updated, User,
                                ItemType, IsDone, Completed, SeriesId)
            VALUES (@GoogleId, @CalendarId, @Title, @Notes, @Start, @End,
                    @IsAllday, @Location, @Status, @ColorId, @Recurrence, @Updated, @User,
                    @ItemType, @IsDone, @Completed, @SeriesId);
            SELECT last_insert_rowid();";
        try
        {
            using var cmd = CreateCommand(query);
            AddParameters(cmd, ev);
            var result = await cmd.ExecuteScalarAsync();
            ev.No = Convert.ToInt32(result);
            LogInfo($"KEvent 생성: No={ev.No}, Title={ev.Title}");
            return ev.No;
        }
        catch (Exception ex)
        {
            LogError($"KEvent 생성 실패: {ev.Title}", ex);
            throw;
        }
    }

    #endregion

    #region Read

    public async Task<KEvent?> GetByIdAsync(int no)
    {
        const string query = "SELECT * FROM KEvent WHERE No = @No";
        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@No", no);
            using var reader = await cmd.ExecuteReaderAsync();
            return await reader.ReadAsync() ? Map(reader) : null;
        }
        catch (Exception ex)
        {
            LogError($"KEvent 조회 실패: No={no}", ex);
            throw;
        }
    }

    /// <summary>날짜 범위로 이벤트 조회 (다중일 이벤트 포함: Start~End 범위가 조회 기간과 겹치는 모든 이벤트)</summary>
    public async Task<List<KEvent>> GetByDateRangeAsync(DateTime startDate, int days = 1)
    {
        var list = new List<KEvent>();
        try
        {
            var from = DateTime.SpecifyKind(startDate.Date, DateTimeKind.Unspecified);
            var to   = DateTime.SpecifyKind(from.AddDays(days), DateTimeKind.Unspecified);

            // 시간 이벤트=UTC 문자열, 종일 이벤트=로컬 날짜(yyyy-MM-dd) 저장 → 각각의 경계로 비교
            const string query = @"
                SELECT * FROM KEvent
                WHERE Status <> 'cancelled'
                  AND ( (IsAllday = 0 AND Start < @ToUtc  AND End >= @FromUtc)
                     OR (IsAllday = 1 AND Start < @ToDate AND End >= @FromDate) )
                ORDER BY Start ASC, Title ASC";

            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@FromUtc",  DateTimeHelper.ToStandardString(from));
            cmd.Parameters.AddWithValue("@ToUtc",    DateTimeHelper.ToStandardString(to));
            cmd.Parameters.AddWithValue("@FromDate", from.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@ToDate",   to.ToString("yyyy-MM-dd"));

            list = await ExecuteListAsync(cmd, Map);

            LogInfo($"KEvent 범위 조회: {list.Count}개 ({startDate:yyyy-MM-dd}, {days}일)");
            return list;
        }
        catch (Exception ex)
        {
            LogError($"KEvent 범위 조회 실패: {startDate:yyyy-MM-dd}", ex);
            throw;
        }
    }

    /// <summary>캘린더별 이벤트 조회</summary>
    public async Task<List<KEvent>> GetByCalendarIdAsync(int calendarId, DateTime startDate, int days = 30)
    {
        var list = new List<KEvent>();
        try
        {
            var from = DateTime.SpecifyKind(startDate.Date, DateTimeKind.Unspecified);
            var to   = DateTime.SpecifyKind(from.AddDays(days), DateTimeKind.Unspecified);

            const string query = @"
                SELECT * FROM KEvent
                WHERE CalendarId = @CalendarId
                  AND Status <> 'cancelled'
                  AND ( (IsAllday = 0 AND Start < @ToUtc  AND End >= @FromUtc)
                     OR (IsAllday = 1 AND Start < @ToDate AND End >= @FromDate) )
                ORDER BY Start ASC";

            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@CalendarId", calendarId);
            cmd.Parameters.AddWithValue("@FromUtc",  DateTimeHelper.ToStandardString(from));
            cmd.Parameters.AddWithValue("@ToUtc",    DateTimeHelper.ToStandardString(to));
            cmd.Parameters.AddWithValue("@FromDate", from.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@ToDate",   to.ToString("yyyy-MM-dd"));

            list = await ExecuteListAsync(cmd, Map);

            return list;
        }
        catch (Exception ex)
        {
            LogError($"KEvent CalendarId={calendarId} 조회 실패", ex);
            throw;
        }
    }

    public async Task<int> GetCountAsync()
    {
        try
        {
            using var cmd = CreateCommand("SELECT COUNT(*) FROM KEvent");
            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }
        catch (Exception ex)
        {
            LogError("KEvent 개수 조회 실패", ex);
            throw;
        }
    }

    #endregion

    #region Update

    public async Task<bool> UpdateAsync(KEvent ev)
    {
        const string query = @"
            UPDATE KEvent SET GoogleId=@GoogleId, CalendarId=@CalendarId, Title=@Title,
                Notes=@Notes, Start=@Start, End=@End, IsAllday=@IsAllday, Location=@Location,
                Status=@Status, ColorId=@ColorId, Recurrence=@Recurrence, Updated=@Updated, User=@User,
                ItemType=@ItemType, IsDone=@IsDone, Completed=@Completed, SeriesId=@SeriesId
            WHERE No = @No";
        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@No", ev.No);
            AddParameters(cmd, ev);
            bool ok = await cmd.ExecuteNonQueryAsync() > 0;
            if (ok) LogInfo($"KEvent 수정: No={ev.No}");
            else LogWarning($"KEvent 수정 실패(미존재): No={ev.No}");
            return ok;
        }
        catch (Exception ex)
        {
            LogError($"KEvent 수정 실패: No={ev.No}", ex);
            throw;
        }
    }

    #endregion

    #region Delete

    public async Task<bool> DeleteAsync(int no)
    {
        try
        {
            using var cmd = CreateCommand("DELETE FROM KEvent WHERE No = @No");
            cmd.Parameters.AddWithValue("@No", no);
            bool ok = await cmd.ExecuteNonQueryAsync() > 0;
            if (ok) LogInfo($"KEvent 삭제: No={no}");
            return ok;
        }
        catch (Exception ex)
        {
            LogError($"KEvent 삭제 실패: No={no}", ex);
            throw;
        }
    }

    /// <summary>
    /// soft-delete: Status='cancelled' 로 표시. 동기화된 이벤트의 삭제를 Google 에 전파하기 위함.
    /// (GetDeletedWithGoogleIdAsync 가 이 상태를 찾아 Google 에서 삭제 후 영구삭제)
    /// </summary>
    public async Task<bool> MarkCancelledAsync(int no)
    {
        try
        {
            using var cmd = CreateCommand("UPDATE KEvent SET Status='cancelled' WHERE No = @No");
            cmd.Parameters.AddWithValue("@No", no);
            bool ok = await cmd.ExecuteNonQueryAsync() > 0;
            if (ok) LogInfo($"KEvent soft-delete(cancelled): No={no}");
            return ok;
        }
        catch (Exception ex)
        {
            LogError($"KEvent soft-delete 실패: No={no}", ex);
            throw;
        }
    }

    #endregion

    #region Helpers

    private static void AddParameters(SqliteCommand cmd, KEvent ev)
    {
        cmd.Parameters.AddWithValue("@GoogleId",   ev.GoogleId   ?? string.Empty);
        cmd.Parameters.AddWithValue("@CalendarId", ev.CalendarId);
        cmd.Parameters.AddWithValue("@Title",      ev.Title      ?? string.Empty);
        cmd.Parameters.AddWithValue("@Notes",      ev.Notes      ?? string.Empty);

        if (ev.IsAllday)
        {
            // 종일 이벤트: 날짜만 저장 (UTC 변환하면 시간대에 따라 날짜가 밀릴 수 있음)
            cmd.Parameters.AddWithValue("@Start", ev.Start.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@End",   ev.End.ToString("yyyy-MM-dd"));
        }
        else
        {
            var startU = DateTime.SpecifyKind(ev.Start, DateTimeKind.Unspecified);
            var endU   = DateTime.SpecifyKind(ev.End,   DateTimeKind.Unspecified);
            cmd.Parameters.AddWithValue("@Start", DateTimeHelper.ToStandardString(startU));
            cmd.Parameters.AddWithValue("@End",   DateTimeHelper.ToStandardString(endU));
        }
        cmd.Parameters.AddWithValue("@IsAllday",   ev.IsAllday ? 1 : 0);
        cmd.Parameters.AddWithValue("@Location",   ev.Location   ?? string.Empty);
        cmd.Parameters.AddWithValue("@Status",     ev.Status     ?? "confirmed");
        cmd.Parameters.AddWithValue("@ColorId",    ev.ColorId    ?? string.Empty);
        cmd.Parameters.AddWithValue("@Recurrence", ev.Recurrence ?? string.Empty);
        cmd.Parameters.AddWithValue("@Updated",    ev.Updated    ?? string.Empty);
        cmd.Parameters.AddWithValue("@User",       ev.User       ?? string.Empty);
        cmd.Parameters.AddWithValue("@ItemType",  ev.ItemType  ?? "event");
        cmd.Parameters.AddWithValue("@IsDone",    ev.IsDone ? 1 : 0);
        cmd.Parameters.AddWithValue("@Completed", ev.Completed ?? string.Empty);
        cmd.Parameters.AddWithValue("@SeriesId",  ev.SeriesId ?? string.Empty);
    }

    // 컬럼 인덱스 캐시 기반 매핑 (행마다 GetOrdinal 반복 호출 제거)
    private static KEvent Map(SqliteDataReader r, ReaderColumnCache c)
    {
        var isAllday = r.GetInt32(c.GetOrdinal("IsAllday")) == 1;
        var startStr = r.GetString(c.GetOrdinal("Start"));
        var endStr   = r.GetString(c.GetOrdinal("End"));

        // 종일 이벤트: 날짜 전용 문자열("yyyy-MM-dd")로 저장되므로 시간대 변환 없이 파싱
        // 시간 이벤트: UTC 문자열로 저장되므로 Local 변환 포함 파싱
        DateTime start, end;
        if (isAllday)
        {
            start = DateTimeHelper.FromDateString(startStr);
            end   = DateTimeHelper.FromDateString(endStr);
        }
        else
        {
            start = DateTimeHelper.FromString(startStr);
            end   = DateTimeHelper.FromString(endStr);
        }

        var ev = new KEvent
        {
            No          = r.GetInt32(c.GetOrdinal("No")),
            GoogleId    = r.GetString(c.GetOrdinal("GoogleId")),
            CalendarId  = r.GetInt32(c.GetOrdinal("CalendarId")),
            Title       = r.GetString(c.GetOrdinal("Title")),
            Notes       = r.GetString(c.GetOrdinal("Notes")),
            Start       = start,
            End         = end,
            IsAllday    = isAllday,
            Location    = r.GetString(c.GetOrdinal("Location")),
            Status      = r.GetString(c.GetOrdinal("Status")),
            ColorId     = r.GetString(c.GetOrdinal("ColorId")),
            Recurrence  = r.GetString(c.GetOrdinal("Recurrence")),
            Updated     = r.GetString(c.GetOrdinal("Updated")),
            User        = r.GetString(c.GetOrdinal("User"))
        };
        // Ktask 통합 컬럼 (기존 DB 호환)
        ev.ItemType  = TryGetString(r, c, "ItemType", "event");
        ev.IsDone    = TryGetInt(r, c, "IsDone") == 1;
        ev.Completed = TryGetString(r, c, "Completed", string.Empty);
        ev.SeriesId  = TryGetString(r, c, "SeriesId", string.Empty);
        return ev;
    }

    // 단일 행 조회용 오버로드 (캐시 1회 초기화)
    private static KEvent Map(SqliteDataReader r)
    {
        var c = new ReaderColumnCache();
        c.Initialize(r);
        return Map(r, c);
    }

    private static string TryGetString(SqliteDataReader r, ReaderColumnCache c, string col, string fallback)
        => c.TryGetOrdinal(col, out var i) && !r.IsDBNull(i) ? r.GetString(i) : fallback;

    private static int TryGetInt(SqliteDataReader r, ReaderColumnCache c, string col)
        => c.TryGetOrdinal(col, out var i) && !r.IsDBNull(i) ? r.GetInt32(i) : 0;

    #endregion

    #region Google Sync Queries

    /// <summary>GoogleId로 이벤트 조회</summary>
    public async Task<KEvent?> GetByGoogleIdAsync(string googleId)
    {
        const string query = "SELECT * FROM KEvent WHERE GoogleId = @GoogleId LIMIT 1";
        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@GoogleId", googleId);
            using var reader = await cmd.ExecuteReaderAsync();
            return await reader.ReadAsync() ? Map(reader) : null;
        }
        catch (Exception ex)
        {
            LogError($"KEvent GoogleId 조회 실패: '{googleId}'", ex);
            throw;
        }
    }

    /// <summary>GoogleId가 비어있는(아직 동기화 안 된) 이벤트 조회</summary>
    public async Task<List<KEvent>> GetUnsyncedAsync(int calendarId)
    {
        const string query = @"
            SELECT * FROM KEvent
            WHERE CalendarId = @CalendarId AND (GoogleId = '' OR GoogleId IS NULL)
              AND Status <> 'cancelled'
            ORDER BY Start ASC";
        var list = new List<KEvent>();
        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@CalendarId", calendarId);
            list = await ExecuteListAsync(cmd, Map);
            return list;
        }
        catch (Exception ex)
        {
            LogError($"미동기화 이벤트 조회 실패: CalendarId={calendarId}", ex);
            throw;
        }
    }

    /// <summary>특정 시각 이후 수정된 이벤트 조회 (push 용)</summary>
    public async Task<List<KEvent>> GetModifiedSinceAsync(int calendarId, string sinceUtc)
    {
        const string query = @"
            SELECT * FROM KEvent
            WHERE CalendarId = @CalendarId AND GoogleId <> '' AND Updated > @Since
            ORDER BY Updated ASC";
        var list = new List<KEvent>();
        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@CalendarId", calendarId);
            cmd.Parameters.AddWithValue("@Since", sinceUtc);
            list = await ExecuteListAsync(cmd, Map);
            return list;
        }
        catch (Exception ex)
        {
            LogError($"수정된 이벤트 조회 실패: CalendarId={calendarId}", ex);
            throw;
        }
    }

    /// <summary>캘린더의 삭제(cancelled) 상태 이벤트 중 GoogleId가 있는 것</summary>
    public async Task<List<KEvent>> GetDeletedWithGoogleIdAsync(int calendarId)
    {
        const string query = @"
            SELECT * FROM KEvent
            WHERE CalendarId = @CalendarId AND GoogleId <> '' AND Status = 'cancelled'";
        var list = new List<KEvent>();
        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@CalendarId", calendarId);
            list = await ExecuteListAsync(cmd, Map);
            return list;
        }
        catch (Exception ex)
        {
            LogError($"삭제된 이벤트 조회 실패: CalendarId={calendarId}", ex);
            throw;
        }
    }

    #endregion

    #region Task Queries (Ktask 통합)

    /// <summary>날짜 범위로 할 일(task) 조회</summary>
    public async Task<List<KEvent>> GetTasksByDateRangeAsync(DateTime startDate, int days = 1, bool showCompleted = true)
    {
        var list = new List<KEvent>();
        try
        {
            var from = DateTime.SpecifyKind(startDate.Date, DateTimeKind.Unspecified);
            var to   = DateTime.SpecifyKind(from.AddDays(days), DateTimeKind.Unspecified);

            string query = @"
                SELECT * FROM KEvent
                WHERE ItemType = 'task'
                  AND Status <> 'cancelled'
                  AND ( (IsAllday = 0 AND Start < @ToUtc  AND End >= @FromUtc)
                     OR (IsAllday = 1 AND Start < @ToDate AND End >= @FromDate) )";
            if (!showCompleted)
                query += " AND IsDone = 0";
            query += " ORDER BY Start ASC, Title ASC";

            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@FromUtc",  DateTimeHelper.ToStandardString(from));
            cmd.Parameters.AddWithValue("@ToUtc",    DateTimeHelper.ToStandardString(to));
            cmd.Parameters.AddWithValue("@FromDate", from.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@ToDate",   to.ToString("yyyy-MM-dd"));

            list = await ExecuteListAsync(cmd, Map);

            LogInfo($"Task 범위 조회: {list.Count}개 ({startDate:yyyy-MM-dd}, {days}일)");
        }
        catch (Exception ex)
        {
            LogError($"Task 범위 조회 실패: {startDate:yyyy-MM-dd}", ex);
            throw;
        }
        return list;
    }

    /// <summary>오늘 기준 미완료 + 미래 할 일 조회</summary>
    public async Task<List<KEvent>> GetPendingAndFutureTasksAsync(DateTime today)
    {
        var list = new List<KEvent>();
        try
        {
            var todayUtc  = DateTimeHelper.ToStandardString(
                DateTime.SpecifyKind(today.Date, DateTimeKind.Unspecified));
            var todayDate = today.Date.ToString("yyyy-MM-dd");

            // 종일=로컬 날짜, 시간=UTC 로 저장되므로 '오늘 기준' 비교도 각각의 형식으로
            const string query = @"
                SELECT * FROM KEvent
                WHERE ItemType = 'task'
                  AND Status <> 'cancelled'
                  AND ( ( ((IsAllday = 0 AND Start < @TodayUtc) OR (IsAllday = 1 AND Start < @TodayDate)) AND IsDone = 0 )
                     OR ( (IsAllday = 0 AND Start >= @TodayUtc) OR (IsAllday = 1 AND Start >= @TodayDate) ) )
                ORDER BY Start ASC, Title ASC";

            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@TodayUtc",  todayUtc);
            cmd.Parameters.AddWithValue("@TodayDate", todayDate);

            list = await ExecuteListAsync(cmd, Map);

            LogInfo($"미완료+미래 Task 조회: {list.Count}개 (기준: {today:yyyy-MM-dd})");
        }
        catch (Exception ex)
        {
            LogError($"미완료+미래 Task 조회 실패", ex);
            throw;
        }
        return list;
    }

    /// <summary>캘린더별 미완료+미래 할 일 조회</summary>
    public async Task<List<KEvent>> GetTasksByCalendarIdPendingAsync(int calendarId, DateTime today)
    {
        var list = new List<KEvent>();
        try
        {
            var todayUtc  = DateTimeHelper.ToStandardString(
                DateTime.SpecifyKind(today.Date, DateTimeKind.Unspecified));
            var todayDate = today.Date.ToString("yyyy-MM-dd");

            const string query = @"
                SELECT * FROM KEvent
                WHERE ItemType = 'task' AND CalendarId = @CalendarId
                  AND Status <> 'cancelled'
                  AND ( ( ((IsAllday = 0 AND Start < @TodayUtc) OR (IsAllday = 1 AND Start < @TodayDate)) AND IsDone = 0 )
                     OR ( (IsAllday = 0 AND Start >= @TodayUtc) OR (IsAllday = 1 AND Start >= @TodayDate) ) )
                ORDER BY Start ASC, Title ASC";

            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@CalendarId", calendarId);
            cmd.Parameters.AddWithValue("@TodayUtc",  todayUtc);
            cmd.Parameters.AddWithValue("@TodayDate", todayDate);

            list = await ExecuteListAsync(cmd, Map);
        }
        catch (Exception ex)
        {
            LogError($"CalendarId={calendarId} Task 조회 실패", ex);
            throw;
        }
        return list;
    }

    /// <summary>전체 할 일 조회</summary>
    public async Task<List<KEvent>> GetAllTasksAsync(bool showCompleted = true)
    {
        var list = new List<KEvent>();
        try
        {
            string query = "SELECT * FROM KEvent WHERE ItemType = 'task' AND Status <> 'cancelled'";
            if (!showCompleted)
                query += " AND IsDone = 0";
            query += " ORDER BY Start ASC";

            using var cmd = CreateCommand(query);
            list = await ExecuteListAsync(cmd, Map);
        }
        catch (Exception ex)
        {
            LogError("전체 Task 조회 실패", ex);
            throw;
        }
        return list;
    }

    /// <summary>
    /// 같은 반복 시리즈의 항목 중 기준일 이후(포함) 것만 조회 — "이후 반복 항목 모두 삭제"용
    /// </summary>
    public async Task<List<KEvent>> GetBySeriesIdFromAsync(string seriesId, DateTime fromDate)
    {
        var list = new List<KEvent>();
        if (string.IsNullOrEmpty(seriesId)) return list;

        try
        {
            var fromUtc  = DateTimeHelper.ToStandardString(DateTime.SpecifyKind(fromDate.Date, DateTimeKind.Unspecified));
            var fromDateStr = fromDate.Date.ToString("yyyy-MM-dd");

            const string query = @"
                SELECT * FROM KEvent
                WHERE SeriesId = @SeriesId
                  AND Status <> 'cancelled'
                  AND ( (IsAllday = 0 AND Start >= @FromUtc) OR (IsAllday = 1 AND Start >= @FromDate) )
                ORDER BY Start ASC";

            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@SeriesId", seriesId);
            cmd.Parameters.AddWithValue("@FromUtc", fromUtc);
            cmd.Parameters.AddWithValue("@FromDate", fromDateStr);

            list = await ExecuteListAsync(cmd, Map);
        }
        catch (Exception ex)
        {
            LogError($"시리즈 조회 실패: SeriesId={seriesId}", ex);
            throw;
        }
        return list;
    }

    /// <summary>할 일 개수 조회</summary>
    public async Task<int> GetTaskCountAsync(bool onlyIncomplete = false)
    {
        try
        {
            string query = "SELECT COUNT(*) FROM KEvent WHERE ItemType = 'task' AND Status <> 'cancelled'";
            if (onlyIncomplete)
                query += " AND IsDone = 0";

            using var cmd = CreateCommand(query);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }
        catch (Exception ex)
        {
            LogError("Task 개수 조회 실패", ex);
            throw;
        }
    }

    #endregion
}

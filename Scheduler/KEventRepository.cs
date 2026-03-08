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

    #region Create

    public async Task<int> CreateAsync(KEvent ev)
    {
        const string query = @"
            INSERT INTO KEvent (GoogleId, CalendarId, Title, Notes, Start, End,
                                IsAllday, Location, Status, ColorId, Recurrence, Updated, User,
                                ItemType, IsDone, Completed)
            VALUES (@GoogleId, @CalendarId, @Title, @Notes, @Start, @End,
                    @IsAllday, @Location, @Status, @ColorId, @Recurrence, @Updated, @User,
                    @ItemType, @IsDone, @Completed);
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

            const string query = @"
                SELECT * FROM KEvent
                WHERE Start < @To AND End >= @From
                  AND Status <> 'cancelled'
                ORDER BY Start ASC, Title ASC";

            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@From", DateTimeHelper.ToStandardString(from));
            cmd.Parameters.AddWithValue("@To",   DateTimeHelper.ToStandardString(to));

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                list.Add(Map(reader));

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
                  AND Start < @To AND End >= @From
                  AND Status <> 'cancelled'
                ORDER BY Start ASC";

            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@CalendarId", calendarId);
            cmd.Parameters.AddWithValue("@From", DateTimeHelper.ToStandardString(from));
            cmd.Parameters.AddWithValue("@To",   DateTimeHelper.ToStandardString(to));

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                list.Add(Map(reader));

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
                ItemType=@ItemType, IsDone=@IsDone, Completed=@Completed
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
    }

    private static KEvent Map(SqliteDataReader r)
    {
        var isAllday = r.GetInt32(r.GetOrdinal("IsAllday")) == 1;
        var startStr = r.GetString(r.GetOrdinal("Start"));
        var endStr   = r.GetString(r.GetOrdinal("End"));

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
            No          = r.GetInt32(r.GetOrdinal("No")),
            GoogleId    = r.GetString(r.GetOrdinal("GoogleId")),
            CalendarId  = r.GetInt32(r.GetOrdinal("CalendarId")),
            Title       = r.GetString(r.GetOrdinal("Title")),
            Notes       = r.GetString(r.GetOrdinal("Notes")),
            Start       = start,
            End         = end,
            IsAllday    = isAllday,
            Location    = r.GetString(r.GetOrdinal("Location")),
            Status      = r.GetString(r.GetOrdinal("Status")),
            ColorId     = r.GetString(r.GetOrdinal("ColorId")),
            Recurrence  = r.GetString(r.GetOrdinal("Recurrence")),
            Updated     = r.GetString(r.GetOrdinal("Updated")),
            User        = r.GetString(r.GetOrdinal("User"))
        };
        // Ktask 통합 컬럼 (기존 DB 호환)
        ev.ItemType  = TryGetString(r, "ItemType", "event");
        ev.IsDone    = TryGetInt(r, "IsDone") == 1;
        ev.Completed = TryGetString(r, "Completed", string.Empty);
        return ev;
    }

    private static string TryGetString(SqliteDataReader r, string col, string fallback)
    {
        try { var i = r.GetOrdinal(col); return r.IsDBNull(i) ? fallback : r.GetString(i); }
        catch { return fallback; }
    }

    private static int TryGetInt(SqliteDataReader r, string col)
    {
        try { var i = r.GetOrdinal(col); return r.IsDBNull(i) ? 0 : r.GetInt32(i); }
        catch { return 0; }
    }

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
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                list.Add(Map(reader));
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
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                list.Add(Map(reader));
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
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                list.Add(Map(reader));
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
                  AND Start < @To AND End >= @From
                  AND Status <> 'cancelled'";
            if (!showCompleted)
                query += " AND IsDone = 0";
            query += " ORDER BY Start ASC, Title ASC";

            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@From", DateTimeHelper.ToStandardString(from));
            cmd.Parameters.AddWithValue("@To",   DateTimeHelper.ToStandardString(to));

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                list.Add(Map(reader));

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
            var todayStr = DateTimeHelper.ToStandardString(
                DateTime.SpecifyKind(today.Date, DateTimeKind.Unspecified));

            const string query = @"
                SELECT * FROM KEvent
                WHERE ItemType = 'task'
                  AND Status <> 'cancelled'
                  AND ((Start < @Today AND IsDone = 0) OR (Start >= @Today))
                ORDER BY Start ASC, Title ASC";

            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@Today", todayStr);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                list.Add(Map(reader));

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
            var todayStr = DateTimeHelper.ToStandardString(
                DateTime.SpecifyKind(today.Date, DateTimeKind.Unspecified));

            const string query = @"
                SELECT * FROM KEvent
                WHERE ItemType = 'task' AND CalendarId = @CalendarId
                  AND Status <> 'cancelled'
                  AND ((Start < @Today AND IsDone = 0) OR (Start >= @Today))
                ORDER BY Start ASC, Title ASC";

            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@CalendarId", calendarId);
            cmd.Parameters.AddWithValue("@Today", todayStr);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                list.Add(Map(reader));
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
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                list.Add(Map(reader));
        }
        catch (Exception ex)
        {
            LogError("전체 Task 조회 실패", ex);
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

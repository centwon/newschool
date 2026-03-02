using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Models;

namespace NewSchool.Repositories;

/// <summary>
/// SchoolEvent Repository
/// 학교 일정 CRUD
/// </summary>
public class SchoolEventRepository : BaseRepository
{
    public SchoolEventRepository(string dbPath) : base(dbPath)
    {
        EnsureTableExists();
    }

    private void EnsureTableExists()
    {
        const string sql = @"
            CREATE TABLE IF NOT EXISTS SchoolEvent (
                No          INTEGER PRIMARY KEY AUTOINCREMENT,
                TeacherID   TEXT    NOT NULL,
                SchoolCode  TEXT    NOT NULL DEFAULT '',
                Year        INTEGER NOT NULL,
                Category    INTEGER NOT NULL DEFAULT 0,
                Title       TEXT    NOT NULL DEFAULT '',
                Description TEXT    NOT NULL DEFAULT '',
                StartDate   TEXT    NOT NULL,
                EndDate     TEXT    NOT NULL,
                IsAllDay    INTEGER NOT NULL DEFAULT 1,
                StartTime   TEXT    NOT NULL DEFAULT '',
                EndTime     TEXT    NOT NULL DEFAULT '',
                Location    TEXT    NOT NULL DEFAULT '',
                Color       TEXT    NOT NULL DEFAULT '#FF339AF0',
                IsImportant INTEGER NOT NULL DEFAULT 0,
                Repeat      INTEGER NOT NULL DEFAULT 0,
                CreatedAt   TEXT    NOT NULL,
                UpdatedAt   TEXT    NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_SchoolEvent_Date ON SchoolEvent(StartDate, EndDate);
            CREATE INDEX IF NOT EXISTS IX_SchoolEvent_Teacher ON SchoolEvent(TeacherID, Year);";

        using var cmd = CreateCommand(sql);
        cmd.ExecuteNonQuery();
    }

    #region Create

    public async Task<int> InsertAsync(SchoolEvent evt)
    {
        const string sql = @"
            INSERT INTO SchoolEvent (
                TeacherID, SchoolCode, Year, Category, Title, Description,
                StartDate, EndDate, IsAllDay, StartTime, EndTime,
                Location, Color, IsImportant, Repeat, CreatedAt, UpdatedAt
            ) VALUES (
                @TeacherID, @SchoolCode, @Year, @Category, @Title, @Description,
                @StartDate, @EndDate, @IsAllDay, @StartTime, @EndTime,
                @Location, @Color, @IsImportant, @Repeat, @CreatedAt, @UpdatedAt
            );
            SELECT last_insert_rowid();";

        using var cmd = CreateCommand(sql);
        AddParameters(cmd, evt);

        var result = await cmd.ExecuteScalarAsync();
        evt.No = Convert.ToInt32(result);
        return evt.No;
    }

    #endregion

    #region Read

    public async Task<SchoolEvent?> GetByNoAsync(int no)
    {
        const string sql = "SELECT * FROM SchoolEvent WHERE No = @No";
        using var cmd = CreateCommand(sql);
        cmd.Parameters.AddWithValue("@No", no);

        using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapFromReader(reader) : null;
    }

    /// <summary>
    /// 특정 월에 포함되는 일정 조회 (시작~종료 범위가 해당 월과 겹치는 일정)
    /// </summary>
    public async Task<List<SchoolEvent>> GetByMonthAsync(string teacherId, int year, int month)
    {
        var monthStart = new DateTime(year, month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);

        const string sql = @"
            SELECT * FROM SchoolEvent
            WHERE TeacherID = @TeacherID
              AND StartDate <= @MonthEnd AND EndDate >= @MonthStart
            ORDER BY StartDate, No";

        using var cmd = CreateCommand(sql);
        cmd.Parameters.AddWithValue("@TeacherID", teacherId);
        cmd.Parameters.AddWithValue("@MonthStart", monthStart.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@MonthEnd", monthEnd.ToString("yyyy-MM-dd"));

        return await ReadListAsync(cmd);
    }

    /// <summary>
    /// 특정 날짜에 포함되는 일정 조회
    /// </summary>
    public async Task<List<SchoolEvent>> GetByDateAsync(string teacherId, DateTime date)
    {
        const string sql = @"
            SELECT * FROM SchoolEvent
            WHERE TeacherID = @TeacherID
              AND StartDate <= @Date AND EndDate >= @Date
            ORDER BY IsAllDay DESC, StartTime, No";

        using var cmd = CreateCommand(sql);
        cmd.Parameters.AddWithValue("@TeacherID", teacherId);
        cmd.Parameters.AddWithValue("@Date", date.ToString("yyyy-MM-dd"));

        return await ReadListAsync(cmd);
    }

    /// <summary>
    /// 다가오는 일정 조회 (오늘 이후, 제한 개수)
    /// </summary>
    public async Task<List<SchoolEvent>> GetUpcomingAsync(string teacherId, int limit = 10)
    {
        const string sql = @"
            SELECT * FROM SchoolEvent
            WHERE TeacherID = @TeacherID AND EndDate >= @Today
            ORDER BY StartDate, No
            LIMIT @Limit";

        using var cmd = CreateCommand(sql);
        cmd.Parameters.AddWithValue("@TeacherID", teacherId);
        cmd.Parameters.AddWithValue("@Today", DateTime.Today.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@Limit", limit);

        return await ReadListAsync(cmd);
    }

    #endregion

    #region Update

    public async Task UpdateAsync(SchoolEvent evt)
    {
        const string sql = @"
            UPDATE SchoolEvent SET
                SchoolCode = @SchoolCode, Year = @Year, Category = @Category,
                Title = @Title, Description = @Description,
                StartDate = @StartDate, EndDate = @EndDate,
                IsAllDay = @IsAllDay, StartTime = @StartTime, EndTime = @EndTime,
                Location = @Location, Color = @Color,
                IsImportant = @IsImportant, Repeat = @Repeat, UpdatedAt = @UpdatedAt
            WHERE No = @No";

        evt.UpdatedAt = DateTime.Now;
        using var cmd = CreateCommand(sql);
        AddParameters(cmd, evt);
        cmd.Parameters.AddWithValue("@No", evt.No);

        await cmd.ExecuteNonQueryAsync();
    }

    #endregion

    #region Delete

    public async Task DeleteAsync(int no)
    {
        const string sql = "DELETE FROM SchoolEvent WHERE No = @No";
        using var cmd = CreateCommand(sql);
        cmd.Parameters.AddWithValue("@No", no);
        await cmd.ExecuteNonQueryAsync();
    }

    #endregion

    #region Helpers

    private void AddParameters(SqliteCommand cmd, SchoolEvent evt)
    {
        cmd.Parameters.AddWithValue("@TeacherID", evt.TeacherID);
        cmd.Parameters.AddWithValue("@SchoolCode", evt.SchoolCode);
        cmd.Parameters.AddWithValue("@Year", evt.Year);
        cmd.Parameters.AddWithValue("@Category", (int)evt.Category);
        cmd.Parameters.AddWithValue("@Title", evt.Title);
        cmd.Parameters.AddWithValue("@Description", evt.Description);
        cmd.Parameters.AddWithValue("@StartDate", evt.StartDate.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@EndDate", evt.EndDate.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@IsAllDay", evt.IsAllDay ? 1 : 0);
        cmd.Parameters.AddWithValue("@StartTime", evt.StartTime.ToString(@"hh\:mm"));
        cmd.Parameters.AddWithValue("@EndTime", evt.EndTime.ToString(@"hh\:mm"));
        cmd.Parameters.AddWithValue("@Location", evt.Location);
        cmd.Parameters.AddWithValue("@Color", evt.Color);
        cmd.Parameters.AddWithValue("@IsImportant", evt.IsImportant ? 1 : 0);
        cmd.Parameters.AddWithValue("@Repeat", (int)evt.Repeat);
        cmd.Parameters.AddWithValue("@CreatedAt", evt.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("@UpdatedAt", evt.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
    }

    private async Task<List<SchoolEvent>> ReadListAsync(SqliteCommand cmd)
    {
        var list = new List<SchoolEvent>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            list.Add(MapFromReader(reader));
        return list;
    }

    private static SchoolEvent MapFromReader(SqliteDataReader reader)
    {
        return new SchoolEvent
        {
            No = reader.GetInt32(reader.GetOrdinal("No")),
            TeacherID = reader.GetString(reader.GetOrdinal("TeacherID")),
            SchoolCode = reader.GetString(reader.GetOrdinal("SchoolCode")),
            Year = reader.GetInt32(reader.GetOrdinal("Year")),
            Category = (EventCategory)reader.GetInt32(reader.GetOrdinal("Category")),
            Title = reader.GetString(reader.GetOrdinal("Title")),
            Description = reader.GetString(reader.GetOrdinal("Description")),
            StartDate = DateTime.Parse(reader.GetString(reader.GetOrdinal("StartDate"))),
            EndDate = DateTime.Parse(reader.GetString(reader.GetOrdinal("EndDate"))),
            IsAllDay = reader.GetInt32(reader.GetOrdinal("IsAllDay")) == 1,
            StartTime = ParseTimeSpanSafe(reader, "StartTime"),
            EndTime = ParseTimeSpanSafe(reader, "EndTime"),
            Location = reader.GetString(reader.GetOrdinal("Location")),
            Color = reader.GetString(reader.GetOrdinal("Color")),
            IsImportant = reader.GetInt32(reader.GetOrdinal("IsImportant")) == 1,
            Repeat = (EventRepeat)reader.GetInt32(reader.GetOrdinal("Repeat")),
            CreatedAt = ParseDateTimeSafe(reader, "CreatedAt"),
            UpdatedAt = ParseDateTimeSafe(reader, "UpdatedAt")
        };
    }

    private static TimeSpan ParseTimeSpanSafe(SqliteDataReader reader, string column)
    {
        try
        {
            var ordinal = reader.GetOrdinal(column);
            if (reader.IsDBNull(ordinal)) return TimeSpan.Zero;
            var str = reader.GetString(ordinal);
            return TimeSpan.TryParse(str, out var ts) ? ts : TimeSpan.Zero;
        }
        catch { return TimeSpan.Zero; }
    }

    private static DateTime ParseDateTimeSafe(SqliteDataReader reader, string column)
    {
        try
        {
            var ordinal = reader.GetOrdinal(column);
            if (reader.IsDBNull(ordinal)) return DateTime.Now;
            return DateTime.TryParse(reader.GetString(ordinal), out var dt) ? dt : DateTime.Now;
        }
        catch { return DateTime.Now; }
    }

    #endregion
}

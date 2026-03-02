using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Models;

namespace NewSchool.Repositories;

/// <summary>
/// WorkLog Repository
/// 업무 일지 CRUD
/// </summary>
public class WorkLogRepository : BaseRepository
{
    public WorkLogRepository(string dbPath) : base(dbPath)
    {
        EnsureTableExists();
    }

    private void EnsureTableExists()
    {
        const string sql = @"
            CREATE TABLE IF NOT EXISTS WorkLog (
                No          INTEGER PRIMARY KEY AUTOINCREMENT,
                TeacherID   TEXT    NOT NULL,
                Date        TEXT    NOT NULL,
                Category    INTEGER NOT NULL DEFAULT 0,
                Title       TEXT    NOT NULL DEFAULT '',
                Content     TEXT    NOT NULL DEFAULT '',
                Tag         TEXT    NOT NULL DEFAULT '',
                IsImportant INTEGER NOT NULL DEFAULT 0,
                CreatedAt   TEXT    NOT NULL,
                UpdatedAt   TEXT    NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_WorkLog_Teacher_Date ON WorkLog(TeacherID, Date);";

        using var cmd = CreateCommand(sql);
        cmd.ExecuteNonQuery();
    }

    #region Create

    public async Task<int> InsertAsync(WorkLog log)
    {
        const string sql = @"
            INSERT INTO WorkLog (TeacherID, Date, Category, Title, Content, Tag, IsImportant, CreatedAt, UpdatedAt)
            VALUES (@TeacherID, @Date, @Category, @Title, @Content, @Tag, @IsImportant, @CreatedAt, @UpdatedAt);
            SELECT last_insert_rowid();";

        using var cmd = CreateCommand(sql);
        AddParameters(cmd, log);

        var result = await cmd.ExecuteScalarAsync();
        log.No = Convert.ToInt32(result);
        return log.No;
    }

    #endregion

    #region Read

    public async Task<WorkLog?> GetByNoAsync(int no)
    {
        const string sql = "SELECT * FROM WorkLog WHERE No = @No";
        using var cmd = CreateCommand(sql);
        cmd.Parameters.AddWithValue("@No", no);

        using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapFromReader(reader) : null;
    }

    public async Task<List<WorkLog>> GetByTeacherAsync(string teacherId, DateTime startDate, DateTime endDate)
    {
        const string sql = @"
            SELECT * FROM WorkLog
            WHERE TeacherID = @TeacherID AND Date BETWEEN @Start AND @End
            ORDER BY Date DESC, No DESC";

        using var cmd = CreateCommand(sql);
        cmd.Parameters.AddWithValue("@TeacherID", teacherId);
        cmd.Parameters.AddWithValue("@Start", startDate.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@End", endDate.ToString("yyyy-MM-dd"));

        return await ReadListAsync(cmd);
    }

    public async Task<List<WorkLog>> GetByDateAsync(string teacherId, DateTime date)
    {
        const string sql = @"
            SELECT * FROM WorkLog
            WHERE TeacherID = @TeacherID AND Date = @Date
            ORDER BY No DESC";

        using var cmd = CreateCommand(sql);
        cmd.Parameters.AddWithValue("@TeacherID", teacherId);
        cmd.Parameters.AddWithValue("@Date", date.ToString("yyyy-MM-dd"));

        return await ReadListAsync(cmd);
    }

    public async Task<List<WorkLog>> SearchAsync(string teacherId, string keyword)
    {
        const string sql = @"
            SELECT * FROM WorkLog
            WHERE TeacherID = @TeacherID 
              AND (Title LIKE @Keyword OR Content LIKE @Keyword OR Tag LIKE @Keyword)
            ORDER BY Date DESC, No DESC";

        using var cmd = CreateCommand(sql);
        cmd.Parameters.AddWithValue("@TeacherID", teacherId);
        cmd.Parameters.AddWithValue("@Keyword", $"%{keyword}%");

        return await ReadListAsync(cmd);
    }

    #endregion

    #region Update

    public async Task UpdateAsync(WorkLog log)
    {
        const string sql = @"
            UPDATE WorkLog SET
                Date = @Date, Category = @Category, Title = @Title,
                Content = @Content, Tag = @Tag, IsImportant = @IsImportant,
                UpdatedAt = @UpdatedAt
            WHERE No = @No";

        log.UpdatedAt = DateTime.Now;
        using var cmd = CreateCommand(sql);
        AddParameters(cmd, log);
        cmd.Parameters.AddWithValue("@No", log.No);

        await cmd.ExecuteNonQueryAsync();
    }

    #endregion

    #region Delete

    public async Task DeleteAsync(int no)
    {
        const string sql = "DELETE FROM WorkLog WHERE No = @No";
        using var cmd = CreateCommand(sql);
        cmd.Parameters.AddWithValue("@No", no);
        await cmd.ExecuteNonQueryAsync();
    }

    #endregion

    #region Helpers

    private void AddParameters(SqliteCommand cmd, WorkLog log)
    {
        cmd.Parameters.AddWithValue("@TeacherID", log.TeacherID);
        cmd.Parameters.AddWithValue("@Date", log.Date.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@Category", (int)log.Category);
        cmd.Parameters.AddWithValue("@Title", log.Title);
        cmd.Parameters.AddWithValue("@Content", log.Content);
        cmd.Parameters.AddWithValue("@Tag", log.Tag);
        cmd.Parameters.AddWithValue("@IsImportant", log.IsImportant ? 1 : 0);
        cmd.Parameters.AddWithValue("@CreatedAt", log.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("@UpdatedAt", log.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
    }

    private async Task<List<WorkLog>> ReadListAsync(SqliteCommand cmd)
    {
        var list = new List<WorkLog>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            list.Add(MapFromReader(reader));
        return list;
    }

    private static WorkLog MapFromReader(SqliteDataReader reader)
    {
        return new WorkLog
        {
            No = reader.GetInt32(reader.GetOrdinal("No")),
            TeacherID = reader.GetString(reader.GetOrdinal("TeacherID")),
            Date = DateTime.Parse(reader.GetString(reader.GetOrdinal("Date"))),
            Category = (WorkLogCategory)reader.GetInt32(reader.GetOrdinal("Category")),
            Title = reader.GetString(reader.GetOrdinal("Title")),
            Content = reader.GetString(reader.GetOrdinal("Content")),
            Tag = reader.GetString(reader.GetOrdinal("Tag")),
            IsImportant = reader.GetInt32(reader.GetOrdinal("IsImportant")) == 1,
            CreatedAt = ParseDateTimeSafe(reader, "CreatedAt"),
            UpdatedAt = ParseDateTimeSafe(reader, "UpdatedAt")
        };
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

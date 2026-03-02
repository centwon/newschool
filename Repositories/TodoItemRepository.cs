using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Models;

namespace NewSchool.Repositories;

/// <summary>
/// TodoItem Repository
/// 할 일 CRUD
/// </summary>
public class TodoItemRepository : BaseRepository
{
    public TodoItemRepository(string dbPath) : base(dbPath)
    {
        EnsureTableExists();
    }

    private void EnsureTableExists()
    {
        const string sql = @"
            CREATE TABLE IF NOT EXISTS TodoItem (
                No          INTEGER PRIMARY KEY AUTOINCREMENT,
                TeacherID   TEXT    NOT NULL,
                Title       TEXT    NOT NULL DEFAULT '',
                Description TEXT    NOT NULL DEFAULT '',
                Priority    INTEGER NOT NULL DEFAULT 1,
                Status      INTEGER NOT NULL DEFAULT 0,
                DueDate     TEXT,
                CompletedAt TEXT,
                Tag         TEXT    NOT NULL DEFAULT '',
                SortOrder   INTEGER NOT NULL DEFAULT 0,
                CreatedAt   TEXT    NOT NULL,
                UpdatedAt   TEXT    NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_TodoItem_Teacher ON TodoItem(TeacherID, Status);";

        using var cmd = CreateCommand(sql);
        cmd.ExecuteNonQuery();
    }

    #region Create

    public async Task<int> InsertAsync(TodoItem item)
    {
        const string sql = @"
            INSERT INTO TodoItem (
                TeacherID, Title, Description, Priority, Status,
                DueDate, CompletedAt, Tag, SortOrder, CreatedAt, UpdatedAt
            ) VALUES (
                @TeacherID, @Title, @Description, @Priority, @Status,
                @DueDate, @CompletedAt, @Tag, @SortOrder, @CreatedAt, @UpdatedAt
            );
            SELECT last_insert_rowid();";

        using var cmd = CreateCommand(sql);
        AddParameters(cmd, item);

        var result = await cmd.ExecuteScalarAsync();
        item.No = Convert.ToInt32(result);
        return item.No;
    }

    #endregion

    #region Read

    public async Task<TodoItem?> GetByNoAsync(int no)
    {
        const string sql = "SELECT * FROM TodoItem WHERE No = @No";
        using var cmd = CreateCommand(sql);
        cmd.Parameters.AddWithValue("@No", no);

        using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapFromReader(reader) : null;
    }

    /// <summary>
    /// 미완료 할 일 (우선순위 → 마감일 순)
    /// </summary>
    public async Task<List<TodoItem>> GetActiveAsync(string teacherId)
    {
        const string sql = @"
            SELECT * FROM TodoItem
            WHERE TeacherID = @TeacherID AND Status <> @CompletedStatus
            ORDER BY Priority DESC, 
                     CASE WHEN DueDate IS NULL THEN 1 ELSE 0 END,
                     DueDate ASC,
                     SortOrder, No";

        using var cmd = CreateCommand(sql);
        cmd.Parameters.AddWithValue("@TeacherID", teacherId);
        cmd.Parameters.AddWithValue("@CompletedStatus", (int)TodoStatus.완료);

        return await ReadListAsync(cmd);
    }

    /// <summary>
    /// 완료된 할 일 (최근 완료순)
    /// </summary>
    public async Task<List<TodoItem>> GetCompletedAsync(string teacherId, int limit = 50)
    {
        const string sql = @"
            SELECT * FROM TodoItem
            WHERE TeacherID = @TeacherID AND Status = @CompletedStatus
            ORDER BY CompletedAt DESC
            LIMIT @Limit";

        using var cmd = CreateCommand(sql);
        cmd.Parameters.AddWithValue("@TeacherID", teacherId);
        cmd.Parameters.AddWithValue("@CompletedStatus", (int)TodoStatus.완료);
        cmd.Parameters.AddWithValue("@Limit", limit);

        return await ReadListAsync(cmd);
    }

    /// <summary>
    /// 전체 (미완료 + 완료)
    /// </summary>
    public async Task<List<TodoItem>> GetAllAsync(string teacherId)
    {
        const string sql = @"
            SELECT * FROM TodoItem
            WHERE TeacherID = @TeacherID
            ORDER BY Status ASC, Priority DESC, DueDate ASC, SortOrder, No";

        using var cmd = CreateCommand(sql);
        cmd.Parameters.AddWithValue("@TeacherID", teacherId);

        return await ReadListAsync(cmd);
    }

    /// <summary>
    /// 마감일 초과 항목
    /// </summary>
    public async Task<List<TodoItem>> GetOverdueAsync(string teacherId)
    {
        const string sql = @"
            SELECT * FROM TodoItem
            WHERE TeacherID = @TeacherID 
              AND Status <> @CompletedStatus
              AND DueDate IS NOT NULL 
              AND DueDate < @Today
            ORDER BY DueDate ASC";

        using var cmd = CreateCommand(sql);
        cmd.Parameters.AddWithValue("@TeacherID", teacherId);
        cmd.Parameters.AddWithValue("@CompletedStatus", (int)TodoStatus.완료);
        cmd.Parameters.AddWithValue("@Today", DateTime.Today.ToString("yyyy-MM-dd"));

        return await ReadListAsync(cmd);
    }

    #endregion

    #region Update

    public async Task UpdateAsync(TodoItem item)
    {
        const string sql = @"
            UPDATE TodoItem SET
                Title = @Title, Description = @Description,
                Priority = @Priority, Status = @Status,
                DueDate = @DueDate, CompletedAt = @CompletedAt,
                Tag = @Tag, SortOrder = @SortOrder, UpdatedAt = @UpdatedAt
            WHERE No = @No";

        item.UpdatedAt = DateTime.Now;
        using var cmd = CreateCommand(sql);
        AddParameters(cmd, item);
        cmd.Parameters.AddWithValue("@No", item.No);

        await cmd.ExecuteNonQueryAsync();
    }

    #endregion

    #region Delete

    public async Task DeleteAsync(int no)
    {
        const string sql = "DELETE FROM TodoItem WHERE No = @No";
        using var cmd = CreateCommand(sql);
        cmd.Parameters.AddWithValue("@No", no);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>완료된 항목 일괄 삭제</summary>
    public async Task<int> DeleteCompletedAsync(string teacherId)
    {
        const string sql = @"
            DELETE FROM TodoItem 
            WHERE TeacherID = @TeacherID AND Status = @CompletedStatus";

        using var cmd = CreateCommand(sql);
        cmd.Parameters.AddWithValue("@TeacherID", teacherId);
        cmd.Parameters.AddWithValue("@CompletedStatus", (int)TodoStatus.완료);

        return await cmd.ExecuteNonQueryAsync();
    }

    #endregion

    #region Helpers

    private void AddParameters(SqliteCommand cmd, TodoItem item)
    {
        cmd.Parameters.AddWithValue("@TeacherID", item.TeacherID);
        cmd.Parameters.AddWithValue("@Title", item.Title);
        cmd.Parameters.AddWithValue("@Description", item.Description);
        cmd.Parameters.AddWithValue("@Priority", (int)item.Priority);
        cmd.Parameters.AddWithValue("@Status", (int)item.Status);
        cmd.Parameters.AddWithValue("@DueDate",
            item.DueDate.HasValue ? item.DueDate.Value.ToString("yyyy-MM-dd") : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@CompletedAt",
            item.CompletedAt.HasValue ? item.CompletedAt.Value.ToString("yyyy-MM-dd HH:mm:ss") : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@Tag", item.Tag);
        cmd.Parameters.AddWithValue("@SortOrder", item.SortOrder);
        cmd.Parameters.AddWithValue("@CreatedAt", item.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("@UpdatedAt", item.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
    }

    private async Task<List<TodoItem>> ReadListAsync(SqliteCommand cmd)
    {
        var list = new List<TodoItem>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            list.Add(MapFromReader(reader));
        return list;
    }

    private static TodoItem MapFromReader(SqliteDataReader reader)
    {
        return new TodoItem
        {
            No = reader.GetInt32(reader.GetOrdinal("No")),
            TeacherID = reader.GetString(reader.GetOrdinal("TeacherID")),
            Title = reader.GetString(reader.GetOrdinal("Title")),
            Description = reader.GetString(reader.GetOrdinal("Description")),
            Priority = (TodoPriority)reader.GetInt32(reader.GetOrdinal("Priority")),
            Status = (TodoStatus)reader.GetInt32(reader.GetOrdinal("Status")),
            DueDate = ParseNullableDateSafe(reader, "DueDate"),
            CompletedAt = ParseNullableDateTimeSafe(reader, "CompletedAt"),
            Tag = reader.GetString(reader.GetOrdinal("Tag")),
            SortOrder = reader.GetInt32(reader.GetOrdinal("SortOrder")),
            CreatedAt = ParseDateTimeSafe(reader, "CreatedAt"),
            UpdatedAt = ParseDateTimeSafe(reader, "UpdatedAt")
        };
    }

    private static DateTime? ParseNullableDateSafe(SqliteDataReader reader, string column)
    {
        try
        {
            var ordinal = reader.GetOrdinal(column);
            if (reader.IsDBNull(ordinal)) return null;
            return DateTime.TryParse(reader.GetString(ordinal), out var dt) ? dt : null;
        }
        catch { return null; }
    }

    private static DateTime? ParseNullableDateTimeSafe(SqliteDataReader reader, string column)
    {
        try
        {
            var ordinal = reader.GetOrdinal(column);
            if (reader.IsDBNull(ordinal)) return null;
            return DateTime.TryParse(reader.GetString(ordinal), out var dt) ? dt : null;
        }
        catch { return null; }
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

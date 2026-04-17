using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Models;

namespace NewSchool.Repositories;

/// <summary>
/// Undo/Redo 기록 저장소
/// </summary>
public class UndoHistoryRepository : IDisposable
{
    private readonly string _connectionString;

    public UndoHistoryRepository(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
        EnsureTableExists();
    }

    #region Table Management

    private void EnsureTableExists()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        var sql = @"
            CREATE TABLE IF NOT EXISTS UndoHistory (
                No INTEGER PRIMARY KEY AUTOINCREMENT,
                CourseId INTEGER NOT NULL,
                Room TEXT NOT NULL,
                ActionType INTEGER NOT NULL,
                Description TEXT,
                ActionData TEXT,
                CreatedAt TEXT NOT NULL,
                IsUndone INTEGER NOT NULL DEFAULT 0,
                UndoneAt TEXT
            );

            CREATE INDEX IF NOT EXISTS idx_undohistory_course_room 
            ON UndoHistory(CourseId, Room);

            CREATE INDEX IF NOT EXISTS idx_undohistory_created 
            ON UndoHistory(CreatedAt DESC);
        ";

        using var cmd = new SqliteCommand(sql, conn);
        cmd.ExecuteNonQuery();
    }

    #endregion

    #region CRUD Operations

    /// <summary>
    /// Undo 기록 생성
    /// </summary>
    public async Task<int> CreateAsync(UndoAction action)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var sql = @"
            INSERT INTO UndoHistory (CourseId, Room, ActionType, Description, ActionData, CreatedAt, IsUndone)
            VALUES (@CourseId, @Room, @ActionType, @Description, @ActionData, @CreatedAt, 0);
            SELECT last_insert_rowid();
        ";

        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@CourseId", action.CourseId);
        cmd.Parameters.AddWithValue("@Room", action.Room);
        cmd.Parameters.AddWithValue("@ActionType", (int)action.ActionType);
        cmd.Parameters.AddWithValue("@Description", action.Description ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@ActionData", action.ActionData ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@CreatedAt", action.CreatedAt.ToString("o"));

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    /// <summary>
    /// ID로 조회
    /// </summary>
    public async Task<UndoAction?> GetByIdAsync(int no)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var sql = "SELECT * FROM UndoHistory WHERE No = @No";

        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@No", no);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return MapToUndoAction(reader);
        }

        return null;
    }

    /// <summary>
    /// 마지막 Undo 가능 작업 조회
    /// </summary>
    public async Task<UndoAction?> GetLastUndoableActionAsync(int courseId, string room)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var sql = @"
            SELECT * FROM UndoHistory 
            WHERE CourseId = @CourseId AND Room = @Room AND IsUndone = 0
            ORDER BY CreatedAt DESC
            LIMIT 1
        ";

        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@CourseId", courseId);
        cmd.Parameters.AddWithValue("@Room", room);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return MapToUndoAction(reader);
        }

        return null;
    }

    /// <summary>
    /// 마지막 Redo 가능 작업 조회
    /// </summary>
    public async Task<UndoAction?> GetLastRedoableActionAsync(int courseId, string room)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var sql = @"
            SELECT * FROM UndoHistory 
            WHERE CourseId = @CourseId AND Room = @Room AND IsUndone = 1
            ORDER BY UndoneAt DESC
            LIMIT 1
        ";

        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@CourseId", courseId);
        cmd.Parameters.AddWithValue("@Room", room);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return MapToUndoAction(reader);
        }

        return null;
    }

    /// <summary>
    /// Undo 상태로 변경
    /// </summary>
    public async Task MarkAsUndoneAsync(int no)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var sql = @"
            UPDATE UndoHistory 
            SET IsUndone = 1, UndoneAt = @UndoneAt
            WHERE No = @No
        ";

        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@No", no);
        cmd.Parameters.AddWithValue("@UndoneAt", DateTime.Now.ToString("o"));

        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Redo 상태로 변경 (IsUndone = 0)
    /// </summary>
    public async Task MarkAsRedoneAsync(int no)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var sql = @"
            UPDATE UndoHistory 
            SET IsUndone = 0, UndoneAt = NULL
            WHERE No = @No
        ";

        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@No", no);

        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 오래된 기록 삭제 (기본 30일 이상)
    /// </summary>
    public async Task ClearOldHistoryAsync(int daysToKeep = 30)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var cutoffDate = DateTime.Now.AddDays(-daysToKeep);

        var sql = @"
            DELETE FROM UndoHistory 
            WHERE CreatedAt < @CutoffDate
        ";

        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@CutoffDate", cutoffDate.ToString("o"));

        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 특정 과목+학급의 모든 기록 삭제
    /// </summary>
    public async Task ClearByCourseAndRoomAsync(int courseId, string room)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var sql = @"
            DELETE FROM UndoHistory 
            WHERE CourseId = @CourseId AND Room = @Room
        ";

        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@CourseId", courseId);
        cmd.Parameters.AddWithValue("@Room", room);

        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Redo 스택 정리 (새 작업 시 호출)
    /// </summary>
    public async Task ClearRedoStackAsync(int courseId, string room)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var sql = @"
            DELETE FROM UndoHistory 
            WHERE CourseId = @CourseId AND Room = @Room AND IsUndone = 1
        ";

        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@CourseId", courseId);
        cmd.Parameters.AddWithValue("@Room", room);

        await cmd.ExecuteNonQueryAsync();
    }

    #endregion

    #region Query Operations

    /// <summary>
    /// Undo 가능 여부
    /// </summary>
    public async Task<bool> CanUndoAsync(int courseId, string room)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var sql = @"
            SELECT EXISTS(SELECT 1 FROM UndoHistory
            WHERE CourseId = @CourseId AND Room = @Room AND IsUndone = 0)
        ";

        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@CourseId", courseId);
        cmd.Parameters.AddWithValue("@Room", room);

        var result = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        return result == 1;
    }

    /// <summary>
    /// Redo 가능 여부
    /// </summary>
    public async Task<bool> CanRedoAsync(int courseId, string room)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var sql = @"
            SELECT EXISTS(SELECT 1 FROM UndoHistory
            WHERE CourseId = @CourseId AND Room = @Room AND IsUndone = 1)
        ";

        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@CourseId", courseId);
        cmd.Parameters.AddWithValue("@Room", room);

        var result = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        return result == 1;
    }

    /// <summary>
    /// 최근 기록 목록 조회
    /// </summary>
    public async Task<List<UndoAction>> GetRecentActionsAsync(int courseId, string room, int limit = 20)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var sql = @"
            SELECT * FROM UndoHistory 
            WHERE CourseId = @CourseId AND Room = @Room
            ORDER BY CreatedAt DESC
            LIMIT @Limit
        ";

        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@CourseId", courseId);
        cmd.Parameters.AddWithValue("@Room", room);
        cmd.Parameters.AddWithValue("@Limit", limit);

        var actions = new List<UndoAction>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            actions.Add(MapToUndoAction(reader));
        }

        return actions;
    }

    /// <summary>
    /// Undoable 작업 목록 조회
    /// </summary>
    public async Task<List<UndoAction>> GetUndoableActionsAsync(int courseId, string room, int limit = 10)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var sql = @"
            SELECT * FROM UndoHistory 
            WHERE CourseId = @CourseId AND Room = @Room AND IsUndone = 0
            ORDER BY CreatedAt DESC
            LIMIT @Limit
        ";

        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@CourseId", courseId);
        cmd.Parameters.AddWithValue("@Room", room);
        cmd.Parameters.AddWithValue("@Limit", limit);

        var actions = new List<UndoAction>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            actions.Add(MapToUndoAction(reader));
        }

        return actions;
    }

    #endregion

    #region Mapping

    private UndoAction MapToUndoAction(SqliteDataReader reader)
    {
        return new UndoAction
        {
            No = reader.GetInt32(reader.GetOrdinal("No")),
            CourseId = reader.GetInt32(reader.GetOrdinal("CourseId")),
            Room = reader.GetString(reader.GetOrdinal("Room")),
            ActionType = (UndoActionType)reader.GetInt32(reader.GetOrdinal("ActionType")),
            Description = reader.IsDBNull(reader.GetOrdinal("Description"))
                ? string.Empty
                : reader.GetString(reader.GetOrdinal("Description")),
            ActionData = reader.IsDBNull(reader.GetOrdinal("ActionData"))
                ? string.Empty
                : reader.GetString(reader.GetOrdinal("ActionData")),
            CreatedAt = DateTime.TryParse(reader.GetString(reader.GetOrdinal("CreatedAt")), out var created)
                ? created
                : DateTime.Now,
            IsUndone = reader.GetInt32(reader.GetOrdinal("IsUndone")) == 1,
            UndoneAt = reader.IsDBNull(reader.GetOrdinal("UndoneAt"))
                ? null
                : DateTime.TryParse(reader.GetString(reader.GetOrdinal("UndoneAt")), out var undone)
                    ? undone
                    : null
        };
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        // No resources to dispose - connections are created and disposed per operation
    }

    #endregion
}

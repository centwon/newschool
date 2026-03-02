using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Models;

namespace NewSchool.Repositories;

/// <summary>
/// 진도 기록 저장소
/// </summary>
public class LessonProgressRepository : IDisposable
{
    private readonly string _connectionString;

    public LessonProgressRepository(string dbPath)
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
            CREATE TABLE IF NOT EXISTS LessonProgress (
                No INTEGER PRIMARY KEY AUTOINCREMENT,
                CourseSectionId INTEGER NOT NULL,
                Room TEXT NOT NULL,
                IsCompleted INTEGER NOT NULL DEFAULT 0,
                CompletedDate TEXT,
                ProgressType INTEGER NOT NULL DEFAULT 0,
                ScheduleId INTEGER,
                Memo TEXT,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT,
                FOREIGN KEY (CourseSectionId) REFERENCES CourseSection(No),
                FOREIGN KEY (ScheduleId) REFERENCES Schedule(No)
            );

            CREATE INDEX IF NOT EXISTS idx_lessonprogress_section 
            ON LessonProgress(CourseSectionId);

            CREATE INDEX IF NOT EXISTS idx_lessonprogress_room 
            ON LessonProgress(Room);

            CREATE INDEX IF NOT EXISTS idx_lessonprogress_section_room 
            ON LessonProgress(CourseSectionId, Room);

            CREATE UNIQUE INDEX IF NOT EXISTS idx_lessonprogress_unique 
            ON LessonProgress(CourseSectionId, Room);
        ";

        using var cmd = new SqliteCommand(sql, conn);
        cmd.ExecuteNonQuery();
    }

    #endregion

    #region CRUD Operations

    /// <summary>
    /// 진도 기록 생성
    /// </summary>
    public async Task<int> CreateAsync(LessonProgress progress)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var sql = @"
            INSERT INTO LessonProgress (CourseSectionId, Room, IsCompleted, CompletedDate, ProgressType, ScheduleId, Memo, CreatedAt)
            VALUES (@CourseSectionId, @Room, @IsCompleted, @CompletedDate, @ProgressType, @ScheduleId, @Memo, @CreatedAt);
            SELECT last_insert_rowid();
        ";

        using var cmd = new SqliteCommand(sql, conn);
        AddParameters(cmd, progress);

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    /// <summary>
    /// 진도 기록 업데이트
    /// </summary>
    public async Task UpdateAsync(LessonProgress progress)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var sql = @"
            UPDATE LessonProgress SET
                IsCompleted = @IsCompleted,
                CompletedDate = @CompletedDate,
                ProgressType = @ProgressType,
                ScheduleId = @ScheduleId,
                Memo = @Memo,
                UpdatedAt = @UpdatedAt
            WHERE No = @No
        ";

        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@No", progress.No);
        cmd.Parameters.AddWithValue("@IsCompleted", progress.IsCompleted ? 1 : 0);
        cmd.Parameters.AddWithValue("@CompletedDate", progress.CompletedDate?.ToString("o") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@ProgressType", (int)progress.ProgressType);
        cmd.Parameters.AddWithValue("@ScheduleId", progress.ScheduleId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@Memo", progress.Memo ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@UpdatedAt", DateTime.Now.ToString("o"));

        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 진도 기록 삭제
    /// </summary>
    public async Task DeleteAsync(int no)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var sql = "DELETE FROM LessonProgress WHERE No = @No";

        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@No", no);

        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// ID로 조회
    /// </summary>
    public async Task<LessonProgress?> GetByIdAsync(int no)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var sql = "SELECT * FROM LessonProgress WHERE No = @No";

        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@No", no);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return MapToProgress(reader);
        }

        return null;
    }

    /// <summary>
    /// 단원+학급으로 조회
    /// </summary>
    public async Task<LessonProgress?> GetBySectionAndRoomAsync(int sectionId, string room)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var sql = "SELECT * FROM LessonProgress WHERE CourseSectionId = @SectionId AND Room = @Room";

        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@SectionId", sectionId);
        cmd.Parameters.AddWithValue("@Room", room);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return MapToProgress(reader);
        }

        return null;
    }

    /// <summary>
    /// 진도 기록 생성 또는 업데이트 (Upsert)
    /// </summary>
    public async Task<LessonProgress> GetOrCreateAsync(int sectionId, string room)
    {
        var existing = await GetBySectionAndRoomAsync(sectionId, room);
        if (existing != null)
            return existing;

        var progress = new LessonProgress
        {
            CourseSectionId = sectionId,
            Room = room,
            IsCompleted = false,
            ProgressType = ProgressType.Normal,
            CreatedAt = DateTime.Now
        };

        progress.No = await CreateAsync(progress);
        return progress;
    }

    #endregion

    #region Query Methods

    /// <summary>
    /// 과목의 전체 진도 조회 (매트릭스용)
    /// </summary>
    public async Task<List<LessonProgress>> GetByCourseAsync(int courseId)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var sql = @"
            SELECT lp.* FROM LessonProgress lp
            INNER JOIN CourseSection cs ON lp.CourseSectionId = cs.No
            WHERE cs.Course = @CourseId
            ORDER BY cs.SortOrder, lp.Room
        ";

        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@CourseId", courseId);

        var list = new List<LessonProgress>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(MapToProgress(reader));
        }

        return list;
    }

    /// <summary>
    /// 학급별 진도 조회
    /// </summary>
    public async Task<List<LessonProgress>> GetByRoomAsync(int courseId, string room)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var sql = @"
            SELECT lp.* FROM LessonProgress lp
            INNER JOIN CourseSection cs ON lp.CourseSectionId = cs.No
            WHERE cs.Course = @CourseId AND lp.Room = @Room
            ORDER BY cs.SortOrder
        ";

        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@CourseId", courseId);
        cmd.Parameters.AddWithValue("@Room", room);

        var list = new List<LessonProgress>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(MapToProgress(reader));
        }

        return list;
    }

    /// <summary>
    /// 단원별 진도 조회 (모든 학급)
    /// </summary>
    public async Task<List<LessonProgress>> GetBySectionAsync(int sectionId)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var sql = "SELECT * FROM LessonProgress WHERE CourseSectionId = @SectionId ORDER BY Room";

        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@SectionId", sectionId);

        var list = new List<LessonProgress>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(MapToProgress(reader));
        }

        return list;
    }

    /// <summary>
    /// 학급별 완료 수 집계 (격차 분석용)
    /// </summary>
    public async Task<List<ProgressGap>> GetProgressGapsAsync(int courseId, List<string> rooms)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        // 전체 단원 수 조회
        var countSql = "SELECT COUNT(*) FROM CourseSection WHERE Course = @CourseId";
        using var countCmd = new SqliteCommand(countSql, conn);
        countCmd.Parameters.AddWithValue("@CourseId", courseId);
        int totalCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

        // 학급별 완료 수 조회
        var sql = @"
            SELECT lp.Room, COUNT(*) as CompletedCount
            FROM LessonProgress lp
            INNER JOIN CourseSection cs ON lp.CourseSectionId = cs.No
            WHERE cs.Course = @CourseId AND lp.IsCompleted = 1
            GROUP BY lp.Room
        ";

        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@CourseId", courseId);

        var completedByRoom = new Dictionary<string, int>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            completedByRoom[reader.GetString(0)] = reader.GetInt32(1);
        }

        // 결과 생성
        var gaps = new List<ProgressGap>();
        int maxCompleted = 0;
        double totalCompleted = 0;

        foreach (var room in rooms)
        {
            int completed = completedByRoom.GetValueOrDefault(room, 0);
            if (completed > maxCompleted) maxCompleted = completed;
            totalCompleted += completed;
        }

        double avgCompleted = rooms.Count > 0 ? totalCompleted / rooms.Count : 0;

        foreach (var room in rooms)
        {
            int completed = completedByRoom.GetValueOrDefault(room, 0);
            gaps.Add(new ProgressGap
            {
                Room = room,
                CompletedCount = completed,
                TotalCount = totalCount,
                GapFromMax = maxCompleted - completed,
                GapFromAverage = Math.Round(avgCompleted - completed, 1)
            });
        }

        return gaps;
    }

    /// <summary>
    /// 완료 상태 변경
    /// </summary>
    public async Task MarkAsCompletedAsync(int sectionId, string room, DateTime? date = null, int? scheduleId = null)
    {
        var progress = await GetOrCreateAsync(sectionId, room);
        progress.MarkAsCompleted(date, scheduleId);
        await UpdateAsync(progress);
    }

    /// <summary>
    /// 완료 취소
    /// </summary>
    public async Task MarkAsIncompleteAsync(int sectionId, string room)
    {
        var progress = await GetBySectionAndRoomAsync(sectionId, room);
        if (progress != null)
        {
            progress.MarkAsIncomplete();
            await UpdateAsync(progress);
        }
    }

    /// <summary>
    /// 보강으로 표시
    /// </summary>
    public async Task MarkAsMakeupAsync(int sectionId, string room, DateTime date, string? memo = null)
    {
        var progress = await GetOrCreateAsync(sectionId, room);
        progress.MarkAsMakeup(date, memo);
        await UpdateAsync(progress);
    }

    /// <summary>
    /// 건너뛰기 처리
    /// </summary>
    public async Task MarkAsSkippedAsync(int sectionId, string room, string? reason = null)
    {
        var progress = await GetOrCreateAsync(sectionId, room);
        progress.MarkAsSkipped(reason);
        await UpdateAsync(progress);
    }

    /// <summary>
    /// 결강 처리
    /// </summary>
    public async Task MarkAsCancelledAsync(int sectionId, string room, string? reason = null)
    {
        var progress = await GetOrCreateAsync(sectionId, room);
        progress.MarkAsCancelled(reason);
        await UpdateAsync(progress);
    }

    /// <summary>
    /// 학급별 진도 초기화
    /// </summary>
    public async Task InitializeProgressForRoomAsync(int courseId, string room, List<int> sectionIds)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var transaction = conn.BeginTransaction();

        try
        {
            foreach (var sectionId in sectionIds)
            {
                // 이미 존재하는지 확인
                var checkSql = "SELECT COUNT(*) FROM LessonProgress WHERE CourseSectionId = @SectionId AND Room = @Room";
                using var checkCmd = new SqliteCommand(checkSql, conn, transaction);
                checkCmd.Parameters.AddWithValue("@SectionId", sectionId);
                checkCmd.Parameters.AddWithValue("@Room", room);

                var exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) > 0;
                if (exists) continue;

                // 새로 생성
                var insertSql = @"
                    INSERT INTO LessonProgress (CourseSectionId, Room, IsCompleted, ProgressType, CreatedAt)
                    VALUES (@SectionId, @Room, 0, 0, @CreatedAt)
                ";
                using var insertCmd = new SqliteCommand(insertSql, conn, transaction);
                insertCmd.Parameters.AddWithValue("@SectionId", sectionId);
                insertCmd.Parameters.AddWithValue("@Room", room);
                insertCmd.Parameters.AddWithValue("@CreatedAt", DateTime.Now.ToString("o"));

                await insertCmd.ExecuteNonQueryAsync();
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    /// <summary>
    /// 진도 통계 조회
    /// </summary>
    public async Task<ProgressStats> GetStatsAsync(int courseId, string room)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var sql = @"
            SELECT 
                COUNT(*) as Total,
                SUM(CASE WHEN lp.IsCompleted = 1 THEN 1 ELSE 0 END) as Completed,
                SUM(CASE WHEN lp.ProgressType = 1 THEN 1 ELSE 0 END) as Makeup,
                SUM(CASE WHEN lp.ProgressType = 2 THEN 1 ELSE 0 END) as Merged,
                SUM(CASE WHEN lp.ProgressType = 3 THEN 1 ELSE 0 END) as Skipped,
                SUM(CASE WHEN lp.ProgressType = 4 THEN 1 ELSE 0 END) as Cancelled
            FROM LessonProgress lp
            INNER JOIN CourseSection cs ON lp.CourseSectionId = cs.No
            WHERE cs.Course = @CourseId AND lp.Room = @Room
        ";

        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@CourseId", courseId);
        cmd.Parameters.AddWithValue("@Room", room);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new ProgressStats
            {
                TotalCount = reader.IsDBNull(0) ? 0 : reader.GetInt32(0),
                CompletedCount = reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                MakeupCount = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                MergedCount = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                SkippedCount = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                CancelledCount = reader.IsDBNull(5) ? 0 : reader.GetInt32(5)
            };
        }

        return new ProgressStats();
    }

    #endregion

    #region Helper Methods

    private void AddParameters(SqliteCommand cmd, LessonProgress progress)
    {
        cmd.Parameters.AddWithValue("@CourseSectionId", progress.CourseSectionId);
        cmd.Parameters.AddWithValue("@Room", progress.Room);
        cmd.Parameters.AddWithValue("@IsCompleted", progress.IsCompleted ? 1 : 0);
        cmd.Parameters.AddWithValue("@CompletedDate", progress.CompletedDate?.ToString("o") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@ProgressType", (int)progress.ProgressType);
        cmd.Parameters.AddWithValue("@ScheduleId", progress.ScheduleId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@Memo", progress.Memo ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@CreatedAt", progress.CreatedAt.ToString("o"));
    }

    private LessonProgress MapToProgress(SqliteDataReader reader)
    {
        return new LessonProgress
        {
            No = reader.GetInt32(reader.GetOrdinal("No")),
            CourseSectionId = reader.GetInt32(reader.GetOrdinal("CourseSectionId")),
            Room = reader.GetString(reader.GetOrdinal("Room")),
            IsCompleted = reader.GetInt32(reader.GetOrdinal("IsCompleted")) == 1,
            CompletedDate = reader.IsDBNull(reader.GetOrdinal("CompletedDate"))
                ? null
                : DateTime.TryParse(reader.GetString(reader.GetOrdinal("CompletedDate")), out var cd) ? cd : null,
            ProgressType = (ProgressType)reader.GetInt32(reader.GetOrdinal("ProgressType")),
            ScheduleId = reader.IsDBNull(reader.GetOrdinal("ScheduleId"))
                ? null
                : reader.GetInt32(reader.GetOrdinal("ScheduleId")),
            Memo = reader.IsDBNull(reader.GetOrdinal("Memo"))
                ? null
                : reader.GetString(reader.GetOrdinal("Memo")),
            CreatedAt = DateTime.TryParse(reader.GetString(reader.GetOrdinal("CreatedAt")), out var ca)
                ? ca
                : DateTime.Now,
            UpdatedAt = reader.IsDBNull(reader.GetOrdinal("UpdatedAt"))
                ? null
                : DateTime.TryParse(reader.GetString(reader.GetOrdinal("UpdatedAt")), out var ua) ? ua : null
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

/// <summary>
/// 진도 통계
/// </summary>
public class ProgressStats
{
    public int TotalCount { get; set; }
    public int CompletedCount { get; set; }
    public int MakeupCount { get; set; }
    public int MergedCount { get; set; }
    public int SkippedCount { get; set; }
    public int CancelledCount { get; set; }

    public int RemainingCount => TotalCount - CompletedCount - SkippedCount - CancelledCount;
    public double CompletionRate => TotalCount > 0 ? Math.Round((double)CompletedCount / TotalCount * 100, 1) : 0;
}

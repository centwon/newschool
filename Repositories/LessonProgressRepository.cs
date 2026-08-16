using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Models;

namespace NewSchool.Repositories;

/// <summary>
/// 진도 기록 저장소.
///
/// ⚠ 되살리면서 <c>ScheduleId</c> 컬럼을 뺐다 — 부모였던 <c>Schedule</c> 테이블이
/// 1.0 정리에서 사라졌고, 없는 테이블을 가리키는 FK 는 <c>foreign_keys=ON</c> 에서
/// INSERT 를 준비 단계부터 막는다.
/// </summary>
public class LessonProgressRepository : BaseRepository
{
    public LessonProgressRepository(string dbPath) : base(dbPath)
    {
        EnsureTableExists();
    }

    #region Table Management

    /// <summary>
    /// LessonProgress 스키마 정본 — <c>DatabaseInitializer</c> 가 함께 실행한다.
    ///
    /// <c>ON DELETE CASCADE</c> 가 빠지면 NO ACTION 이라 부모를 지우려는 쪽이 막힌다.
    /// 진도 기록이 하나라도 있으면 수업 삭제(Course→CourseSection CASCADE)와 단원 삭제가
    /// 'FOREIGN KEY constraint failed' 로 영구 실패했던 적이 있다.
    /// </summary>
    internal const string SchemaSql = @"
            CREATE TABLE IF NOT EXISTS LessonProgress (
                No INTEGER PRIMARY KEY AUTOINCREMENT,
                CourseSectionId INTEGER NOT NULL,
                Room TEXT NOT NULL,
                IsCompleted INTEGER NOT NULL DEFAULT 0,
                CompletedDate TEXT,
                ProgressType INTEGER NOT NULL DEFAULT 0,
                Memo TEXT,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT,
                FOREIGN KEY (CourseSectionId) REFERENCES CourseSection(No) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_lessonprogress_section
            ON LessonProgress(CourseSectionId);

            CREATE INDEX IF NOT EXISTS idx_lessonprogress_room
            ON LessonProgress(Room);

            CREATE UNIQUE INDEX IF NOT EXISTS idx_lessonprogress_unique
            ON LessonProgress(CourseSectionId, Room);
        ";

    private void EnsureTableExists()
    {
        try
        {
            using var cmd = CreateCommand(SchemaSql);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            LogError("LessonProgress 테이블 생성 실패", ex);
        }
    }

    #endregion

    #region CRUD

    /// <summary>
    /// 진도 기록 생성
    /// </summary>
    public async Task<int> CreateAsync(LessonProgress progress)
    {
        const string query = @"
            INSERT INTO LessonProgress
                (CourseSectionId, Room, IsCompleted, CompletedDate, ProgressType, Memo, CreatedAt)
            VALUES
                (@CourseSectionId, @Room, @IsCompleted, @CompletedDate, @ProgressType, @Memo, @CreatedAt);
            SELECT last_insert_rowid();";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.Add("@CourseSectionId", SqliteType.Integer).Value = progress.CourseSectionId;
            cmd.Parameters.AddWithValue("@Room", progress.Room);
            cmd.Parameters.Add("@IsCompleted", SqliteType.Integer).Value = progress.IsCompleted ? 1 : 0;
            cmd.Parameters.AddWithValue("@CompletedDate", (object?)progress.CompletedDate?.ToString("o") ?? DBNull.Value);
            cmd.Parameters.Add("@ProgressType", SqliteType.Integer).Value = (int)progress.ProgressType;
            cmd.Parameters.AddWithValue("@Memo", (object?)progress.Memo ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CreatedAt", progress.CreatedAt.ToString("o"));

            progress.No = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            return progress.No;
        }
        catch (Exception ex)
        {
            LogError($"진도 기록 생성 실패: 단원={progress.CourseSectionId}, {progress.Room}", ex);
            throw;
        }
    }

    /// <summary>
    /// 진도 기록 갱신
    /// </summary>
    /// <returns>실제로 갱신된 행이 있으면 true</returns>
    public async Task<bool> UpdateAsync(LessonProgress progress)
    {
        const string query = @"
            UPDATE LessonProgress SET
                IsCompleted = @IsCompleted,
                CompletedDate = @CompletedDate,
                ProgressType = @ProgressType,
                Memo = @Memo,
                UpdatedAt = @UpdatedAt
            WHERE No = @No";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.Add("@No", SqliteType.Integer).Value = progress.No;
            cmd.Parameters.Add("@IsCompleted", SqliteType.Integer).Value = progress.IsCompleted ? 1 : 0;
            cmd.Parameters.AddWithValue("@CompletedDate", (object?)progress.CompletedDate?.ToString("o") ?? DBNull.Value);
            cmd.Parameters.Add("@ProgressType", SqliteType.Integer).Value = (int)progress.ProgressType;
            cmd.Parameters.AddWithValue("@Memo", (object?)progress.Memo ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@UpdatedAt", DateTime.Now.ToString("o"));

            return await cmd.ExecuteNonQueryAsync() > 0;
        }
        catch (Exception ex)
        {
            LogError($"진도 기록 갱신 실패: No={progress.No}", ex);
            throw;
        }
    }

    /// <summary>
    /// 단원+학급으로 조회
    /// </summary>
    public async Task<LessonProgress?> GetBySectionAndRoomAsync(int sectionId, string room)
    {
        const string query = "SELECT * FROM LessonProgress WHERE CourseSectionId = @SectionId AND Room = @Room";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.Add("@SectionId", SqliteType.Integer).Value = sectionId;
            cmd.Parameters.AddWithValue("@Room", room);

            using var reader = await cmd.ExecuteReaderAsync();
            return await reader.ReadAsync() ? Map(reader) : null;
        }
        catch (Exception ex)
        {
            LogError($"진도 기록 조회 실패: 단원={sectionId}, {room}", ex);
            throw;
        }
    }

    /// <summary>
    /// 없으면 만들어서 돌려준다
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

        await CreateAsync(progress);
        return progress;
    }

    #endregion

    #region Query

    /// <summary>
    /// 수업의 전체 진도 조회 (매트릭스용)
    /// </summary>
    public async Task<List<LessonProgress>> GetByCourseAsync(int courseNo)
    {
        const string query = @"
            SELECT lp.* FROM LessonProgress lp
            INNER JOIN CourseSection cs ON lp.CourseSectionId = cs.No
            WHERE cs.Course = @CourseNo
            ORDER BY cs.SortOrder, lp.Room";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.Add("@CourseNo", SqliteType.Integer).Value = courseNo;

            var list = new List<LessonProgress>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                list.Add(Map(reader));

            return list;
        }
        catch (Exception ex)
        {
            LogError($"수업 진도 조회 실패: Course={courseNo}", ex);
            throw;
        }
    }

    /// <summary>
    /// 학급별 완료 수 집계 (격차 분석용)
    /// </summary>
    public async Task<List<ProgressGap>> GetProgressGapsAsync(int courseNo, List<string> rooms)
    {
        var gaps = new List<ProgressGap>();
        if (rooms.Count == 0) return gaps;

        try
        {
            int totalCount;
            using (var countCmd = CreateCommand("SELECT COUNT(*) FROM CourseSection WHERE Course = @CourseNo"))
            {
                countCmd.Parameters.Add("@CourseNo", SqliteType.Integer).Value = courseNo;
                totalCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
            }

            var completedByRoom = new Dictionary<string, int>();
            const string query = @"
                SELECT lp.Room, COUNT(*) AS CompletedCount
                FROM LessonProgress lp
                INNER JOIN CourseSection cs ON lp.CourseSectionId = cs.No
                WHERE cs.Course = @CourseNo AND lp.IsCompleted = 1
                GROUP BY lp.Room";

            using (var cmd = CreateCommand(query))
            {
                cmd.Parameters.Add("@CourseNo", SqliteType.Integer).Value = courseNo;
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    completedByRoom[reader.GetString(0)] = reader.GetInt32(1);
            }

            int maxCompleted = 0;
            double sum = 0;
            foreach (var room in rooms)
            {
                int completed = completedByRoom.GetValueOrDefault(room, 0);
                if (completed > maxCompleted) maxCompleted = completed;
                sum += completed;
            }

            double average = sum / rooms.Count;

            foreach (var room in rooms)
            {
                int completed = completedByRoom.GetValueOrDefault(room, 0);
                gaps.Add(new ProgressGap
                {
                    Room = room,
                    CompletedCount = completed,
                    TotalCount = totalCount,
                    GapFromMax = maxCompleted - completed,
                    GapFromAverage = Math.Round(average - completed, 1)
                });
            }

            return gaps;
        }
        catch (Exception ex)
        {
            LogError($"진도 격차 조회 실패: Course={courseNo}", ex);
            throw;
        }
    }

    #endregion

    #region 상태 변경

    /// <summary>완료 처리</summary>
    public async Task<bool> MarkAsCompletedAsync(int sectionId, string room, DateTime? date = null)
    {
        var progress = await GetOrCreateAsync(sectionId, room);
        progress.MarkAsCompleted(date);
        return await UpdateAsync(progress);
    }

    /// <summary>완료 취소 (진도 기록이 아예 없으면 false)</summary>
    public async Task<bool> MarkAsIncompleteAsync(int sectionId, string room)
    {
        var progress = await GetBySectionAndRoomAsync(sectionId, room);
        if (progress == null)
            return false;

        progress.MarkAsIncomplete();
        return await UpdateAsync(progress);
    }

    /// <summary>보강 처리</summary>
    public async Task<bool> MarkAsMakeupAsync(int sectionId, string room, DateTime date, string? memo = null)
    {
        var progress = await GetOrCreateAsync(sectionId, room);
        progress.MarkAsMakeup(date, memo);
        return await UpdateAsync(progress);
    }

    /// <summary>병합 처리</summary>
    public async Task<bool> MarkAsMergedAsync(int sectionId, string room, DateTime? date = null, string? memo = null)
    {
        var progress = await GetOrCreateAsync(sectionId, room);
        progress.MarkAsMerged(date, memo);
        return await UpdateAsync(progress);
    }

    /// <summary>건너뛰기 처리</summary>
    public async Task<bool> MarkAsSkippedAsync(int sectionId, string room, string? reason = null)
    {
        var progress = await GetOrCreateAsync(sectionId, room);
        progress.MarkAsSkipped(reason);
        return await UpdateAsync(progress);
    }

    /// <summary>결강 처리</summary>
    public async Task<bool> MarkAsCancelledAsync(int sectionId, string room, string? reason = null)
    {
        var progress = await GetOrCreateAsync(sectionId, room);
        progress.MarkAsCancelled(reason);
        return await UpdateAsync(progress);
    }

    #endregion

    #region Helper

    private static LessonProgress Map(SqliteDataReader reader)
    {
        return new LessonProgress
        {
            No = reader.GetInt32(reader.GetOrdinal("No")),
            CourseSectionId = reader.GetInt32(reader.GetOrdinal("CourseSectionId")),
            Room = reader.GetString(reader.GetOrdinal("Room")),
            IsCompleted = reader.GetInt32(reader.GetOrdinal("IsCompleted")) == 1,
            CompletedDate = ReadDate(reader, "CompletedDate"),
            ProgressType = (ProgressType)reader.GetInt32(reader.GetOrdinal("ProgressType")),
            Memo = reader.IsDBNull(reader.GetOrdinal("Memo"))
                ? null
                : reader.GetString(reader.GetOrdinal("Memo")),
            CreatedAt = ReadDate(reader, "CreatedAt") ?? DateTime.Now,
            UpdatedAt = ReadDate(reader, "UpdatedAt")
        };
    }

    private static DateTime? ReadDate(SqliteDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        if (reader.IsDBNull(ordinal)) return null;

        return DateTime.TryParse(reader.GetString(ordinal), out var value) ? value : null;
    }

    #endregion
}

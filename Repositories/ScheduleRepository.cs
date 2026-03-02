using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Models;

namespace NewSchool.Repositories;

/// <summary>
/// Schedule Repository
/// 실제 배치된 수업 슬롯 관리
/// </summary>
public class ScheduleRepository : BaseRepository
{
    public ScheduleRepository(string dbPath) : base(dbPath)
    {
        EnsureTableExists();
    }

    #region Table Management

    private void EnsureTableExists()
    {
        const string sql = @"
            CREATE TABLE IF NOT EXISTS Schedule (
                No INTEGER PRIMARY KEY AUTOINCREMENT,
                CourseId INTEGER NOT NULL,
                Room TEXT NOT NULL,
                Date TEXT NOT NULL,
                Period INTEGER NOT NULL,
                IsCompleted INTEGER DEFAULT 0,
                CompletedAt TEXT,
                IsCancelled INTEGER DEFAULT 0,
                CancelReason TEXT DEFAULT '',
                IsPinned INTEGER DEFAULT 0,
                Memo TEXT DEFAULT '',
                CreatedAt TEXT DEFAULT (datetime('now', 'localtime')),
                UpdatedAt TEXT DEFAULT (datetime('now', 'localtime')),
                FOREIGN KEY (CourseId) REFERENCES Course(No) ON DELETE CASCADE
            );
            
            CREATE INDEX IF NOT EXISTS idx_schedule_course ON Schedule(CourseId);
            CREATE INDEX IF NOT EXISTS idx_schedule_date ON Schedule(Date);
            CREATE INDEX IF NOT EXISTS idx_schedule_room ON Schedule(CourseId, Room);
            CREATE UNIQUE INDEX IF NOT EXISTS idx_schedule_slot ON Schedule(CourseId, Room, Date, Period);
        ";

        try
        {
            using var cmd = CreateCommand(sql);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            LogError("Schedule 테이블 생성 실패", ex);
        }
    }

    #endregion

    #region Create

    /// <summary>
    /// 스케줄 생성
    /// </summary>
    public async Task<int> CreateAsync(Schedule schedule)
    {
        const string query = @"
            INSERT INTO Schedule (
                CourseId, Room, Date, Period, IsCompleted, CompletedAt,
                IsCancelled, CancelReason, IsPinned, Memo, CreatedAt, UpdatedAt
            ) VALUES (
                @CourseId, @Room, @Date, @Period, @IsCompleted, @CompletedAt,
                @IsCancelled, @CancelReason, @IsPinned, @Memo, @CreatedAt, @UpdatedAt
            );
            SELECT last_insert_rowid();";

        try
        {
            using var cmd = CreateCommand(query);
            AddParameters(cmd, schedule);

            var result = await cmd.ExecuteScalarAsync();
            schedule.No = Convert.ToInt32(result);

            LogInfo($"스케줄 생성 완료: No={schedule.No}, {schedule.FullSlotDisplay}");
            return schedule.No;
        }
        catch (Exception ex)
        {
            LogError($"스케줄 생성 실패: {schedule.Room} {schedule.DateDisplay}", ex);
            throw;
        }
    }

    /// <summary>
    /// 스케줄 일괄 생성
    /// </summary>
    public async Task<int> BulkCreateAsync(List<Schedule> schedules)
    {
        int count = 0;
        foreach (var schedule in schedules)
        {
            await CreateAsync(schedule);
            count++;
        }
        LogInfo($"스케줄 일괄 생성 완료: {count}개");
        return count;
    }

    /// <summary>
    /// 스케줄 조회 또는 생성 (GetOrCreate 패턴)
    /// </summary>
    public async Task<Schedule> GetOrCreateAsync(int courseId, string room, DateTime date, int period)
    {
        var existing = await GetBySlotAsync(courseId, room, date, period);
        if (existing != null)
            return existing;

        var newSchedule = new Schedule
        {
            CourseId = courseId,
            Room = room,
            Date = date,
            Period = period
        };
        await CreateAsync(newSchedule);
        return newSchedule;
    }

    #endregion

    #region Read

    /// <summary>
    /// No로 스케줄 조회
    /// </summary>
    public async Task<Schedule?> GetByIdAsync(int no)
    {
        const string query = "SELECT * FROM Schedule WHERE No = @No";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@No", no);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return MapSchedule(reader);
            }
            return null;
        }
        catch (Exception ex)
        {
            LogError($"스케줄 조회 실패: No={no}", ex);
            throw;
        }
    }

    /// <summary>
    /// 특정 슬롯으로 조회 (중복 확인용)
    /// </summary>
    public async Task<Schedule?> GetBySlotAsync(int courseId, string room, DateTime date, int period)
    {
        const string query = @"
            SELECT * FROM Schedule 
            WHERE CourseId = @CourseId AND Room = @Room AND Date = @Date AND Period = @Period";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@CourseId", courseId);
            cmd.Parameters.AddWithValue("@Room", room);
            cmd.Parameters.AddWithValue("@Date", date.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@Period", period);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return MapSchedule(reader);
            }
            return null;
        }
        catch (Exception ex)
        {
            LogError($"슬롯 조회 실패", ex);
            throw;
        }
    }

    /// <summary>
    /// 과목별 스케줄 조회
    /// </summary>
    public async Task<List<Schedule>> GetByCourseAsync(int courseId)
    {
        const string query = @"
            SELECT * FROM Schedule 
            WHERE CourseId = @CourseId AND IsCancelled = 0
            ORDER BY Date, Period";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@CourseId", courseId);

            return await ExecuteQueryAsync(cmd);
        }
        catch (Exception ex)
        {
            LogError($"과목별 스케줄 조회 실패: CourseId={courseId}", ex);
            throw;
        }
    }

    /// <summary>
    /// 과목+학급별 스케줄 조회
    /// </summary>
    public async Task<List<Schedule>> GetByCourseAndRoomAsync(int courseId, string room)
    {
        const string query = @"
            SELECT * FROM Schedule 
            WHERE CourseId = @CourseId AND Room = @Room AND IsCancelled = 0
            ORDER BY Date, Period";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@CourseId", courseId);
            cmd.Parameters.AddWithValue("@Room", room);

            return await ExecuteQueryAsync(cmd);
        }
        catch (Exception ex)
        {
            LogError($"과목+학급별 스케줄 조회 실패: CourseId={courseId}, Room={room}", ex);
            throw;
        }
    }

    /// <summary>
    /// 날짜 범위로 스케줄 조회
    /// </summary>
    public async Task<List<Schedule>> GetByDateRangeAsync(int courseId, DateTime startDate, DateTime endDate)
    {
        const string query = @"
            SELECT * FROM Schedule 
            WHERE CourseId = @CourseId 
              AND Date >= @StartDate AND Date <= @EndDate
              AND IsCancelled = 0
            ORDER BY Date, Period";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@CourseId", courseId);
            cmd.Parameters.AddWithValue("@StartDate", startDate.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@EndDate", endDate.ToString("yyyy-MM-dd"));

            return await ExecuteQueryAsync(cmd);
        }
        catch (Exception ex)
        {
            LogError($"날짜 범위 스케줄 조회 실패", ex);
            throw;
        }
    }

    /// <summary>
    /// 특정 날짜 이후의 스케줄 조회 (밀리기/당기기용)
    /// </summary>
    public async Task<List<Schedule>> GetSchedulesFromDateAsync(int courseId, string room, DateTime fromDate)
    {
        const string query = @"
            SELECT * FROM Schedule 
            WHERE CourseId = @CourseId AND Room = @Room 
              AND Date >= @FromDate AND IsCancelled = 0
            ORDER BY Date, Period";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@CourseId", courseId);
            cmd.Parameters.AddWithValue("@Room", room);
            cmd.Parameters.AddWithValue("@FromDate", fromDate.ToString("yyyy-MM-dd"));

            return await ExecuteQueryAsync(cmd);
        }
        catch (Exception ex)
        {
            LogError($"날짜 이후 스케줄 조회 실패", ex);
            throw;
        }
    }

    /// <summary>
    /// 고정되지 않은 스케줄만 조회 (밀리기/당기기용)
    /// </summary>
    public async Task<List<Schedule>> GetUnpinnedSchedulesFromDateAsync(int courseId, string room, DateTime fromDate)
    {
        const string query = @"
            SELECT * FROM Schedule 
            WHERE CourseId = @CourseId AND Room = @Room 
              AND Date >= @FromDate AND IsCancelled = 0 AND IsPinned = 0
            ORDER BY Date, Period";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@CourseId", courseId);
            cmd.Parameters.AddWithValue("@Room", room);
            cmd.Parameters.AddWithValue("@FromDate", fromDate.ToString("yyyy-MM-dd"));

            return await ExecuteQueryAsync(cmd);
        }
        catch (Exception ex)
        {
            LogError($"미고정 스케줄 조회 실패", ex);
            throw;
        }
    }

    /// <summary>
    /// 미완료 스케줄 조회
    /// </summary>
    public async Task<List<Schedule>> GetIncompleteAsync(int courseId, string? room = null)
    {
        var query = @"
            SELECT * FROM Schedule 
            WHERE CourseId = @CourseId AND IsCompleted = 0 AND IsCancelled = 0";
        
        if (!string.IsNullOrEmpty(room))
            query += " AND Room = @Room";
        
        query += " ORDER BY Date, Period";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@CourseId", courseId);
            if (!string.IsNullOrEmpty(room))
                cmd.Parameters.AddWithValue("@Room", room);

            return await ExecuteQueryAsync(cmd);
        }
        catch (Exception ex)
        {
            LogError($"미완료 스케줄 조회 실패", ex);
            throw;
        }
    }

    /// <summary>
    /// 과목의 학급 목록 조회
    /// </summary>
    public async Task<List<string>> GetRoomsAsync(int courseId)
    {
        const string query = @"
            SELECT DISTINCT Room FROM Schedule 
            WHERE CourseId = @CourseId 
            ORDER BY Room";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@CourseId", courseId);

            var rooms = new List<string>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rooms.Add(reader.GetString(0));
            }
            return rooms;
        }
        catch (Exception ex)
        {
            LogError($"학급 목록 조회 실패", ex);
            throw;
        }
    }

    #endregion

    #region Update

    /// <summary>
    /// 스케줄 수정
    /// </summary>
    public async Task<bool> UpdateAsync(Schedule schedule)
    {
        const string query = @"
            UPDATE Schedule SET
                CourseId = @CourseId,
                Room = @Room,
                Date = @Date,
                Period = @Period,
                IsCompleted = @IsCompleted,
                CompletedAt = @CompletedAt,
                IsCancelled = @IsCancelled,
                CancelReason = @CancelReason,
                IsPinned = @IsPinned,
                Memo = @Memo,
                UpdatedAt = @UpdatedAt
            WHERE No = @No";

        try
        {
            schedule.UpdatedAt = DateTime.Now;
            
            using var cmd = CreateCommand(query);
            AddParameters(cmd, schedule);

            int affected = await cmd.ExecuteNonQueryAsync();
            bool success = affected > 0;

            if (success)
                LogInfo($"스케줄 수정 완료: No={schedule.No}");

            return success;
        }
        catch (Exception ex)
        {
            LogError($"스케줄 수정 실패: No={schedule.No}", ex);
            throw;
        }
    }

    /// <summary>
    /// 완료 처리
    /// </summary>
    public async Task<bool> MarkAsCompletedAsync(int no)
    {
        const string query = @"
            UPDATE Schedule SET 
                IsCompleted = 1, 
                CompletedAt = @CompletedAt,
                UpdatedAt = @UpdatedAt 
            WHERE No = @No";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@No", no);
            cmd.Parameters.AddWithValue("@CompletedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@UpdatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            return await cmd.ExecuteNonQueryAsync() > 0;
        }
        catch (Exception ex)
        {
            LogError($"완료 처리 실패: No={no}", ex);
            throw;
        }
    }

    /// <summary>
    /// 결강 처리
    /// </summary>
    public async Task<bool> MarkAsCancelledAsync(int no, string reason = "")
    {
        const string query = @"
            UPDATE Schedule SET 
                IsCancelled = 1, 
                CancelReason = @CancelReason,
                UpdatedAt = @UpdatedAt 
            WHERE No = @No";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@No", no);
            cmd.Parameters.AddWithValue("@CancelReason", reason);
            cmd.Parameters.AddWithValue("@UpdatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            return await cmd.ExecuteNonQueryAsync() > 0;
        }
        catch (Exception ex)
        {
            LogError($"결강 처리 실패: No={no}", ex);
            throw;
        }
    }

    /// <summary>
    /// 고정 설정
    /// </summary>
    public async Task<bool> SetPinnedAsync(int no, bool isPinned)
    {
        const string query = @"
            UPDATE Schedule SET 
                IsPinned = @IsPinned,
                UpdatedAt = @UpdatedAt 
            WHERE No = @No";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@No", no);
            cmd.Parameters.AddWithValue("@IsPinned", isPinned ? 1 : 0);
            cmd.Parameters.AddWithValue("@UpdatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            return await cmd.ExecuteNonQueryAsync() > 0;
        }
        catch (Exception ex)
        {
            LogError($"고정 설정 실패: No={no}", ex);
            throw;
        }
    }

    /// <summary>
    /// 스케줄 일괄 이동 (밀리기/당기기용) - Transaction 사용
    /// </summary>
    public async Task<int> ShiftSchedulesAsync(List<(int ScheduleId, DateTime NewDate, int NewPeriod)> shifts)
    {
        if (shifts.Count == 0) return 0;

        const string query = @"
            UPDATE Schedule SET 
                Date = @Date, 
                Period = @Period,
                UpdatedAt = @UpdatedAt 
            WHERE No = @No";

        try
        {
            int count = 0;
            var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            // Transaction 처리
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                foreach (var (scheduleId, newDate, newPeriod) in shifts)
                {
                    using var cmd = connection.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = query;
                    cmd.Parameters.AddWithValue("@No", scheduleId);
                    cmd.Parameters.AddWithValue("@Date", newDate.ToString("yyyy-MM-dd"));
                    cmd.Parameters.AddWithValue("@Period", newPeriod);
                    cmd.Parameters.AddWithValue("@UpdatedAt", now);

                    count += await cmd.ExecuteNonQueryAsync();
                }

                transaction.Commit();
                LogInfo($"스케줄 일괄 이동 완료: {count}개");
                return count;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex)
        {
            LogError($"스케줄 일괄 이동 실패", ex);
            throw;
        }
    }

    #endregion

    #region Delete

    /// <summary>
    /// 스케줄 삭제
    /// </summary>
    public async Task<bool> DeleteAsync(int no)
    {
        const string query = "DELETE FROM Schedule WHERE No = @No";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@No", no);

            int affected = await cmd.ExecuteNonQueryAsync();
            bool success = affected > 0;

            if (success)
                LogInfo($"스케줄 삭제 완료: No={no}");

            return success;
        }
        catch (Exception ex)
        {
            LogError($"스케줄 삭제 실패: No={no}", ex);
            throw;
        }
    }

    /// <summary>
    /// 과목의 모든 스케줄 삭제
    /// </summary>
    public async Task<int> DeleteByCourseAsync(int courseId)
    {
        const string query = "DELETE FROM Schedule WHERE CourseId = @CourseId";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@CourseId", courseId);

            int affected = await cmd.ExecuteNonQueryAsync();
            LogInfo($"과목별 스케줄 삭제: CourseId={courseId}, 삭제={affected}개");

            return affected;
        }
        catch (Exception ex)
        {
            LogError($"과목별 스케줄 삭제 실패: CourseId={courseId}", ex);
            throw;
        }
    }

    /// <summary>
    /// 과목+학급의 모든 스케줄 삭제
    /// </summary>
    public async Task<int> DeleteByCourseAndRoomAsync(int courseId, string room)
    {
        const string query = "DELETE FROM Schedule WHERE CourseId = @CourseId AND Room = @Room";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@CourseId", courseId);
            cmd.Parameters.AddWithValue("@Room", room);

            int affected = await cmd.ExecuteNonQueryAsync();
            LogInfo($"과목+학급별 스케줄 삭제: CourseId={courseId}, Room={room}, 삭제={affected}개");

            return affected;
        }
        catch (Exception ex)
        {
            LogError($"과목+학급별 스케줄 삭제 실패", ex);
            throw;
        }
    }

    #endregion

    #region Statistics

    /// <summary>
    /// 스케줄 통계 조회
    /// </summary>
    public async Task<ScheduleStats> GetStatsAsync(int courseId, string? room = null)
    {
        var query = @"
            SELECT 
                COUNT(*) as Total,
                SUM(CASE WHEN IsCompleted = 1 THEN 1 ELSE 0 END) as Completed,
                SUM(CASE WHEN IsCancelled = 1 THEN 1 ELSE 0 END) as Cancelled,
                SUM(CASE WHEN IsPinned = 1 THEN 1 ELSE 0 END) as Pinned
            FROM Schedule 
            WHERE CourseId = @CourseId";
        
        if (!string.IsNullOrEmpty(room))
            query += " AND Room = @Room";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@CourseId", courseId);
            if (!string.IsNullOrEmpty(room))
                cmd.Parameters.AddWithValue("@Room", room);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new ScheduleStats
                {
                    Total = reader.GetInt32(0),
                    Completed = reader.GetInt32(1),
                    Cancelled = reader.GetInt32(2),
                    Pinned = reader.GetInt32(3)
                };
            }
            return new ScheduleStats();
        }
        catch (Exception ex)
        {
            LogError($"스케줄 통계 조회 실패", ex);
            throw;
        }
    }

    #endregion

    #region Helper Methods

    private void AddParameters(SqliteCommand cmd, Schedule schedule)
    {
        cmd.Parameters.AddWithValue("@No", schedule.No);
        cmd.Parameters.AddWithValue("@CourseId", schedule.CourseId);
        cmd.Parameters.AddWithValue("@Room", schedule.Room);
        cmd.Parameters.AddWithValue("@Date", schedule.Date.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@Period", schedule.Period);
        cmd.Parameters.AddWithValue("@IsCompleted", schedule.IsCompleted ? 1 : 0);
        cmd.Parameters.AddWithValue("@CompletedAt", schedule.CompletedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@IsCancelled", schedule.IsCancelled ? 1 : 0);
        cmd.Parameters.AddWithValue("@CancelReason", schedule.CancelReason ?? string.Empty);
        cmd.Parameters.AddWithValue("@IsPinned", schedule.IsPinned ? 1 : 0);
        cmd.Parameters.AddWithValue("@Memo", schedule.Memo ?? string.Empty);
        cmd.Parameters.AddWithValue("@CreatedAt", schedule.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("@UpdatedAt", schedule.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
    }

    private async Task<List<Schedule>> ExecuteQueryAsync(SqliteCommand cmd)
    {
        var schedules = new List<Schedule>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            schedules.Add(MapSchedule(reader));
        }
        return schedules;
    }

    private Schedule MapSchedule(SqliteDataReader reader)
    {
        var schedule = new Schedule
        {
            No = reader.GetInt32(reader.GetOrdinal("No")),
            CourseId = reader.GetInt32(reader.GetOrdinal("CourseId")),
            Room = reader.GetString(reader.GetOrdinal("Room")),
            Period = reader.GetInt32(reader.GetOrdinal("Period")),
            IsCompleted = reader.GetInt32(reader.GetOrdinal("IsCompleted")) == 1,
            IsCancelled = reader.GetInt32(reader.GetOrdinal("IsCancelled")) == 1,
            IsPinned = reader.GetInt32(reader.GetOrdinal("IsPinned")) == 1,
            CancelReason = GetStringOrDefault(reader, "CancelReason"),
            Memo = GetStringOrDefault(reader, "Memo")
        };

        // Date 파싱
        var dateStr = reader.GetString(reader.GetOrdinal("Date"));
        if (DateTime.TryParse(dateStr, out var date))
            schedule.Date = date;

        // CompletedAt 파싱
        var completedAtStr = GetStringOrDefault(reader, "CompletedAt");
        if (!string.IsNullOrEmpty(completedAtStr) && DateTime.TryParse(completedAtStr, out var completedAt))
            schedule.CompletedAt = completedAt;

        // CreatedAt, UpdatedAt 파싱
        var createdAtStr = GetStringOrDefault(reader, "CreatedAt");
        if (!string.IsNullOrEmpty(createdAtStr) && DateTime.TryParse(createdAtStr, out var createdAt))
            schedule.CreatedAt = createdAt;

        var updatedAtStr = GetStringOrDefault(reader, "UpdatedAt");
        if (!string.IsNullOrEmpty(updatedAtStr) && DateTime.TryParse(updatedAtStr, out var updatedAt))
            schedule.UpdatedAt = updatedAt;

        return schedule;
    }

    private string GetStringOrDefault(SqliteDataReader reader, string columnName, string defaultValue = "")
    {
        try
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? defaultValue : reader.GetString(ordinal);
        }
        catch
        {
            return defaultValue;
        }
    }

    #endregion
}

/// <summary>
/// 스케줄 통계
/// </summary>
public class ScheduleStats
{
    public int Total { get; set; }
    public int Completed { get; set; }
    public int Cancelled { get; set; }
    public int Pinned { get; set; }
    
    public int Active => Total - Cancelled;
    public int Remaining => Active - Completed;
    public double CompletionRate => Active > 0 ? (double)Completed / Active * 100 : 0;
}

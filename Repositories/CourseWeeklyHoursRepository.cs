using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Models;

namespace NewSchool.Repositories;

/// <summary>
/// 주차별 시수 조정 저장소.
///
/// 자동 계산값은 저장하지 않는다 — 행이 있다는 것 자체가 "교사가 이 칸을 손으로 고쳤다"는 뜻이다.
/// 조정을 지우면 행을 지우고, 다시 자동값을 따른다.
/// </summary>
public class CourseWeeklyHoursRepository : BaseRepository
{
    public CourseWeeklyHoursRepository(string dbPath) : base(dbPath)
    {
        EnsureTableExists();
    }

    #region Table Management

    /// <summary>
    /// CourseWeeklyHours 스키마 정본 — <c>DatabaseInitializer</c> 가 함께 실행한다.
    ///
    /// 1.0 을 첫 배포로 잡았으므로 그 이전 모양을 위한 마이그레이션은 두지 않는다.
    /// 배포 전 개발 DB 에 옛 모양이 남아 있으면 이 테이블만 지우고 다시 만들면 된다
    /// (담긴 것은 손으로 고친 주차 시수뿐이다).
    /// </summary>
    internal const string SchemaSql = @"
            CREATE TABLE IF NOT EXISTS CourseWeeklyHours (
                No INTEGER PRIMARY KEY AUTOINCREMENT,
                CourseNo INTEGER NOT NULL,
                Room TEXT NOT NULL DEFAULT '',
                Week INTEGER NOT NULL,
                WeekStartDate TEXT NOT NULL,
                PlannedHours INTEGER NOT NULL,
                FOREIGN KEY (CourseNo) REFERENCES Course(No) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_courseweeklyhours_course
            ON CourseWeeklyHours(CourseNo);

            -- 같은 (수업, 학급, 주차) 가 두 줄이면 어느 값이 진짜인지 알 수 없다. UPSERT 도 여기 기댄다.
            CREATE UNIQUE INDEX IF NOT EXISTS idx_courseweeklyhours_unique
            ON CourseWeeklyHours(CourseNo, Room, Week);
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
            LogError("CourseWeeklyHours 테이블 생성 실패", ex);
        }
    }

    #endregion

    /// <summary>
    /// 수업의 조정 기록 조회 — (학급, 주차) → 조정
    /// </summary>
    public async Task<Dictionary<(string Room, int Week), CourseWeeklyHours>> GetByCourseAsync(int courseNo)
    {
        const string query = "SELECT * FROM CourseWeeklyHours WHERE CourseNo = @CourseNo ORDER BY Room, Week";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.Add("@CourseNo", SqliteType.Integer).Value = courseNo;

            var map = new Dictionary<(string, int), CourseWeeklyHours>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var row = Map(reader);
                map[(row.Room, row.Week)] = row;
            }

            return map;
        }
        catch (Exception ex)
        {
            LogError($"주차별 시수 조회 실패: Course={courseNo}", ex);
            throw;
        }
    }

    /// <summary>
    /// 조정 저장 (있으면 갱신, 없으면 추가)
    /// </summary>
    public async Task<bool> UpsertAsync(CourseWeeklyHours hours)
    {
        const string query = @"
            INSERT INTO CourseWeeklyHours (CourseNo, Room, Week, WeekStartDate, PlannedHours)
            VALUES (@CourseNo, @Room, @Week, @WeekStartDate, @PlannedHours)
            ON CONFLICT(CourseNo, Room, Week) DO UPDATE SET
                WeekStartDate = excluded.WeekStartDate,
                PlannedHours  = excluded.PlannedHours;";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.Add("@CourseNo", SqliteType.Integer).Value = hours.CourseNo;
            cmd.Parameters.AddWithValue("@Room", hours.Room ?? string.Empty);
            cmd.Parameters.Add("@Week", SqliteType.Integer).Value = hours.Week;
            cmd.Parameters.AddWithValue("@WeekStartDate", hours.WeekStart.ToString("yyyy-MM-dd"));
            cmd.Parameters.Add("@PlannedHours", SqliteType.Integer).Value = hours.PlannedHours;

            return await cmd.ExecuteNonQueryAsync() > 0;
        }
        catch (Exception ex)
        {
            LogError($"주차별 시수 저장 실패: Course={hours.CourseNo}, {hours.Room} {hours.Week}주차", ex);
            throw;
        }
    }

    /// <summary>
    /// 조정 삭제 (자동값으로 되돌리기)
    /// </summary>
    public async Task<bool> DeleteAsync(int courseNo, string room, int week)
    {
        const string query = "DELETE FROM CourseWeeklyHours WHERE CourseNo = @CourseNo AND Room = @Room AND Week = @Week";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.Add("@CourseNo", SqliteType.Integer).Value = courseNo;
            cmd.Parameters.AddWithValue("@Room", room ?? string.Empty);
            cmd.Parameters.Add("@Week", SqliteType.Integer).Value = week;

            return await cmd.ExecuteNonQueryAsync() > 0;
        }
        catch (Exception ex)
        {
            LogError($"주차별 시수 삭제 실패: Course={courseNo}, {room} {week}주차", ex);
            throw;
        }
    }

    /// <summary>
    /// 수업의 조정 전체 삭제
    /// </summary>
    public async Task<int> DeleteByCourseAsync(int courseNo)
    {
        const string query = "DELETE FROM CourseWeeklyHours WHERE CourseNo = @CourseNo";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.Add("@CourseNo", SqliteType.Integer).Value = courseNo;

            return await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            LogError($"주차별 시수 전체 삭제 실패: Course={courseNo}", ex);
            throw;
        }
    }

    private static CourseWeeklyHours Map(SqliteDataReader reader)
    {
        return new CourseWeeklyHours
        {
            No = reader.GetInt32(reader.GetOrdinal("No")),
            CourseNo = reader.GetInt32(reader.GetOrdinal("CourseNo")),
            Room = reader.GetString(reader.GetOrdinal("Room")),
            Week = reader.GetInt32(reader.GetOrdinal("Week")),
            WeekStart = DateTimeHelper.FromDateString(reader.GetString(reader.GetOrdinal("WeekStartDate"))),
            PlannedHours = reader.GetInt32(reader.GetOrdinal("PlannedHours"))
        };
    }
}

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

    /// <summary>공유 연결 생성자. 테이블은 이미 있다고 보고 DDL 을 실행하지 않는다 —
    /// 트랜잭션 안에서 DDL 을 돌리지 않기 위해서다
    /// (<see cref="Services.CourseRoomReset"/>).</summary>
    public CourseWeeklyHoursRepository(SqliteConnection connection) : base(connection) { }

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

            -- 한 주를 가리키는 것은 <b>주 시작일</b>이지 주차 번호가 아니다.
            --
            -- ⚠ 예전에는 (수업, 학급, 주차번호) 가 키였다. 그런데 주차 번호는 학기 구간에서
            --    다시 세어지는 값이라, 2학기를 관례값(9/1 시작)으로 보다가 나중에 학사일정을
            --    내려받으면 시작이 여름방학 다음 첫 수업일(예: 8/17)로 당겨지면서 번호가
            --    통째로 밀린다. 그러면 9월 셋째 주에 손으로 고친 시수가 8월 마지막 주 칸에
            --    나타났다 — 조용히 다른 주에 붙는 것이라 알아채기 어렵다.
            --    주 시작일(월요일)은 그렇게 움직이지 않는다. UPSERT 도 여기 기댄다.
            CREATE UNIQUE INDEX IF NOT EXISTS idx_courseweeklyhours_by_weekstart
            ON CourseWeeklyHours(CourseNo, Room, WeekStartDate);
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
    /// 수업의 조정 기록 조회 — (학급, <b>주 시작일</b>) → 조정.
    ///
    /// <para>주차 번호가 아니라 날짜로 찍는다 — 번호는 학기 구간이 바뀌면 다시 세어진다
    /// (<see cref="SchemaSql"/> 의 인덱스 주석).</para>
    /// </summary>
    public async Task<Dictionary<(string Room, DateTime WeekStart), CourseWeeklyHours>> GetByCourseAsync(int courseNo)
    {
        const string query = "SELECT * FROM CourseWeeklyHours WHERE CourseNo = @CourseNo ORDER BY Room, WeekStartDate";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.Add("@CourseNo", SqliteType.Integer).Value = courseNo;

            var map = new Dictionary<(string, DateTime), CourseWeeklyHours>();
            using var reader = await cmd.ExecuteReaderAsync();
            var cache = new ReaderColumnCache();
            cache.Initialize(reader);   // 컬럼 인덱스를 행마다 다시 찾지 않도록 한 번만
            while (await reader.ReadAsync())
            {
                var row = Map(reader, cache);
                map[(row.Room, row.WeekStart.Date)] = row;
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
            ON CONFLICT(CourseNo, Room, WeekStartDate) DO UPDATE SET
                Week         = excluded.Week,
                PlannedHours = excluded.PlannedHours;";

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
    /// 조정 삭제 (자동값으로 되돌리기). 주차 번호가 아니라 <b>주 시작일</b>로 지운다.
    /// </summary>
    public async Task<bool> DeleteAsync(int courseNo, string room, DateTime weekStart)
    {
        const string query = "DELETE FROM CourseWeeklyHours WHERE CourseNo = @CourseNo AND Room = @Room AND WeekStartDate = @WeekStartDate";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.Add("@CourseNo", SqliteType.Integer).Value = courseNo;
            cmd.Parameters.AddWithValue("@Room", room ?? string.Empty);
            cmd.Parameters.AddWithValue("@WeekStartDate", weekStart.ToString("yyyy-MM-dd"));

            return await cmd.ExecuteNonQueryAsync() > 0;
        }
        catch (Exception ex)
        {
            LogError($"주차별 시수 삭제 실패: Course={courseNo}, {room} {weekStart:yyyy-MM-dd} 주", ex);
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

    private static CourseWeeklyHours Map(SqliteDataReader reader, ReaderColumnCache cache)
    {
        return new CourseWeeklyHours
        {
            No = reader.GetInt32(cache.GetOrdinal("No")),
            CourseNo = reader.GetInt32(cache.GetOrdinal("CourseNo")),
            Room = reader.GetString(cache.GetOrdinal("Room")),
            Week = reader.GetInt32(cache.GetOrdinal("Week")),
            WeekStart = DateTimeHelper.FromDateString(reader.GetString(cache.GetOrdinal("WeekStartDate"))),
            PlannedHours = reader.GetInt32(cache.GetOrdinal("PlannedHours"))
        };
    }
}

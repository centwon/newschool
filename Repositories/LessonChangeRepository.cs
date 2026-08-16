using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Models;

namespace NewSchool.Repositories;

/// <summary>
/// 시간표 변경 저장소 — 날짜·교시 단위 예외만 담는다.
///
/// 정기 시간표(<c>Lesson</c>)는 건드리지 않으므로, 변경을 넣어도 시수 계산이나
/// 교사 시간표의 "평소" 기준은 그대로다.
/// </summary>
public class LessonChangeRepository : BaseRepository
{
    public LessonChangeRepository(string dbPath) : base(dbPath)
    {
        EnsureTableExists();
    }

    #region Table Management

    /// <summary>
    /// LessonChange 스키마 정본 — <c>DatabaseInitializer</c> 가 함께 실행한다.
    ///
    /// <c>CourseNo</c> 는 nullable 이다. 휴강을 0 같은 특수값으로 두면 <c>Course(No)</c> 에
    /// 그런 행이 없어 FK 가 걸린다 — SQLite 는 NULL 인 FK 만 통과시킨다.
    /// </summary>
    internal const string SchemaSql = @"
            CREATE TABLE IF NOT EXISTS LessonChange (
                No INTEGER PRIMARY KEY AUTOINCREMENT,
                TeacherID TEXT NOT NULL,
                Year INTEGER NOT NULL,
                Semester INTEGER NOT NULL,
                Date TEXT NOT NULL,
                Period INTEGER NOT NULL,
                CourseNo INTEGER,
                SubjectText TEXT,
                Room TEXT,
                Memo TEXT,
                FOREIGN KEY (CourseNo) REFERENCES Course(No) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_lessonchange_teacher_date
            ON LessonChange(TeacherID, Date);

            -- 한 교시에 변경은 하나다. 둘이 겹치면 어느 쪽이 오늘의 답인지 알 수 없다.
            CREATE UNIQUE INDEX IF NOT EXISTS idx_lessonchange_unique
            ON LessonChange(TeacherID, Date, Period);
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
            LogError("LessonChange 테이블 생성 실패", ex);
        }
    }

    #endregion

    #region Read

    /// <summary>
    /// 특정 날짜의 변경 — 교시 → 변경
    /// </summary>
    public async Task<Dictionary<int, LessonChange>> GetByDateAsync(string teacherId, DateTime date)
    {
        const string query = @"
            SELECT lc.*, c.Subject AS CourseSubject
            FROM LessonChange lc
            LEFT JOIN Course c ON lc.CourseNo = c.No
            WHERE lc.TeacherID = @TeacherID AND lc.Date = @Date
            ORDER BY lc.Period";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@TeacherID", teacherId);
            cmd.Parameters.AddWithValue("@Date", date.ToString("yyyy-MM-dd"));

            var map = new Dictionary<int, LessonChange>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var change = Map(reader);
                map[change.Period] = change;
            }

            return map;
        }
        catch (Exception ex)
        {
            LogError($"시간표 변경 조회 실패: {teacherId} {date:yyyy-MM-dd}", ex);
            throw;
        }
    }

    /// <summary>
    /// 기간 안의 변경 (등록 목록용). 날짜·교시 순.
    /// </summary>
    public async Task<List<LessonChange>> GetRangeAsync(string teacherId, DateTime from, DateTime to)
    {
        const string query = @"
            SELECT lc.*, c.Subject AS CourseSubject
            FROM LessonChange lc
            LEFT JOIN Course c ON lc.CourseNo = c.No
            WHERE lc.TeacherID = @TeacherID
              AND lc.Date >= @From
              AND lc.Date <= @To
            ORDER BY lc.Date, lc.Period";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@TeacherID", teacherId);
            cmd.Parameters.AddWithValue("@From", from.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@To", to.ToString("yyyy-MM-dd"));

            var list = new List<LessonChange>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                list.Add(Map(reader));

            return list;
        }
        catch (Exception ex)
        {
            LogError($"시간표 변경 목록 조회 실패: {teacherId}", ex);
            throw;
        }
    }

    #endregion

    #region Write

    /// <summary>
    /// 변경 저장 (같은 날짜·교시가 있으면 덮어쓴다)
    /// </summary>
    public async Task<bool> UpsertAsync(LessonChange change)
    {
        const string query = @"
            INSERT INTO LessonChange (TeacherID, Year, Semester, Date, Period, CourseNo, SubjectText, Room, Memo)
            VALUES (@TeacherID, @Year, @Semester, @Date, @Period, @CourseNo, @SubjectText, @Room, @Memo)
            ON CONFLICT(TeacherID, Date, Period) DO UPDATE SET
                Year        = excluded.Year,
                Semester    = excluded.Semester,
                CourseNo    = excluded.CourseNo,
                SubjectText = excluded.SubjectText,
                Room        = excluded.Room,
                Memo        = excluded.Memo;";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@TeacherID", change.TeacherID);
            cmd.Parameters.Add("@Year", SqliteType.Integer).Value = change.Year;
            cmd.Parameters.Add("@Semester", SqliteType.Integer).Value = change.Semester;
            cmd.Parameters.AddWithValue("@Date", change.Date.ToString("yyyy-MM-dd"));
            cmd.Parameters.Add("@Period", SqliteType.Integer).Value = change.Period;
            cmd.Parameters.AddWithValue("@CourseNo",
                change.HasCourse ? change.CourseNo!.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@SubjectText",
                change.IsSubstitute ? change.SubjectText : DBNull.Value);
            cmd.Parameters.AddWithValue("@Room", change.Room ?? string.Empty);
            cmd.Parameters.AddWithValue("@Memo", change.Memo ?? string.Empty);

            return await cmd.ExecuteNonQueryAsync() > 0;
        }
        catch (Exception ex)
        {
            LogError($"시간표 변경 저장 실패: {change}", ex);
            throw;
        }
    }

    /// <summary>기간 안의 변경을 모두 되돌린다 (그 주 전체 되돌리기용)</summary>
    public async Task<int> DeleteRangeAsync(string teacherId, DateTime from, DateTime to)
    {
        const string query = @"
            DELETE FROM LessonChange
            WHERE TeacherID = @TeacherID AND Date >= @From AND Date <= @To";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@TeacherID", teacherId);
            cmd.Parameters.AddWithValue("@From", from.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@To", to.ToString("yyyy-MM-dd"));

            return await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            LogError($"시간표 변경 일괄 삭제 실패: {teacherId} {from:yyyy-MM-dd}~{to:yyyy-MM-dd}", ex);
            throw;
        }
    }

    /// <summary>변경 삭제</summary>
    public async Task<bool> DeleteAsync(int no)
    {
        const string query = "DELETE FROM LessonChange WHERE No = @No";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.Add("@No", SqliteType.Integer).Value = no;

            return await cmd.ExecuteNonQueryAsync() > 0;
        }
        catch (Exception ex)
        {
            LogError($"시간표 변경 삭제 실패: No={no}", ex);
            throw;
        }
    }

    #endregion

    private static LessonChange Map(SqliteDataReader reader)
    {
        int courseOrdinal = reader.GetOrdinal("CourseNo");

        return new LessonChange
        {
            No = reader.GetInt32(reader.GetOrdinal("No")),
            TeacherID = reader.GetString(reader.GetOrdinal("TeacherID")),
            Year = reader.GetInt32(reader.GetOrdinal("Year")),
            Semester = reader.GetInt32(reader.GetOrdinal("Semester")),
            Date = DateTimeHelper.FromDateString(reader.GetString(reader.GetOrdinal("Date"))),
            Period = reader.GetInt32(reader.GetOrdinal("Period")),
            CourseNo = reader.IsDBNull(courseOrdinal) ? null : reader.GetInt32(courseOrdinal),
            SubjectText = Text(reader, "SubjectText"),
            Room = Text(reader, "Room"),
            Memo = Text(reader, "Memo"),
            CourseSubject = Text(reader, "CourseSubject")
        };
    }

    private static string Text(SqliteDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Models;

namespace NewSchool.Repositories;

/// <summary>
/// LessonLog 데이터 접근 리포지토리
/// 수업 일지 CRUD
///
/// 연결·트랜잭션·에러 로깅은 <see cref="BaseRepository"/> 가 담당한다.
/// 예전에는 이 클래스만 <c>IDisposable</c> 을 직접 구현하고 맨 연결
/// (<c>Data Source=…</c>) 을 열어서, 형제 리포지토리(StudentLog·StudentSpecial·ClassDiary)
/// 와 달리 WAL·<c>foreign_keys=ON</c>·busy_timeout 이 적용되지 않았다.
/// </summary>
public class LessonLogRepository : BaseRepository
{
    public LessonLogRepository(string dbPath) : base(dbPath)
    {
        EnsureTableExists();
    }

    #region Table Management

    /// <summary>
    /// LessonLog 스키마 정본.
    ///
    /// 예전에는 <c>DatabaseInitializer</c> 와 이 리포지토리가 서로 다른 정의를 갖고 있었고
    /// <c>CREATE TABLE IF NOT EXISTS</c> 특성상 먼저 실행한 쪽이 이겼다.
    /// 지금은 정의가 이 상수 하나뿐이다.
    /// </summary>
    internal const string SchemaSql = @"
            CREATE TABLE IF NOT EXISTS LessonLog (
                No INTEGER PRIMARY KEY AUTOINCREMENT,
                Lesson INTEGER,
                TeacherID TEXT NULL,
                Year INTEGER NOT NULL,
                Semester INTEGER NOT NULL,
                Date TEXT NOT NULL,
                Period INTEGER,
                Subject TEXT,
                Room TEXT,
                Topic TEXT,
                Content TEXT,
                Grade INTEGER DEFAULT 0,
                Class INTEGER DEFAULT 0,
                CourseSectionNo INTEGER,
                SectionName TEXT,
                Note TEXT,
                CreatedAt TEXT,
                UpdatedAt TEXT,
                FOREIGN KEY (Lesson) REFERENCES Lesson(No) ON DELETE SET NULL,
                FOREIGN KEY (TeacherID) REFERENCES Teacher(TeacherID) ON DELETE SET NULL
            );
        ";

    /// <summary>
    /// 테이블 존재 확인 및 생성
    /// </summary>
    private void EnsureTableExists()
    {
        using (var cmd = CreateCommand(SchemaSql))
            cmd.ExecuteNonQuery();

        const string indexSql = @"
            CREATE INDEX IF NOT EXISTS idx_lessonlog_teacher_year ON LessonLog(TeacherID, Year);
            CREATE INDEX IF NOT EXISTS idx_lessonlog_subject ON LessonLog(Subject);
            CREATE INDEX IF NOT EXISTS idx_lessonlog_date ON LessonLog(Date);
            CREATE INDEX IF NOT EXISTS idx_lessonlog_grade_class ON LessonLog(Grade, Class);
        ";

        using (var cmd = CreateCommand(indexSql))
            cmd.ExecuteNonQuery();
    }

    #endregion

    #region CRUD Operations

    /// <summary>
    /// 수업 기록 추가
    /// </summary>
    public async Task<int> InsertAsync(LessonLog log)
    {
        const string sql = @"
            INSERT INTO LessonLog (
                Lesson, TeacherID, Year, Semester, Date, Period, Subject,
                Grade, Class, Room, CourseSectionNo, SectionName,
                Topic, Content, Note, CreatedAt, UpdatedAt
            ) VALUES (
                @Lesson, @TeacherID, @Year, @Semester, @Date, @Period, @Subject,
                @Grade, @Class, @Room, @CourseSectionNo, @SectionName,
                @Topic, @Content, @Note, @CreatedAt, @UpdatedAt
            );
            SELECT last_insert_rowid();
        ";

        using var cmd = CreateCommand(sql);
        AddParameters(cmd, log);

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    /// <summary>
    /// 수업 기록 수정
    /// </summary>
    public async Task<int> UpdateAsync(LessonLog log)
    {
        const string sql = @"
            UPDATE LessonLog 
            SET Lesson = @Lesson,
                TeacherID = @TeacherID,
                Year = @Year,
                Semester = @Semester,
                Date = @Date,
                Period = @Period,
                Subject = @Subject,
                Grade = @Grade,
                Class = @Class,
                Room = @Room,
                CourseSectionNo = @CourseSectionNo,
                SectionName = @SectionName,
                Topic = @Topic,
                Content = @Content,
                Note = @Note,
                UpdatedAt = @UpdatedAt
            WHERE No = @No
        ";

        using var cmd = CreateCommand(sql);
        cmd.Parameters.AddWithValue("@No", log.No);
        AddParameters(cmd, log);

        return await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 수업 기록 삭제
    /// </summary>
    public async Task<int> DeleteAsync(int no)
    {
        const string sql = "DELETE FROM LessonLog WHERE No = @No";

        using var cmd = CreateCommand(sql);
        cmd.Parameters.AddWithValue("@No", no);

        return await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 단일 수업 기록 조회
    /// </summary>
    public async Task<LessonLog?> GetByIdAsync(int no)
    {
        const string sql = "SELECT * FROM LessonLog WHERE No = @No";

        using var cmd = CreateCommand(sql);
        cmd.Parameters.AddWithValue("@No", no);

        var found = await ExecuteQueryAsync(cmd);
        return found.Count > 0 ? found[0] : null;
    }

    /// <summary>
    /// 교사별 수업 기록 조회
    /// </summary>
    public async Task<List<LessonLog>> GetByTeacherAsync(string teacherId, int year, int? semester = null)
    {
        var sql = "SELECT * FROM LessonLog WHERE TeacherID = @TeacherID AND Year = @Year";
        if (semester.HasValue)
        {
            sql += " AND Semester = @Semester";
        }
        sql += " ORDER BY Date DESC, Period DESC";

        using var cmd = CreateCommand(sql);
        cmd.Parameters.AddWithValue("@TeacherID", teacherId);
        cmd.Parameters.AddWithValue("@Year", year);
        if (semester.HasValue)
        {
            cmd.Parameters.AddWithValue("@Semester", semester.Value);
        }

        return await ExecuteQueryAsync(cmd);
    }

    /// <summary>
    /// 과목별 수업 기록 조회
    /// </summary>
    public async Task<List<LessonLog>> GetBySubjectAsync(string teacherId, int year, int semester, string subject)
    {
        const string sql = @"
            SELECT * FROM LessonLog 
            WHERE TeacherID = @TeacherID AND Year = @Year AND Semester = @Semester AND Subject = @Subject
            ORDER BY Date DESC, Period DESC
        ";

        using var cmd = CreateCommand(sql);
        cmd.Parameters.AddWithValue("@TeacherID", teacherId);
        cmd.Parameters.AddWithValue("@Year", year);
        cmd.Parameters.AddWithValue("@Semester", semester);
        cmd.Parameters.AddWithValue("@Subject", subject);

        return await ExecuteQueryAsync(cmd);
    }

    /// <summary>
    /// 과목 + 학급별 수업 기록 조회
    /// </summary>
    public async Task<List<LessonLog>> GetBySubjectAndClassAsync(string teacherId, int year, int semester, 
        string? subject = null, int? grade = null, int? classNum = null, int limit = 30)
    {
        var sql = "SELECT * FROM LessonLog WHERE TeacherID = @TeacherID AND Year = @Year AND Semester = @Semester";
        
        if (!string.IsNullOrEmpty(subject))
            sql += " AND Subject = @Subject";
        if (grade.HasValue && grade > 0)
            sql += " AND Grade = @Grade";
        if (classNum.HasValue && classNum > 0)
            sql += " AND Class = @Class";
        
        sql += $" ORDER BY Date DESC, Period DESC LIMIT {limit}";

        using var cmd = CreateCommand(sql);
        cmd.Parameters.AddWithValue("@TeacherID", teacherId);
        cmd.Parameters.AddWithValue("@Year", year);
        cmd.Parameters.AddWithValue("@Semester", semester);
        
        if (!string.IsNullOrEmpty(subject))
            cmd.Parameters.AddWithValue("@Subject", subject);
        if (grade.HasValue && grade > 0)
            cmd.Parameters.AddWithValue("@Grade", grade.Value);
        if (classNum.HasValue && classNum > 0)
            cmd.Parameters.AddWithValue("@Class", classNum.Value);

        return await ExecuteQueryAsync(cmd);
    }

    /// <summary>
    /// 과목 + 강의실별 수업 기록 조회 (기존 호환)
    /// </summary>
    public async Task<List<LessonLog>> GetBySubjectAndRoomAsync(string teacherId, int year, int semester, 
        string? subject = null, string? room = null, int limit = 30)
    {
        var sql = "SELECT * FROM LessonLog WHERE TeacherID = @TeacherID AND Year = @Year AND Semester = @Semester";
        
        if (!string.IsNullOrEmpty(subject))
            sql += " AND Subject = @Subject";
        if (!string.IsNullOrEmpty(room))
            sql += " AND Room = @Room";
        
        sql += $" ORDER BY Date DESC, Period DESC LIMIT {limit}";

        using var cmd = CreateCommand(sql);
        cmd.Parameters.AddWithValue("@TeacherID", teacherId);
        cmd.Parameters.AddWithValue("@Year", year);
        cmd.Parameters.AddWithValue("@Semester", semester);
        
        if (!string.IsNullOrEmpty(subject))
            cmd.Parameters.AddWithValue("@Subject", subject);
        if (!string.IsNullOrEmpty(room))
            cmd.Parameters.AddWithValue("@Room", room);

        return await ExecuteQueryAsync(cmd);
    }

    /// <summary>
    /// 날짜별 수업 기록 조회
    /// </summary>
    public async Task<List<LessonLog>> GetByDateAsync(string teacherId, DateTime date)
    {
        const string sql = @"
            SELECT * FROM LessonLog 
            WHERE TeacherID = @TeacherID AND Date = @Date
            ORDER BY Period
        ";

        using var cmd = CreateCommand(sql);
        cmd.Parameters.AddWithValue("@TeacherID", teacherId);
        cmd.Parameters.AddWithValue("@Date", date.ToString("yyyy-MM-dd"));

        return await ExecuteQueryAsync(cmd);
    }

    /// <summary>
    /// 강의실 목록 조회 (과목별)
    /// </summary>
    public async Task<List<string>> GetRoomsAsync(string teacherId, int year, int semester, string subject)
    {
        const string sql = @"
            SELECT DISTINCT Room FROM LessonLog 
            WHERE TeacherID = @TeacherID AND Year = @Year AND Semester = @Semester AND Subject = @Subject
            ORDER BY Room
        ";

        using var cmd = CreateCommand(sql);
        cmd.Parameters.AddWithValue("@TeacherID", teacherId);
        cmd.Parameters.AddWithValue("@Year", year);
        cmd.Parameters.AddWithValue("@Semester", semester);
        cmd.Parameters.AddWithValue("@Subject", subject);

        var rooms = new List<string>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var room = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
            if (!string.IsNullOrEmpty(room))
            {
                rooms.Add(room);
            }
        }
        return rooms;
    }

    /// <summary>
    /// 수업 횟수 조회 (진도율 계산용)
    /// </summary>
    public async Task<int> GetLessonCountAsync(string teacherId, int year, int semester, string? subject = null, string? room = null)
    {
        var sql = "SELECT COUNT(*) FROM LessonLog WHERE TeacherID = @TeacherID AND Year = @Year AND Semester = @Semester";
        
        if (!string.IsNullOrEmpty(subject))
            sql += " AND Subject = @Subject";
        if (!string.IsNullOrEmpty(room))
            sql += " AND Room = @Room";

        using var cmd = CreateCommand(sql);
        cmd.Parameters.AddWithValue("@TeacherID", teacherId);
        cmd.Parameters.AddWithValue("@Year", year);
        cmd.Parameters.AddWithValue("@Semester", semester);
        
        if (!string.IsNullOrEmpty(subject))
            cmd.Parameters.AddWithValue("@Subject", subject);
        if (!string.IsNullOrEmpty(room))
            cmd.Parameters.AddWithValue("@Room", room);

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    #endregion

    #region Helper Methods

    private void AddParameters(SqliteCommand cmd, LessonLog log)
    {
        cmd.Parameters.AddWithValue("@Lesson", log.Lesson.HasValue ? log.Lesson.Value : DBNull.Value);
        // 빈 문자열은 NULL 로 바꿔 넣는다. TeacherID 는 Teacher(TeacherID) 를 참조하는데
        // 빈 문자열은 NULL 이 아니라서 foreign_keys=ON 에서는 제약 위반이 된다
        // (StudentLog·ClassDiary 리포지토리도 같은 이유로 같은 변환을 한다).
        cmd.Parameters.AddWithValue("@TeacherID",
            string.IsNullOrWhiteSpace(log.TeacherID) ? DBNull.Value : log.TeacherID);
        cmd.Parameters.AddWithValue("@Year", log.Year);
        cmd.Parameters.AddWithValue("@Semester", log.Semester);
        cmd.Parameters.AddWithValue("@Date", log.Date.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@Period", log.Period);
        cmd.Parameters.AddWithValue("@Subject", log.Subject);
        cmd.Parameters.AddWithValue("@Grade", log.Grade);
        cmd.Parameters.AddWithValue("@Class", log.Class);
        cmd.Parameters.AddWithValue("@Room", log.Room ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@CourseSectionNo", log.CourseSectionNo.HasValue ? log.CourseSectionNo.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@SectionName", log.SectionName ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@Topic", log.Topic ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@Content", log.Content ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@Note", log.Note ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@CreatedAt", log.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("@UpdatedAt", log.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
    }

    private async Task<List<LessonLog>> ExecuteQueryAsync(SqliteCommand cmd)
        => await ExecuteListAsync(cmd, MapToLessonLog).ConfigureAwait(false);

    private LessonLog MapToLessonLog(SqliteDataReader reader, ReaderColumnCache cache)
    {
        var lessonIdx = cache.GetOrdinal("Lesson");
        var teacherIdIdx = cache.GetOrdinal("TeacherID");
        var dateIdx = cache.GetOrdinal("Date");
        var periodIdx = cache.GetOrdinal("Period");
        var subjectIdx = cache.GetOrdinal("Subject");

        return new LessonLog
        {
            No = reader.GetInt32(cache.GetOrdinal("No")),
            Lesson = reader.IsDBNull(lessonIdx) ? null : reader.GetInt32(lessonIdx),
            // TeacherID·Subject 는 스키마상 NULL 이 가능하다. 특히 TeacherID 는
            // DatabaseInitializer.CleanupOrphansAsync 가 교사 삭제 시 NULL 로 만들기 때문에
            // 예전의 무조건 GetString 은 InvalidCastException 을 냈다.
            TeacherID = reader.IsDBNull(teacherIdIdx) ? string.Empty : reader.GetString(teacherIdIdx),
            Year = reader.GetInt32(cache.GetOrdinal("Year")),
            Semester = reader.GetInt32(cache.GetOrdinal("Semester")),
            Date = reader.IsDBNull(dateIdx) ? DateTime.Today : DateTime.Parse(reader.GetString(dateIdx)),
            Period = reader.IsDBNull(periodIdx) ? 0 : reader.GetInt32(periodIdx),
            Subject = reader.IsDBNull(subjectIdx) ? string.Empty : reader.GetString(subjectIdx),
            Grade = GetIntSafe(reader, cache, "Grade"),
            Class = GetIntSafe(reader, cache, "Class"),
            Room = GetStringSafe(reader, cache, "Room"),
            CourseSectionNo = GetNullableIntSafe(reader, cache, "CourseSectionNo"),
            SectionName = GetStringSafe(reader, cache, "SectionName"),
            Topic = GetStringSafe(reader, cache, "Topic"),
            Content = GetStringSafe(reader, cache, "Content"),
            Note = GetStringSafe(reader, cache, "Note"),
            CreatedAt = GetDateTimeSafe(reader, cache, "CreatedAt"),
            UpdatedAt = GetDateTimeSafe(reader, cache, "UpdatedAt")
        };
    }

    private static string GetStringSafe(SqliteDataReader reader, ReaderColumnCache cache, string column)
    {
        var ordinal = cache.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
    }

    private static int GetIntSafe(SqliteDataReader reader, ReaderColumnCache cache, string column)
    {
        var ordinal = cache.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? 0 : reader.GetInt32(ordinal);
    }

    private static int? GetNullableIntSafe(SqliteDataReader reader, ReaderColumnCache cache, string column)
    {
        var ordinal = cache.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    private static DateTime GetDateTimeSafe(SqliteDataReader reader, ReaderColumnCache cache, string column)
    {
        var ordinal = cache.GetOrdinal(column);
        if (reader.IsDBNull(ordinal)) return DateTime.Now;
        var str = reader.GetString(ordinal);
        return DateTime.TryParse(str, out var dt) ? dt : DateTime.Now;
    }

    #endregion
}

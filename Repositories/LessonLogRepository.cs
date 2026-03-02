using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Models;

namespace NewSchool.Repositories;

/// <summary>
/// LessonLog 데이터 접근 리포지토리
/// 수업 일지 CRUD
/// </summary>
public class LessonLogRepository : IDisposable
{
    private readonly SqliteConnection _connection;
    private bool _disposed;

    public LessonLogRepository(string dbPath)
    {
        _connection = new SqliteConnection($"Data Source={dbPath}");
        _connection.Open();
        EnsureTableExists();
    }

    #region Table Management

    /// <summary>
    /// 테이블 존재 확인 및 생성
    /// </summary>
    private void EnsureTableExists()
    {
        // 1. 테이블 생성 (새 DB용)
        const string createSql = @"
            CREATE TABLE IF NOT EXISTS LessonLog (
                No INTEGER PRIMARY KEY AUTOINCREMENT,
                Lesson INTEGER,
                TeacherID TEXT NOT NULL,
                Year INTEGER NOT NULL,
                Semester INTEGER NOT NULL,
                Date TEXT NOT NULL,
                Period INTEGER,
                Subject TEXT NOT NULL,
                Grade INTEGER DEFAULT 0,
                Class INTEGER DEFAULT 0,
                Room TEXT,
                CourseSectionNo INTEGER,
                SectionName TEXT,
                Topic TEXT,
                Content TEXT,
                Note TEXT,
                CreatedAt TEXT,
                UpdatedAt TEXT
            );
        ";

        using (var cmd = new SqliteCommand(createSql, _connection))
            cmd.ExecuteNonQuery();

        // 2. 기존 DB 마이그레이션: 새 컬럼 추가 (이미 있으면 무시)
        TryAddColumn("Grade", "INTEGER DEFAULT 0");
        TryAddColumn("Class", "INTEGER DEFAULT 0");
        TryAddColumn("CourseSectionNo", "INTEGER");
        TryAddColumn("SectionName", "TEXT");
        TryAddColumn("Note", "TEXT");
        TryAddColumn("CreatedAt", "TEXT");
        TryAddColumn("UpdatedAt", "TEXT");

        // 3. 인덱스 생성 (컬럼 추가 후 실행해야 기존 DB에서 오류 안 남)
        const string indexSql = @"
            CREATE INDEX IF NOT EXISTS idx_lessonlog_teacher_year ON LessonLog(TeacherID, Year);
            CREATE INDEX IF NOT EXISTS idx_lessonlog_subject ON LessonLog(Subject);
            CREATE INDEX IF NOT EXISTS idx_lessonlog_date ON LessonLog(Date);
            CREATE INDEX IF NOT EXISTS idx_lessonlog_grade_class ON LessonLog(Grade, Class);
        ";

        using (var cmd = new SqliteCommand(indexSql, _connection))
            cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 컬럼 추가 시도 (이미 존재하면 무시)
    /// </summary>
    private void TryAddColumn(string columnName, string columnDef)
    {
        try
        {
            using var cmd = new SqliteCommand(
                $"ALTER TABLE LessonLog ADD COLUMN {columnName} {columnDef}", _connection);
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            // 이미 존재하는 경우 무시
        }
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

        using var cmd = new SqliteCommand(sql, _connection);
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

        using var cmd = new SqliteCommand(sql, _connection);
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

        using var cmd = new SqliteCommand(sql, _connection);
        cmd.Parameters.AddWithValue("@No", no);

        return await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 단일 수업 기록 조회
    /// </summary>
    public async Task<LessonLog?> GetByIdAsync(int no)
    {
        const string sql = "SELECT * FROM LessonLog WHERE No = @No";

        using var cmd = new SqliteCommand(sql, _connection);
        cmd.Parameters.AddWithValue("@No", no);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return MapToLessonLog(reader);
        }
        return null;
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

        using var cmd = new SqliteCommand(sql, _connection);
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

        using var cmd = new SqliteCommand(sql, _connection);
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

        using var cmd = new SqliteCommand(sql, _connection);
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

        using var cmd = new SqliteCommand(sql, _connection);
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

        using var cmd = new SqliteCommand(sql, _connection);
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

        using var cmd = new SqliteCommand(sql, _connection);
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

        using var cmd = new SqliteCommand(sql, _connection);
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
        cmd.Parameters.AddWithValue("@TeacherID", log.TeacherID);
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
    {
        var logs = new List<LessonLog>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            logs.Add(MapToLessonLog(reader));
        }
        return logs;
    }

    private LessonLog MapToLessonLog(SqliteDataReader reader)
    {
        return new LessonLog
        {
            No = reader.GetInt32(reader.GetOrdinal("No")),
            Lesson = reader.IsDBNull(reader.GetOrdinal("Lesson")) ? null : reader.GetInt32(reader.GetOrdinal("Lesson")),
            TeacherID = reader.GetString(reader.GetOrdinal("TeacherID")),
            Year = reader.GetInt32(reader.GetOrdinal("Year")),
            Semester = reader.GetInt32(reader.GetOrdinal("Semester")),
            Date = DateTime.Parse(reader.GetString(reader.GetOrdinal("Date"))),
            Period = reader.IsDBNull(reader.GetOrdinal("Period")) ? 0 : reader.GetInt32(reader.GetOrdinal("Period")),
            Subject = reader.GetString(reader.GetOrdinal("Subject")),
            Grade = GetIntSafe(reader, "Grade"),
            Class = GetIntSafe(reader, "Class"),
            Room = GetStringSafe(reader, "Room"),
            CourseSectionNo = GetNullableIntSafe(reader, "CourseSectionNo"),
            SectionName = GetStringSafe(reader, "SectionName"),
            Topic = GetStringSafe(reader, "Topic"),
            Content = GetStringSafe(reader, "Content"),
            Note = GetStringSafe(reader, "Note"),
            CreatedAt = GetDateTimeSafe(reader, "CreatedAt"),
            UpdatedAt = GetDateTimeSafe(reader, "UpdatedAt")
        };
    }

    private static string GetStringSafe(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
    }

    private static int GetIntSafe(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? 0 : reader.GetInt32(ordinal);
    }

    private static int? GetNullableIntSafe(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    private static DateTime GetDateTimeSafe(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        if (reader.IsDBNull(ordinal)) return DateTime.Now;
        var str = reader.GetString(ordinal);
        return DateTime.TryParse(str, out var dt) ? dt : DateTime.Now;
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _connection?.Close();
                _connection?.Dispose();
            }
            _disposed = true;
        }
    }

    #endregion
}

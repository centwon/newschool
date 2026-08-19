using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Models;

namespace NewSchool.Repositories;

/// <summary>
/// Lesson Repository
/// 시간표 및 개별 수업 관리
/// </summary>
public class LessonRepository : BaseRepository
{
    public LessonRepository(string dbPath) : base(dbPath)
    {
        EnsureTableExists();
    }

    /// <summary>공유 연결 생성자 (UnitOfWork 전용). 테이블은 이미 존재한다고 가정하므로 DDL 을 실행하지 않는다.</summary>
    public LessonRepository(SqliteConnection connection) : base(connection) { }

    #region Table Management

    /// <summary>
    /// Lesson 스키마 정본. 예전에는 <c>DatabaseInitializer</c> 에도 따로 정의돼 있어
    /// 먼저 실행한 쪽에 따라 제약(DayOfWeek NOT NULL, Class 기본값, FK CASCADE)이 갈렸다.
    /// 정의를 이곳 하나로 모으고 초기화기가 이 상수를 실행한다.
    /// </summary>
    internal const string SchemaSql = @"
            CREATE TABLE IF NOT EXISTS Lesson (
                No INTEGER PRIMARY KEY AUTOINCREMENT,
                Course INTEGER NOT NULL,
                Teacher TEXT NOT NULL,
                Year INTEGER NOT NULL,
                Semester INTEGER NOT NULL,
                Date TEXT,
                DayOfWeek INTEGER NOT NULL,
                Period INTEGER NOT NULL,
                Grade INTEGER,
                Class INTEGER DEFAULT 0,
                Room TEXT,
                Topic TEXT,
                IsRecurring INTEGER DEFAULT 1,
                IsCompleted INTEGER DEFAULT 0,
                IsCancelled INTEGER DEFAULT 0,
                FOREIGN KEY (Course) REFERENCES Course(No) ON DELETE CASCADE
            );
            
            CREATE INDEX IF NOT EXISTS idx_lesson_course ON Lesson(Course);
            CREATE INDEX IF NOT EXISTS idx_lesson_teacher_year ON Lesson(Teacher, Year, Semester);
            CREATE INDEX IF NOT EXISTS idx_lesson_schedule ON Lesson(DayOfWeek, Period);
            CREATE INDEX IF NOT EXISTS idx_lesson_date ON Lesson(Date);
        ";

    private void EnsureTableExists()
    {
        const string sql = SchemaSql;

        try
        {
            using var cmd = CreateCommand(sql);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            LogError("Lesson 테이블 생성 실패", ex);
        }
    }

    #endregion

    #region Create

    /// <summary>
    /// 수업 생성
    /// </summary>
    public async Task<int> CreateAsync(Lesson lesson)
    {
        const string query = @"
            INSERT INTO Lesson (
                Course, Teacher, Year, Semester, Date, DayOfWeek, Period,
                Grade, Class, Room, Topic, IsRecurring, IsCompleted, IsCancelled
            ) VALUES (
                @Course, @Teacher, @Year, @Semester, @Date, @DayOfWeek, @Period,
                @Grade, @Class, @Room, @Topic, @IsRecurring, @IsCompleted, @IsCancelled
            );
            SELECT last_insert_rowid();";

        try
        {
            using var cmd = CreateCommand(query);
            AddLessonParameters(cmd, lesson);

            var result = await cmd.ExecuteScalarAsync();
            lesson.No = Convert.ToInt32(result);

            LogInfo($"수업 생성 완료: No={lesson.No}");
            return lesson.No;
        }
        catch (Exception ex)
        {
            LogError($"수업 생성 실패: Course={lesson.Course}", ex);
            throw;
        }
    }

    /// <summary>
    /// 정기 시간표 일괄 생성 (CourseSchedule 데이터 기반)
    /// 단일 트랜잭션 + 파라미터 재사용으로 배치 INSERT
    /// </summary>
    public async Task<int> CreateFromSchedulesAsync(
        int courseNo, string teacherId, int year, int semester,
        int grade, int classNum, List<(int DayOfWeek, int Period, string Room)> schedules)
    {
        if (schedules.Count == 0) return 0;

        const string query = @"
            INSERT INTO Lesson (
                Course, Teacher, Year, Semester, Date, DayOfWeek, Period,
                Grade, Class, Room, Topic, IsRecurring, IsCompleted, IsCancelled
            ) VALUES (
                @Course, @Teacher, @Year, @Semester, '', @DayOfWeek, @Period,
                @Grade, @Class, @Room, '', 1, 0, 0
            )";

        int count = 0;
        BeginTransaction();
        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@Course", courseNo);
            cmd.Parameters.AddWithValue("@Teacher", teacherId);
            cmd.Parameters.AddWithValue("@Year", year);
            cmd.Parameters.AddWithValue("@Semester", semester);
            cmd.Parameters.AddWithValue("@Grade", grade);
            cmd.Parameters.AddWithValue("@Class", classNum);
            cmd.Parameters.AddWithValue("@DayOfWeek", 0);
            cmd.Parameters.AddWithValue("@Period", 0);
            cmd.Parameters.AddWithValue("@Room", string.Empty);

            foreach (var (dayOfWeek, period, room) in schedules)
            {
                cmd.Parameters["@DayOfWeek"].Value = dayOfWeek;
                cmd.Parameters["@Period"].Value = period;
                cmd.Parameters["@Room"].Value = room ?? string.Empty;

                await cmd.ExecuteNonQueryAsync();
                count++;
            }

            Commit();
            LogInfo($"정기 시간표 일괄 생성: {count}개");
            return count;
        }
        catch
        {
            Rollback();
            throw;
        }
    }

    #endregion

    #region Read

    /// <summary>
    /// No로 수업 조회
    /// </summary>
    public async Task<Lesson?> GetByIdAsync(int no)
    {
        const string query = "SELECT * FROM Lesson WHERE No = @No";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@No", no);

            using var reader = await cmd.ExecuteReaderAsync();
            var cache = new ReaderColumnCache();
            cache.Initialize(reader);   // 컬럼 인덱스를 행마다 다시 찾지 않도록 한 번만
            if (await reader.ReadAsync())
            {
                return MapLesson(reader, cache);
            }
            return null;
        }
        catch (Exception ex)
        {
            LogError($"수업 조회 실패: No={no}", ex);
            throw;
        }
    }

    /// <summary>
    /// 과목(Course)별 수업 조회
    /// </summary>
    public async Task<List<Lesson>> GetByCourseAsync(int courseNo)
    {
        const string query = @"
            SELECT * FROM Lesson 
            WHERE Course = @Course
            ORDER BY DayOfWeek, Period";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@Course", courseNo);

            return await ExecuteQueryAsync(cmd);
        }
        catch (Exception ex)
        {
            LogError($"Course별 수업 조회 실패: Course={courseNo}", ex);
            throw;
        }
    }

    /// <summary>
    /// 여러 Course 의 수업을 한 번에 조회한다 (Course 번호 → 수업 목록).
    ///
    /// <para><see cref="GetByCourseAsync"/> 를 루프로 돌면 과목 수만큼 쿼리가 나간다.
    /// 필터 조건은 <see cref="GetByCourseAsync"/> 와 똑같이 두었다 — 교사 시간표 화면이
    /// 취소된 수업까지 보고 판단하고 있어서, 여기서 <c>IsCancelled</c> 를 걸러내면
    /// 화면 동작이 조용히 달라진다.</para>
    /// </summary>
    public async Task<Dictionary<int, List<Lesson>>> GetByCoursesAsync(IEnumerable<int> courseNos)
    {
        var ids = courseNos?.Distinct().ToList() ?? new List<int>();
        var result = new Dictionary<int, List<Lesson>>();
        if (ids.Count == 0) return result;

        var placeholders = string.Join(",", ids.Select((_, i) => $"@c{i}"));
        string query = $@"
            SELECT * FROM Lesson
            WHERE Course IN ({placeholders})
            ORDER BY DayOfWeek, Period";

        try
        {
            using var cmd = CreateCommand(query);
            for (int i = 0; i < ids.Count; i++)
                cmd.Parameters.AddWithValue($"@c{i}", ids[i]);

            foreach (var lesson in await ExecuteQueryAsync(cmd))
            {
                if (!result.TryGetValue(lesson.Course, out var list))
                    result[lesson.Course] = list = new List<Lesson>();
                list.Add(lesson);
            }
            return result;
        }
        catch (Exception ex)
        {
            LogError($"Course 일괄 수업 조회 실패: {ids.Count}건", ex);
            throw;
        }
    }

    /// <summary>
    /// 교사 시간표 조회 (정기 수업만)
    /// </summary>
    public async Task<List<Lesson>> GetTeacherScheduleAsync(
        string teacherId, int year, int semester)
    {
        const string query = @"
            SELECT * FROM Lesson 
            WHERE Teacher = @Teacher 
              AND Year = @Year 
              AND Semester = @Semester
              AND IsRecurring = 1
              AND IsCancelled = 0
            ORDER BY DayOfWeek, Period";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@Teacher", teacherId);
            cmd.Parameters.AddWithValue("@Year", year);
            cmd.Parameters.AddWithValue("@Semester", semester);

            return await ExecuteQueryAsync(cmd);
        }
        catch (Exception ex)
        {
            LogError($"교사 시간표 조회 실패: Teacher={teacherId}", ex);
            throw;
        }
    }

    /// <summary>
    /// 학급 시간표 조회 (정기 수업만)
    /// </summary>
    public async Task<List<Lesson>> GetClassScheduleAsync(
        int year, int semester, int grade, int classNum)
    {
        const string query = @"
            SELECT * FROM Lesson 
            WHERE Year = @Year 
              AND Semester = @Semester
              AND Grade = @Grade
              AND Class = @Class
              AND IsRecurring = 1
              AND IsCancelled = 0
            ORDER BY DayOfWeek, Period";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@Year", year);
            cmd.Parameters.AddWithValue("@Semester", semester);
            cmd.Parameters.AddWithValue("@Grade", grade);
            cmd.Parameters.AddWithValue("@Class", classNum);

            return await ExecuteQueryAsync(cmd);
        }
        catch (Exception ex)
        {
            LogError($"학급 시간표 조회 실패: {grade}-{classNum}", ex);
            throw;
        }
    }

    /// <summary>
    /// 특정 날짜의 수업 조회 (정기 + 비정기)
    /// </summary>
    public async Task<List<Lesson>> GetByDateAsync(string teacherId, DateTime date)
    {
        int dayOfWeek = ((int)date.DayOfWeek == 0) ? 7 : (int)date.DayOfWeek; // 일=7, 월=1...
        string dateStr = date.ToString("yyyy-MM-dd");

        const string query = @"
            SELECT * FROM Lesson 
            WHERE Teacher = @Teacher
              AND (
                  (IsRecurring = 1 AND DayOfWeek = @DayOfWeek AND IsCancelled = 0)
                  OR (IsRecurring = 0 AND Date = @Date AND IsCancelled = 0)
              )
            ORDER BY Period";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@Teacher", teacherId);
            cmd.Parameters.AddWithValue("@DayOfWeek", dayOfWeek);
            cmd.Parameters.AddWithValue("@Date", dateStr);

            return await ExecuteQueryAsync(cmd);
        }
        catch (Exception ex)
        {
            LogError($"날짜별 수업 조회 실패: {date:yyyy-MM-dd}", ex);
            throw;
        }
    }

    /// <summary>
    /// 특정 시간대 수업 조회 (충돌 확인용)
    /// </summary>
    public async Task<Lesson?> GetBySlotAsync(
        string teacherId, int year, int semester, int dayOfWeek, int period)
    {
        const string query = @"
            SELECT * FROM Lesson 
            WHERE Teacher = @Teacher
              AND Year = @Year
              AND Semester = @Semester
              AND DayOfWeek = @DayOfWeek
              AND Period = @Period
              AND IsRecurring = 1
              AND IsCancelled = 0
            LIMIT 1";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@Teacher", teacherId);
            cmd.Parameters.AddWithValue("@Year", year);
            cmd.Parameters.AddWithValue("@Semester", semester);
            cmd.Parameters.AddWithValue("@DayOfWeek", dayOfWeek);
            cmd.Parameters.AddWithValue("@Period", period);

            using var reader = await cmd.ExecuteReaderAsync();
            var cache = new ReaderColumnCache();
            cache.Initialize(reader);   // 컬럼 인덱스를 행마다 다시 찾지 않도록 한 번만
            if (await reader.ReadAsync())
            {
                return MapLesson(reader, cache);
            }
            return null;
        }
        catch (Exception ex)
        {
            LogError($"시간대 수업 조회 실패", ex);
            throw;
        }
    }

    #endregion

    #region Update

    /// <summary>
    /// 수업 수정
    /// </summary>
    public async Task<bool> UpdateAsync(Lesson lesson)
    {
        const string query = @"
            UPDATE Lesson SET
                Course = @Course,
                Teacher = @Teacher,
                Year = @Year,
                Semester = @Semester,
                Date = @Date,
                DayOfWeek = @DayOfWeek,
                Period = @Period,
                Grade = @Grade,
                Class = @Class,
                Room = @Room,
                Topic = @Topic,
                IsRecurring = @IsRecurring,
                IsCompleted = @IsCompleted,
                IsCancelled = @IsCancelled
            WHERE No = @No";

        try
        {
            using var cmd = CreateCommand(query);
            AddLessonParameters(cmd, lesson);

            int affected = await cmd.ExecuteNonQueryAsync();
            bool success = affected > 0;

            if (success)
                LogInfo($"수업 수정 완료: No={lesson.No}");

            return success;
        }
        catch (Exception ex)
        {
            LogError($"수업 수정 실패: No={lesson.No}", ex);
            throw;
        }
    }

    /// <summary>
    /// 수업 완료 처리
    /// </summary>
    public async Task<bool> MarkCompletedAsync(int no, bool isCompleted = true)
    {
        const string query = "UPDATE Lesson SET IsCompleted = @IsCompleted WHERE No = @No";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@No", no);
            cmd.Parameters.AddWithValue("@IsCompleted", isCompleted ? 1 : 0);

            return await cmd.ExecuteNonQueryAsync() > 0;
        }
        catch (Exception ex)
        {
            LogError($"수업 완료 처리 실패: No={no}", ex);
            throw;
        }
    }

    /// <summary>
    /// 수업 취소 처리
    /// </summary>
    public async Task<bool> MarkCancelledAsync(int no, bool isCancelled = true)
    {
        const string query = "UPDATE Lesson SET IsCancelled = @IsCancelled WHERE No = @No";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@No", no);
            cmd.Parameters.AddWithValue("@IsCancelled", isCancelled ? 1 : 0);

            return await cmd.ExecuteNonQueryAsync() > 0;
        }
        catch (Exception ex)
        {
            LogError($"수업 취소 처리 실패: No={no}", ex);
            throw;
        }
    }

    #endregion

    #region Delete

    /// <summary>
    /// 수업 삭제
    /// </summary>
    public async Task<bool> DeleteAsync(int no)
    {
        const string query = "DELETE FROM Lesson WHERE No = @No";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@No", no);

            int affected = await cmd.ExecuteNonQueryAsync();
            bool success = affected > 0;

            if (success)
                LogInfo($"수업 삭제 완료: No={no}");

            return success;
        }
        catch (Exception ex)
        {
            LogError($"수업 삭제 실패: No={no}", ex);
            throw;
        }
    }

    /// <summary>
    /// Course에 해당하는 모든 수업 삭제
    /// </summary>
    public async Task<int> DeleteByCourseAsync(int courseNo)
    {
        const string query = "DELETE FROM Lesson WHERE Course = @Course";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@Course", courseNo);

            int affected = await cmd.ExecuteNonQueryAsync();
            LogInfo($"Course별 수업 삭제: Course={courseNo}, 삭제={affected}개");

            return affected;
        }
        catch (Exception ex)
        {
            LogError($"Course별 수업 삭제 실패: Course={courseNo}", ex);
            throw;
        }
    }

    #endregion

    #region Helper Methods

    private void AddLessonParameters(SqliteCommand cmd, Lesson lesson)
    {
        cmd.Parameters.AddWithValue("@No", lesson.No);
        cmd.Parameters.AddWithValue("@Course", lesson.Course);
        cmd.Parameters.AddWithValue("@Teacher", lesson.Teacher);
        cmd.Parameters.AddWithValue("@Year", lesson.Year);
        cmd.Parameters.AddWithValue("@Semester", lesson.Semester);
        cmd.Parameters.AddWithValue("@Date", lesson.Date ?? string.Empty);
        cmd.Parameters.AddWithValue("@DayOfWeek", lesson.DayOfWeek);
        cmd.Parameters.AddWithValue("@Period", lesson.Period);
        cmd.Parameters.AddWithValue("@Grade", lesson.Grade);
        cmd.Parameters.AddWithValue("@Class", lesson.Class);
        cmd.Parameters.AddWithValue("@Room", lesson.Room ?? string.Empty);
        cmd.Parameters.AddWithValue("@Topic", lesson.Topic ?? string.Empty);
        cmd.Parameters.AddWithValue("@IsRecurring", lesson.IsRecurring ? 1 : 0);
        cmd.Parameters.AddWithValue("@IsCompleted", lesson.IsCompleted ? 1 : 0);
        cmd.Parameters.AddWithValue("@IsCancelled", lesson.IsCancelled ? 1 : 0);
    }

    private async Task<List<Lesson>> ExecuteQueryAsync(SqliteCommand cmd)
    {
        var lessons = new List<Lesson>();
        using var reader = await cmd.ExecuteReaderAsync();
        var cache = new ReaderColumnCache();
        cache.Initialize(reader);   // 컬럼 인덱스를 행마다 다시 찾지 않도록 한 번만
        while (await reader.ReadAsync())
        {
            lessons.Add(MapLesson(reader, cache));
        }
        return lessons;
    }

    private Lesson MapLesson(SqliteDataReader reader, ReaderColumnCache cache)
    {
        return new Lesson
        {
            No = reader.GetInt32(cache.GetOrdinal("No")),
            Course = reader.GetInt32(cache.GetOrdinal("Course")),
            Teacher = reader.GetString(cache.GetOrdinal("Teacher")),
            Year = reader.GetInt32(cache.GetOrdinal("Year")),
            Semester = reader.GetInt32(cache.GetOrdinal("Semester")),
            Date = reader.IsDBNull(cache.GetOrdinal("Date")) ? string.Empty : reader.GetString(cache.GetOrdinal("Date")),
            DayOfWeek = reader.IsDBNull(cache.GetOrdinal("DayOfWeek")) ? 0 : reader.GetInt32(cache.GetOrdinal("DayOfWeek")),
            Period = reader.GetInt32(cache.GetOrdinal("Period")),
            Grade = reader.IsDBNull(cache.GetOrdinal("Grade")) ? 0 : reader.GetInt32(cache.GetOrdinal("Grade")),
            Class = reader.IsDBNull(cache.GetOrdinal("Class")) ? 0 : reader.GetInt32(cache.GetOrdinal("Class")),
            Room = reader.IsDBNull(cache.GetOrdinal("Room")) ? string.Empty : reader.GetString(cache.GetOrdinal("Room")),
            Topic = reader.IsDBNull(cache.GetOrdinal("Topic")) ? string.Empty : reader.GetString(cache.GetOrdinal("Topic")),
            IsRecurring = reader.GetInt32(cache.GetOrdinal("IsRecurring")) == 1,
            IsCompleted = reader.GetInt32(cache.GetOrdinal("IsCompleted")) == 1,
            IsCancelled = reader.GetInt32(cache.GetOrdinal("IsCancelled")) == 1
        };
    }

    #endregion
}

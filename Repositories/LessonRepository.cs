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
    // 여섯 열(Date·Class·Topic·IsRecurring·IsCompleted·IsCancelled)을 뺐다(2026-08-29).
    // 채우는 코드가 없어 만들어진 뒤로 줄곧 기본값이었고, 그 일들은 각각 게시판 일지와
    // LessonChange 가 이미 하고 있다 — 근거는 Models/Lesson.cs 머리 주석.
    //
    // ⚠ 마이그레이션은 두지 않았다. 이미 만들어진 DB 는 그 여섯 열을 그대로 안고 가되,
    // 읽지도 쓰지도 않으므로 값이 없는 채로 남는다(전부 NULL 허용이거나 DEFAULT 가 있어
    // 이 INSERT 는 옛 DB 에서도 그대로 통한다). 새로 만드는 DB 에만 열이 없다.
    internal const string SchemaSql = @"
            CREATE TABLE IF NOT EXISTS Lesson (
                No INTEGER PRIMARY KEY AUTOINCREMENT,
                Course INTEGER NOT NULL,
                Teacher TEXT NOT NULL,
                Year INTEGER NOT NULL,
                Semester INTEGER NOT NULL,
                DayOfWeek INTEGER NOT NULL,
                Period INTEGER NOT NULL,
                Grade INTEGER,
                Room TEXT,
                FOREIGN KEY (Course) REFERENCES Course(No) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_lesson_course ON Lesson(Course);
            CREATE INDEX IF NOT EXISTS idx_lesson_teacher_year ON Lesson(Teacher, Year, Semester);
            CREATE INDEX IF NOT EXISTS idx_lesson_schedule ON Lesson(DayOfWeek, Period);
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
                Course, Teacher, Year, Semester, DayOfWeek, Period, Grade, Room
            ) VALUES (
                @Course, @Teacher, @Year, @Semester, @DayOfWeek, @Period, @Grade, @Room
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

    // 정기 시간표 일괄 생성(CreateFromSchedulesAsync)은 이를 부르던
    // TeacherTimetableService.CreateScheduleFromCourseAsync 와 함께 지웠다(39차)
    // — 둘 다 호출부가 없었다. (그때 그 서비스의 이름은 LessonService 였다.)

    #endregion

    #region Read

    // No 하나로 읽는 조회(GetByIdAsync)는 지웠다 — 유일한 호출자가 TeacherTimetableService 의
    // 통과 래퍼였고 그것도 부르는 곳이 없었다. 시간표 화면들은 언제나 묶음으로 읽는다
    // (교사별·과목별·날짜별). 한 줄만 필요해지면 그때 되살리면 된다.

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

    // 여러 Course 를 IN 절로 묶어 읽던 GetByCoursesAsync 는 이를 쓰던
    // TimetableService.GetTeacherTimetableAsync 와 함께 지웠다(39차).
    // (N+1 을 없애려고 만든 것이었는데, 그 유일한 호출부가 사라졌다.)

    /// <summary>
    /// 교사 시간표 조회
    /// </summary>
    public async Task<List<Lesson>> GetTeacherScheduleAsync(
        string teacherId, int year, int semester)
    {
        // IsRecurring=1·IsCancelled=0 조건은 그 열들과 함께 걷어냈다 — 두 값이 늘 기본값이라
        // 아무것도 거르지 않던 조건이다. 휴강은 LessonChange 가 따로 얹는다.
        const string query = @"
            SELECT * FROM Lesson
            WHERE Teacher = @Teacher
              AND Year = @Year
              AND Semester = @Semester
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

    // 학급 시간표 조회(GetClassScheduleAsync)는 지웠다 — 호출부가 없었고, **있었다면 늘 빈
    // 목록을 돌려줬다.** 조건에 `Class = @Class` 가 있는데 Lesson.Class 를 채우는 코드가 한
    // 곳도 없어(유일한 생성 지점인 CourseTimetableBoard 가 Grade 만 넣는다) 항상 0 이었다.
    //
    // 이름이 "학급 시간표" 라 다음에 그게 필요한 사람이 반드시 집어 들 자리였고, 그러면
    // 아무것도 안 나오는 이유를 한참 찾게 된다. 학급 시간표는 ClassTimetable 이 맡는다 —
    // Lesson 은 교사 관점(내 수업이 언제 어디)이라 애초에 다른 표다.

    /// <summary>
    /// 그 날 요일에 있는 수업 조회.
    ///
    /// <para><b>학년도·학기로 반드시 거른다.</b> 예전에는 교사와 요일만 봐서, 학년도가 바뀌면
    /// <b>작년 같은 요일 수업이 "오늘의 수업" 에 섞였다.</b> 과목 목록은 올해 것만이라 과목명이
    /// 빈 유령 행으로 뜨고 "N시간 중 M건" 의 N 도 함께 부풀려진다. 배포 첫 해라 아직 드러나지
    /// 않았을 뿐, 첫 학년도 롤오버에 바로 나타날 자리였다.</para>
    ///
    /// <para>비정기 수업 갈래(<c>IsRecurring=0 AND Date=…</c>)는 없앴다 — 그 두 열을 채우는
    /// 코드가 없어 한 번도 타지 않는 가지였다. 날짜 하나짜리 수업(보강)은 <c>LessonChange</c>
    /// 가 맡는다.</para>
    /// </summary>
    public async Task<List<Lesson>> GetByDateAsync(
        string teacherId, DateTime date, int year, int semester)
    {
        int dayOfWeek = ((int)date.DayOfWeek == 0) ? 7 : (int)date.DayOfWeek; // 일=7, 월=1...

        const string query = @"
            SELECT * FROM Lesson
            WHERE Teacher = @Teacher
              AND Year = @Year
              AND Semester = @Semester
              AND DayOfWeek = @DayOfWeek
            ORDER BY Period";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@Teacher", teacherId);
            cmd.Parameters.AddWithValue("@Year", year);
            cmd.Parameters.AddWithValue("@Semester", semester);
            cmd.Parameters.AddWithValue("@DayOfWeek", dayOfWeek);

            return await ExecuteQueryAsync(cmd);
        }
        catch (Exception ex)
        {
            LogError($"날짜별 수업 조회 실패: {date:yyyy-MM-dd}", ex);
            throw;
        }
    }

    // 시간대 수업 조회(GetBySlotAsync)는 이를 쓰던 TeacherTimetableService.HasConflictAsync
    // 와 함께 지웠다(39차. 그때 그 서비스의 이름은 LessonService 였다).

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
                DayOfWeek = @DayOfWeek,
                Period = @Period,
                Grade = @Grade,
                Room = @Room
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

    // 완료·취소 처리(MarkCompletedAsync·MarkCancelledAsync)는 지웠다 — 부르는 곳이 한 곳도
    // 없었다. 그래서 Lesson.IsCompleted·IsCancelled 는 테이블이 생긴 뒤로 줄곧 0 이고,
    // 그 둘로 거르는 조회들은 조건이 없는 것과 같이 돈다.
    //
    // ⚠ 되살리기 전에 읽을 것 — 휴강을 IsCancelled 로 하면 안 된다. Lesson 은 정기 시간표라
    // 행 하나가 "매주 그 교시" 를 뜻하므로, 취소를 세우면 그 수업이 **매주** 사라진다.
    // 특정 날짜 한 교시만 바꾸는 일은 LessonChange 가 맡는다(Models/LessonChange.cs 머리 주석).

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
        cmd.Parameters.AddWithValue("@DayOfWeek", lesson.DayOfWeek);
        cmd.Parameters.AddWithValue("@Period", lesson.Period);
        cmd.Parameters.AddWithValue("@Grade", lesson.Grade);
        cmd.Parameters.AddWithValue("@Room", lesson.Room ?? string.Empty);
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
            DayOfWeek = reader.IsDBNull(cache.GetOrdinal("DayOfWeek")) ? 0 : reader.GetInt32(cache.GetOrdinal("DayOfWeek")),
            Period = reader.GetInt32(cache.GetOrdinal("Period")),
            Grade = reader.IsDBNull(cache.GetOrdinal("Grade")) ? 0 : reader.GetInt32(cache.GetOrdinal("Grade")),
            Room = reader.IsDBNull(cache.GetOrdinal("Room")) ? string.Empty : reader.GetString(cache.GetOrdinal("Room"))
        };
        // 걷어낸 여섯 열은 여기서도 읽지 않는다. 이미 만들어진 DB 에는 그 열이 남아 있지만
        // SELECT * 로 딸려 올 뿐이고, 아무도 보지 않는다.
    }

    #endregion
}

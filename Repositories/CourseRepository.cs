using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Models;

namespace NewSchool.Repositories;

/// <summary>
/// Course Repository
/// 수업 개설 정보 관리
/// ⭐ 재설계: 시간표 정보는 CourseSchedule로 분리
/// </summary>
public class CourseRepository : BaseRepository
{
    public CourseRepository(string dbPath) : base(dbPath) { }

    /// <summary>공유 연결 생성자 (UnitOfWork 전용). 테이블은 이미 존재한다고 가정하므로 DDL 을 실행하지 않는다.</summary>
    public CourseRepository(SqliteConnection connection) : base(connection) { }

    #region Create

    /// <summary>
    /// 수업 생성
    /// </summary>
    public async Task<int> CreateAsync(Course course)
    {
        const string query = @"
                INSERT INTO Course (
                    SchoolCode, TeacherID, Year, Semester, Grade,
                    Subject, Unit, Type, Rooms, Remark
                ) VALUES (
                    @SchoolCode, @TeacherID, @Year, @Semester, @Grade,
                    @Subject, @Unit, @Type, @Rooms, @Remark
                );
                SELECT last_insert_rowid();";

        try
        {
            using var cmd = CreateCommand(query);
            AddCourseParameters(cmd, course);

            var result = await cmd.ExecuteScalarAsync();
            course.No = Convert.ToInt32(result);

            LogInfo($"수업 생성 완료: No={course.No}, Subject={course.Subject}");
            return course.No;
        }
        catch (Exception ex)
        {
            LogError($"수업 생성 실패: Subject={course.Subject}", ex);
            throw;
        }
    }

    #endregion

    #region Read

    /// <summary>
    /// No로 수업 조회
    /// </summary>
    public async Task<Course?> GetByIdAsync(int no)
    {
        const string query = "SELECT * FROM Course WHERE No = @No";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@No", no);

            using var reader = await cmd.ExecuteReaderAsync();
            var cache = new ReaderColumnCache();
            cache.Initialize(reader);   // 컬럼 인덱스를 행마다 다시 찾지 않도록 한 번만
            if (await reader.ReadAsync())
            {
                return MapCourse(reader, cache);
            }

            return null;
        }
        catch (Exception ex)
        {
            LogError($"수업 조회 실패: No={no}", ex);
            throw;
        }
    }
    /// </summary>
    /// course가 등록된 학년도 목록 조회 -techeerid 기준
    /// </summary>
    public async Task<List<int>> GetDistinctCourseYearsAsync(string teacherId)
    {
        const string query = @"
                SELECT DISTINCT Year FROM Course 
                WHERE TeacherID = @TeacherID
                ORDER BY Year DESC";
        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@TeacherID", teacherId);
            var years = new List<int>();
            using var reader = await cmd.ExecuteReaderAsync();
            var cache = new ReaderColumnCache();
            cache.Initialize(reader);   // 컬럼 인덱스를 행마다 다시 찾지 않도록 한 번만
            while (await reader.ReadAsync())
            {
                years.Add(reader.GetInt32(0));
            }
            return years;
        }
        catch (Exception ex)
        {
            LogError($"수업 학년도 목록 조회 실패: TeacherID={teacherId}", ex);
            throw;
        }
    }

    /// <summary>
    /// 학교/학년도/학기별 수업 목록 조회
    /// </summary>
    public async Task<List<Course>> GetBySchoolAsync(
        string schoolCode, int year, int semester)
    {
        const string query = @"
                SELECT * FROM Course 
                WHERE SchoolCode = @SchoolCode 
                  AND Year = @Year 
                  AND Semester = @Semester
                ORDER BY Grade, Subject";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@SchoolCode", schoolCode);
            cmd.Parameters.AddWithValue("@Year", year);
            cmd.Parameters.AddWithValue("@Semester", semester);

            var courses = new List<Course>();
            using var reader = await cmd.ExecuteReaderAsync();
            var cache = new ReaderColumnCache();
            cache.Initialize(reader);   // 컬럼 인덱스를 행마다 다시 찾지 않도록 한 번만
            while (await reader.ReadAsync())
            {
                courses.Add(MapCourse(reader, cache));
            }

            return courses;
        }
        catch (Exception ex)
        {
            LogError($"수업 목록 조회 실패: SchoolCode={schoolCode}, Year={year}, Semester={semester}", ex);
            throw;
        }
    }

    /// <summary>
    /// 교사별 수업 목록 조회
    /// </summary>
    public async Task<List<Course>> GetByTeacherAsync(            string teacherId, int year, int semester)
    {
        const string query = @"
                SELECT * FROM Course 
                WHERE TeacherID = @TeacherID 
                  AND Year = @Year 
                  AND Semester = @Semester
                ORDER BY Grade, Subject";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@TeacherID", teacherId);
            cmd.Parameters.AddWithValue("@Year", year);
            cmd.Parameters.AddWithValue("@Semester", semester);

            var courses = new List<Course>();
            using var reader = await cmd.ExecuteReaderAsync();
            var cache = new ReaderColumnCache();
            cache.Initialize(reader);   // 컬럼 인덱스를 행마다 다시 찾지 않도록 한 번만
            while (await reader.ReadAsync())
            {
                courses.Add(MapCourse(reader, cache));
            }

            return courses;
        }
        catch (Exception ex)
        {
            LogError($"교사별 수업 목록 조회 실패: TeacherID={teacherId}", ex);
            throw;
        }
    }

    // 학년별 수업 목록(GetByGradeAsync)은 호출부가 없어 지웠다(39차) —
    // 수업은 담당 교사 기준으로 읽는다.

    /// <summary>
    /// 여러 No로 수업 일괄 조회
    /// </summary>
    public async Task<List<Course>> GetByIdsAsync(List<int> ids)
    {
        if (ids == null || ids.Count == 0)
            return [];

        var placeholders = string.Join(",", ids.Select((_, i) => $"@id{i}"));
        var query = $"SELECT * FROM Course WHERE No IN ({placeholders})";

        try
        {
            using var cmd = CreateCommand(query);
            for (int i = 0; i < ids.Count; i++)
            {
                cmd.Parameters.AddWithValue($"@id{i}", ids[i]);
            }

            var courses = new List<Course>();
            using var reader = await cmd.ExecuteReaderAsync();
            var cache = new ReaderColumnCache();
            cache.Initialize(reader);   // 컬럼 인덱스를 행마다 다시 찾지 않도록 한 번만
            while (await reader.ReadAsync())
            {
                courses.Add(MapCourse(reader, cache));
            }

            return courses;
        }
        catch (Exception ex)
        {
            LogError($"수업 일괄 조회 실패: {ids.Count}건", ex);
            throw;
        }
    }

    #endregion

    #region Update

    /// <summary>
    /// 수업 정보 수정
    /// </summary>
    public async Task<bool> UpdateAsync(Course course)
    {
        const string query = @"
                UPDATE Course SET
                    SchoolCode = @SchoolCode,
                    TeacherID = @TeacherID,
                    Year = @Year,
                    Semester = @Semester,
                    Grade = @Grade,
                    Subject = @Subject,
                    Unit = @Unit,
                    Type = @Type,
                    Rooms = @Rooms,
                    Remark = @Remark
                WHERE No = @No";

        try
        {
            using var cmd = CreateCommand(query);
            AddCourseParameters(cmd, course);

            int affected = await cmd.ExecuteNonQueryAsync();
            bool success = affected > 0;

            if (success)
                LogInfo($"수업 수정 완료: No={course.No}");
            else
                LogWarning($"수업 수정 실패: No={course.No}");

            return success;
        }
        catch (Exception ex)
        {
            LogError($"수업 수정 실패: No={course.No}", ex);
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
        const string query = "DELETE FROM Course WHERE No = @No";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@No", no);

            int affected = await cmd.ExecuteNonQueryAsync();
            bool success = affected > 0;

            if (success)
                LogInfo($"수업 삭제 완료: No={no}");
            else
                LogWarning($"수업 삭제 실패: No={no}");

            return success;
        }
        catch (Exception ex)
        {
            LogError($"수업 삭제 실패: No={no}", ex);
            throw;
        }
    }

    #endregion

    #region Helper Methods

    private void AddCourseParameters(SqliteCommand cmd, Course course)
    {
        cmd.Parameters.AddWithValue("@No", course.No);
        cmd.Parameters.AddWithValue("@SchoolCode", course.SchoolCode ?? string.Empty);
        cmd.Parameters.AddWithValue("@TeacherID", course.TeacherID ?? string.Empty);
        cmd.Parameters.AddWithValue("@Year", course.Year);
        cmd.Parameters.AddWithValue("@Semester", course.Semester);
        cmd.Parameters.AddWithValue("@Grade", course.Grade);
        cmd.Parameters.AddWithValue("@Subject", course.Subject ?? string.Empty);
        cmd.Parameters.AddWithValue("@Unit", course.Unit);
        cmd.Parameters.AddWithValue("@Type", course.Type ?? CourseTypes.Class);
        cmd.Parameters.AddWithValue("@Rooms", course.Rooms ?? string.Empty);  // ✅ Rooms 추가
        cmd.Parameters.AddWithValue("@Remark", course.Remark ?? string.Empty);
    }

    private Course MapCourse(SqliteDataReader reader, ReaderColumnCache cache)
    {
        return new Course
        {
            No = reader.GetInt32(cache.GetOrdinal("No")),
            SchoolCode = reader.GetString(cache.GetOrdinal("SchoolCode")),
            TeacherID = reader.GetString(cache.GetOrdinal("TeacherID")),
            Year = reader.GetInt32(cache.GetOrdinal("Year")),
            Semester = reader.GetInt32(cache.GetOrdinal("Semester")),
            Grade = reader.GetInt32(cache.GetOrdinal("Grade")),
            Subject = reader.GetString(cache.GetOrdinal("Subject")),
            Unit = reader.GetInt32(cache.GetOrdinal("Unit")),
            Type = reader.GetString(cache.GetOrdinal("Type")),
            Rooms = reader.IsDBNull(cache.GetOrdinal("Rooms")) ? string.Empty : reader.GetString(cache.GetOrdinal("Rooms")),
            Remark = reader.GetString(cache.GetOrdinal("Remark"))
        };
    }

    #endregion
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Models;

namespace NewSchool.Repositories
{
    /// <summary>
    /// Course Repository
    /// 수업 개설 정보 관리
    /// ⭐ 재설계: 시간표 정보는 CourseSchedule로 분리
    /// </summary>
    public class CourseRepository : BaseRepository
    {
        public CourseRepository(string dbPath) : base(dbPath) { }

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
                if (await reader.ReadAsync())
                {
                    return MapCourse(reader);
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
                while (await reader.ReadAsync())
                {
                    courses.Add(MapCourse(reader));
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
                while (await reader.ReadAsync())
                {
                    courses.Add(MapCourse(reader));
                }

                return courses;
            }
            catch (Exception ex)
            {
                LogError($"교사별 수업 목록 조회 실패: TeacherID={teacherId}", ex);
                throw;
            }
        }

        /// <summary>
        /// 학년별 수업 목록 조회
        /// </summary>
        public async Task<List<Course>> GetByGradeAsync(
            string schoolCode, int year, int semester, int grade)
        {
            const string query = @"
                SELECT * FROM Course 
                WHERE SchoolCode = @SchoolCode 
                  AND Year = @Year 
                  AND Semester = @Semester
                  AND Grade = @Grade
                ORDER BY Subject";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@SchoolCode", schoolCode);
                cmd.Parameters.AddWithValue("@Year", year);
                cmd.Parameters.AddWithValue("@Semester", semester);
                cmd.Parameters.AddWithValue("@Grade", grade);

                var courses = new List<Course>();
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    courses.Add(MapCourse(reader));
                }

                return courses;
            }
            catch (Exception ex)
            {
                LogError($"학년별 수업 목록 조회 실패: Grade={grade}", ex);
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
            cmd.Parameters.AddWithValue("@Type", course.Type ?? "Class");
            cmd.Parameters.AddWithValue("@Rooms", course.Rooms ?? string.Empty);  // ✅ Rooms 추가
            cmd.Parameters.AddWithValue("@Remark", course.Remark ?? string.Empty);
        }

        private Course MapCourse(SqliteDataReader reader)
        {
            return new Course
            {
                No = reader.GetInt32(reader.GetOrdinal("No")),
                SchoolCode = reader.GetString(reader.GetOrdinal("SchoolCode")),
                TeacherID = reader.GetString(reader.GetOrdinal("TeacherID")),
                Year = reader.GetInt32(reader.GetOrdinal("Year")),
                Semester = reader.GetInt32(reader.GetOrdinal("Semester")),
                Grade = reader.GetInt32(reader.GetOrdinal("Grade")),
                Subject = reader.GetString(reader.GetOrdinal("Subject")),
                Unit = reader.GetInt32(reader.GetOrdinal("Unit")),
                Type = reader.GetString(reader.GetOrdinal("Type")),
                Rooms = reader.IsDBNull(reader.GetOrdinal("Rooms")) ? string.Empty : reader.GetString(reader.GetOrdinal("Rooms")),
                Remark = reader.GetString(reader.GetOrdinal("Remark"))
            };
        }

        #endregion
    }
}

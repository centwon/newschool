using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Models;

namespace NewSchool.Repositories
{
    /// <summary>
    /// StudentSpecial Repository
    /// 학교생활기록부 특기사항 데이터 접근
    /// DB 칼럼명 IsActive 유지, C# IsFinalized로 반전 매핑
    /// </summary>
    public class StudentSpecialRepository : BaseRepository
    {
        public StudentSpecialRepository(string dbPath) : base(dbPath) { }

        #region Create

        /// <summary>
        /// 학생부 기록 생성
        /// </summary>
        public async Task<int> CreateAsync(StudentSpecial special)
        {
            const string query = @"
                INSERT INTO StudentSpecial (
                    StudentID, Year, Type, Title, Content, Date, TeacherID, 
                    CourseNo, SubjectName, IsActive, Tag
                ) VALUES (
                    @StudentID, @Year, @Type, @Title, @Content, @Date, @TeacherID,
                    @CourseNo, @SubjectName, @IsActive, @Tag
                );
                SELECT last_insert_rowid();";

            try
            {
                using var cmd = CreateCommand(query);
                AddSpecialParameters(cmd, special);

                var result = await cmd.ExecuteScalarAsync();
                special.No = Convert.ToInt32(result);

                LogInfo($"학생부 기록 생성 완료: No={special.No}, StudentID={special.StudentID}");
                return special.No;
            }
            catch (Exception ex)
            {
                LogError($"학생부 기록 생성 실패: StudentID={special.StudentID}", ex);
                throw;
            }
        }

        #endregion

        #region Read

        /// <summary>
        /// No로 학생부 기록 조회
        /// </summary>
        public async Task<StudentSpecial?> GetByIdAsync(int no)
        {
            const string query = "SELECT * FROM StudentSpecial WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@No", no);

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return MapStudentSpecial(reader);
                }

                return null;
            }
            catch (Exception ex)
            {
                LogError($"학생부 기록 조회 실패: No={no}", ex);
                throw;
            }
        }

        /// <summary>
        /// 학생별 학생부 기록 조회
        /// </summary>
        public async Task<List<StudentSpecial>> GetByStudentAsync(string studentId, int year)
        {
            const string query = @"
                SELECT * FROM StudentSpecial 
                WHERE StudentID = @StudentID 
                  AND Year = @Year
                ORDER BY Date DESC";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@StudentID", studentId);
                cmd.Parameters.AddWithValue("@Year", year);

                var specials = new List<StudentSpecial>();
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    specials.Add(MapStudentSpecial(reader));
                }

                return specials;
            }
            catch (Exception ex)
            {
                LogError($"학생별 학생부 기록 조회 실패: StudentID={studentId}", ex);
                throw;
            }
        }

        /// <summary>
        /// 학생의 미마감(작성 중) 기록 조회
        /// </summary>
        public async Task<List<StudentSpecial>> GetDraftByStudentAsync(string studentId)
        {
            const string query = @"
                SELECT * FROM StudentSpecial 
                WHERE StudentID = @StudentID 
                  AND IsActive = 1
                ORDER BY Year DESC, Date DESC";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@StudentID", studentId);

                var specials = new List<StudentSpecial>();
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    specials.Add(MapStudentSpecial(reader));
                }

                return specials;
            }
            catch (Exception ex)
            {
                LogError($"학생 미마감 기록 조회 실패: StudentID={studentId}", ex);
                throw;
            }
        }

        /// <summary>
        /// CourseNo별 학생부 기록 조회
        /// </summary>
        public async Task<List<StudentSpecial>> GetByCourseAsync(int courseNo, int year)
        {
            const string query = @"
                SELECT * FROM StudentSpecial 
                WHERE CourseNo = @CourseNo
                  AND Year = @Year
                ORDER BY Date DESC";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@CourseNo", courseNo);
                cmd.Parameters.AddWithValue("@Year", year);

                var specials = new List<StudentSpecial>();
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    specials.Add(MapStudentSpecial(reader));
                }

                return specials;
            }
            catch (Exception ex)
            {
                LogError($"수업별 학생부 기록 조회 실패: CourseNo={courseNo}", ex);
                throw;
            }
        }

        /// <summary>
        /// 영역별 학생부 기록 조회
        /// </summary>
        public async Task<List<StudentSpecial>> GetByTypeAsync(string type, int year)
        {
            const string query = @"
                SELECT * FROM StudentSpecial 
                WHERE Type = @Type 
                  AND Year = @Year
                ORDER BY Date DESC";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@Type", type);
                cmd.Parameters.AddWithValue("@Year", year);

                var specials = new List<StudentSpecial>();
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    specials.Add(MapStudentSpecial(reader));
                }

                return specials;
            }
            catch (Exception ex)
            {
                LogError($"영역별 학생부 기록 조회 실패: Type={type}", ex);
                throw;
            }
        }

        /// <summary>
        /// 영역별 미마감(작성 중) 기록 조회
        /// </summary>
        public async Task<List<StudentSpecial>> GetDraftByTypeAsync(string type)
        {
            const string query = @"
                SELECT * FROM StudentSpecial 
                WHERE Type = @Type 
                  AND IsActive = 1
                ORDER BY Year DESC, Date DESC";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@Type", type);

                var specials = new List<StudentSpecial>();
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    specials.Add(MapStudentSpecial(reader));
                }

                return specials;
            }
            catch (Exception ex)
            {
                LogError($"영역별 미마감 기록 조회 실패: Type={type}", ex);
                throw;
            }
        }

        /// <summary>
        /// 교사가 작성한 학생부 기록 조회
        /// </summary>
        public async Task<List<StudentSpecial>> GetByTeacherAsync(string teacherId, int year)
        {
            const string query = @"
                SELECT * FROM StudentSpecial 
                WHERE TeacherID = @TeacherID 
                  AND Year = @Year
                ORDER BY Date DESC";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@TeacherID", teacherId);
                cmd.Parameters.AddWithValue("@Year", year);

                var specials = new List<StudentSpecial>();
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    specials.Add(MapStudentSpecial(reader));
                }

                return specials;
            }
            catch (Exception ex)
            {
                LogError($"교사별 학생부 기록 조회 실패: TeacherID={teacherId}", ex);
                throw;
            }
        }

        /// <summary>
        /// 키워드로 학생부 기록 검색
        /// </summary>
        public async Task<List<StudentSpecial>> SearchAsync(string keyword, int year)
        {
            const string query = @"
                SELECT * FROM StudentSpecial 
                WHERE Year = @Year
                  AND (Title LIKE @Keyword OR Content LIKE @Keyword OR Tag LIKE @Keyword OR SubjectName LIKE @Keyword)
                ORDER BY Date DESC
                LIMIT 100";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@Year", year);
                cmd.Parameters.AddWithValue("@Keyword", $"%{keyword}%");

                var specials = new List<StudentSpecial>();
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    specials.Add(MapStudentSpecial(reader));
                }

                return specials;
            }
            catch (Exception ex)
            {
                LogError($"학생부 기록 검색 실패: Keyword={keyword}", ex);
                throw;
            }
        }

        /// <summary>
        /// 영역별 통계 (건수)
        /// </summary>
        public async Task<Dictionary<string, int>> GetCountByTypeAsync(int year)
        {
            const string query = @"
                SELECT Type, COUNT(*) as Count 
                FROM StudentSpecial 
                WHERE Year = @Year
                GROUP BY Type";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@Year", year);

                var counts = new Dictionary<string, int>();
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var type = reader.GetString(0);
                    var count = reader.GetInt32(1);
                    counts[type] = count;
                }

                return counts;
            }
            catch (Exception ex)
            {
                LogError($"영역별 통계 조회 실패: Year={year}", ex);
                throw;
            }
        }

        /// <summary>
        /// 미마감 기록 통계
        /// </summary>
        public async Task<Dictionary<string, int>> GetDraftCountByTypeAsync()
        {
            const string query = @"
                SELECT Type, COUNT(*) as Count 
                FROM StudentSpecial 
                WHERE IsActive = 1
                GROUP BY Type";

            try
            {
                using var cmd = CreateCommand(query);

                var counts = new Dictionary<string, int>();
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var type = reader.GetString(0);
                    var count = reader.GetInt32(1);
                    counts[type] = count;
                }

                return counts;
            }
            catch (Exception ex)
            {
                LogError("미마감 기록 통계 조회 실패", ex);
                throw;
            }
        }

        #endregion

        #region Update

        /// <summary>
        /// 학생부 기록 수정
        /// </summary>
        public async Task<bool> UpdateAsync(StudentSpecial special)
        {
            const string query = @"
                UPDATE StudentSpecial SET
                    StudentID = @StudentID,
                    Year = @Year,
                    Type = @Type,
                    Title = @Title,
                    Content = @Content,
                    Date = @Date,
                    TeacherID = @TeacherID,
                    CourseNo = @CourseNo,
                    SubjectName = @SubjectName,
                    IsActive = @IsActive,
                    Tag = @Tag
                WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                AddSpecialParameters(cmd, special);

                int affected = await cmd.ExecuteNonQueryAsync();
                bool success = affected > 0;

                if (success)
                    LogInfo($"학생부 기록 수정 완료: No={special.No}");
                else
                    LogWarning($"학생부 기록 수정 실패: No={special.No}");

                return success;
            }
            catch (Exception ex)
            {
                LogError($"학생부 기록 수정 실패: No={special.No}", ex);
                throw;
            }
        }

        /// <summary>
        /// 마감 상태 변경
        /// </summary>
        public async Task<bool> UpdateFinalizedStatusAsync(int no, bool isFinalized)
        {
            const string query = @"
                UPDATE StudentSpecial SET
                    IsActive = @IsActive
                WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@No", no);
                cmd.Parameters.AddWithValue("@IsActive", isFinalized ? 0 : 1);

                int affected = await cmd.ExecuteNonQueryAsync();
                return affected > 0;
            }
            catch (Exception ex)
            {
                LogError($"마감 상태 변경 실패: No={no}", ex);
                throw;
            }
        }

        #endregion

        #region Delete

        /// <summary>
        /// 학생부 기록 삭제
        /// </summary>
        public async Task<bool> DeleteAsync(int no)
        {
            const string query = "DELETE FROM StudentSpecial WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@No", no);

                int affected = await cmd.ExecuteNonQueryAsync();
                bool success = affected > 0;

                if (success)
                    LogInfo($"학생부 기록 삭제 완료: No={no}");
                else
                    LogWarning($"학생부 기록 삭제 실패: No={no}");

                return success;
            }
            catch (Exception ex)
            {
                LogError($"학생부 기록 삭제 실패: No={no}", ex);
                throw;
            }
        }

        #endregion

        #region Helper Methods

        private void AddSpecialParameters(SqliteCommand cmd, StudentSpecial special)
        {
            cmd.Parameters.AddWithValue("@No", special.No);
            cmd.Parameters.AddWithValue("@StudentID", special.StudentID ?? string.Empty);
            cmd.Parameters.AddWithValue("@Year", special.Year);
            cmd.Parameters.AddWithValue("@Type", special.Type ?? string.Empty);
            cmd.Parameters.AddWithValue("@Title", special.Title ?? string.Empty);
            cmd.Parameters.AddWithValue("@Content", special.Content ?? string.Empty);
            cmd.Parameters.AddWithValue("@Date", special.Date ?? string.Empty);
            cmd.Parameters.AddWithValue("@TeacherID", special.TeacherID ?? string.Empty);
            cmd.Parameters.AddWithValue("@CourseNo", special.CourseNo > 0 ? special.CourseNo : DBNull.Value);
            cmd.Parameters.AddWithValue("@SubjectName", string.IsNullOrEmpty(special.SubjectName) ? DBNull.Value : special.SubjectName);
            cmd.Parameters.AddWithValue("@IsActive", special.IsFinalized ? 0 : 1);
            cmd.Parameters.AddWithValue("@Tag", special.Tag ?? string.Empty);
        }

        private StudentSpecial MapStudentSpecial(SqliteDataReader reader)
        {
            return new StudentSpecial
            {
                No = reader.GetInt32(reader.GetOrdinal("No")),
                StudentID = reader.GetString(reader.GetOrdinal("StudentID")),
                Year = reader.GetInt32(reader.GetOrdinal("Year")),
                Type = reader.GetString(reader.GetOrdinal("Type")),
                Title = reader.GetString(reader.GetOrdinal("Title")),
                Content = reader.GetString(reader.GetOrdinal("Content")),
                Date = reader.GetString(reader.GetOrdinal("Date")),
                TeacherID = reader.GetString(reader.GetOrdinal("TeacherID")),
                CourseNo = reader.IsDBNull(reader.GetOrdinal("CourseNo")) ? 0 : reader.GetInt32(reader.GetOrdinal("CourseNo")),
                SubjectName = reader.IsDBNull(reader.GetOrdinal("SubjectName")) ? string.Empty : reader.GetString(reader.GetOrdinal("SubjectName")),
                IsFinalized = reader.GetInt32(reader.GetOrdinal("IsActive")) == 0,
                Tag = reader.GetString(reader.GetOrdinal("Tag"))
            };
        }

        #endregion
    }
}

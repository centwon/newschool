using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Models;

namespace NewSchool.Repositories
{
    /// <summary>
    /// Teacher Repository
    /// 교사 정보 (NEIS 표준) 관리
    /// </summary>
    public class TeacherRepository : BaseRepository
    {
        public TeacherRepository(string dbPath) : base(dbPath) { }

        #region Create

        /// <summary>
        /// 교사 생성
        /// </summary>
        public async Task<int> CreateAsync(Teacher teacher)
        {
            const string query = @"
                INSERT INTO Teacher (
                    TeacherID, LoginID, Name, Status, Position, Subject,
                    Phone, Email, BirthDate, HireDate, Photo, Memo,
                    CreatedAt, UpdatedAt, LastLoginAt, IsDeleted
                ) VALUES (
                    @TeacherID, @LoginID, @Name, @Status, @Position, @Subject,
                    @Phone, @Email, @BirthDate, @HireDate, @Photo, @Memo,
                    @CreatedAt, @UpdatedAt, @LastLoginAt, @IsDeleted
                );
                SELECT last_insert_rowid();";

            try
            {
                using var cmd = CreateCommand(query);
                AddTeacherParameters(cmd, teacher);

                var result = await cmd.ExecuteScalarAsync();
                teacher.No = Convert.ToInt32(result);

                LogInfo($"교사 생성 완료: No={teacher.No}");
                return teacher.No;
            }
            catch (Exception ex)
            {
                LogError($"교사 생성 실패: TeacherID={teacher.TeacherID}", ex);
                throw;
            }
        }

        #endregion

        #region Read

        /// <summary>
        /// No로 교사 조회
        /// </summary>
        public async Task<Teacher?> GetByNoAsync(int no)
        {
            const string query = "SELECT * FROM Teacher WHERE No = @No AND IsDeleted = 0";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@No", no);

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return MapTeacher(reader);
                }

                LogWarning($"교사를 찾을 수 없음: No={no}");
                return null;
            }
            catch (Exception ex)
            {
                LogError($"교사 조회 실패: No={no}", ex);
                throw;
            }
        }

        /// <summary>
        /// TeacherID로 교사 조회
        /// </summary>
        public async Task<Teacher?> GetByTeacherIdAsync(string teacherId)
        {
            const string query = "SELECT * FROM Teacher WHERE TeacherID = @TeacherID AND IsDeleted = 0";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@TeacherID", teacherId);

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return MapTeacher(reader);
                }

                LogWarning($"교사를 찾을 수 없음: TeacherID={teacherId}");
                return null;
            }
            catch (Exception ex)
            {
                LogError($"교사 조회 실패: TeacherID={teacherId}", ex);
                throw;
            }
        }

        /// <summary>
        /// LoginID로 교사 조회 (로그인 시 사용)
        /// </summary>
        public async Task<Teacher?> GetByLoginIdAsync(string loginId)
        {
            const string query = "SELECT * FROM Teacher WHERE LoginID = @LoginID AND IsDeleted = 0";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@LoginID", loginId);

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return MapTeacher(reader);
                }

                LogWarning($"교사를 찾을 수 없음: LoginID={loginId}");
                return null;
            }
            catch (Exception ex)
            {
                LogError($"교사 조회 실패: LoginID={loginId}", ex);
                throw;
            }
        }

        /// <summary>
        /// 모든 활성 교사 조회
        /// </summary>
        public async Task<List<Teacher>> GetAllActiveAsync()
        {
            const string query = @"
                SELECT * FROM Teacher 
                WHERE Status = '재직' AND IsDeleted = 0 
                ORDER BY Name";

            var teachers = new List<Teacher>();

            try
            {
                using var cmd = CreateCommand(query);
                using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    teachers.Add(MapTeacher(reader));
                }

                LogInfo($"활성 교사 목록 조회 완료: {teachers.Count}명");
                return teachers;
            }
            catch (Exception ex)
            {
                LogError("활성 교사 목록 조회 실패", ex);
                throw;
            }
        }

        /// <summary>
        /// 상태별 교사 조회
        /// </summary>
        public async Task<List<Teacher>> GetByStatusAsync(string status)
        {
            const string query = @"
                SELECT * FROM Teacher 
                WHERE Status = @Status AND IsDeleted = 0 
                ORDER BY Name";

            var teachers = new List<Teacher>();

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@Status", status);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    teachers.Add(MapTeacher(reader));
                }

                LogInfo($"상태별 교사 조회 완료: Status={status}, Count={teachers.Count}");
                return teachers;
            }
            catch (Exception ex)
            {
                LogError($"상태별 교사 조회 실패: Status={status}", ex);
                throw;
            }
        }

        /// <summary>
        /// 과목별 교사 조회
        /// </summary>
        public async Task<List<Teacher>> GetBySubjectAsync(string subject)
        {
            const string query = @"
                SELECT * FROM Teacher 
                WHERE Subject LIKE @Subject 
                  AND Status = '재직' 
                  AND IsDeleted = 0 
                ORDER BY Name";

            var teachers = new List<Teacher>();

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@Subject", $"%{subject}%");

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    teachers.Add(MapTeacher(reader));
                }

                LogInfo($"과목별 교사 조회 완료: Subject={subject}, Count={teachers.Count}");
                return teachers;
            }
            catch (Exception ex)
            {
                LogError($"과목별 교사 조회 실패: Subject={subject}", ex);
                throw;
            }
        }

        /// <summary>
        /// 교사 검색 (이름, 과목)
        /// </summary>
        public async Task<List<Teacher>> SearchAsync(string keyword)
        {
            const string query = @"
                SELECT * FROM Teacher 
                WHERE (Name LIKE @Keyword OR Subject LIKE @Keyword OR Phone LIKE @Keyword)
                  AND IsDeleted = 0 
                ORDER BY Name";

            var teachers = new List<Teacher>();

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@Keyword", $"%{keyword}%");

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    teachers.Add(MapTeacher(reader));
                }

                LogInfo($"교사 검색 완료: '{keyword}' - {teachers.Count}명");
                return teachers;
            }
            catch (Exception ex)
            {
                LogError($"교사 검색 실패: {keyword}", ex);
                throw;
            }
        }
        /// <summary>
        /// 여러 교사 ID로 일괄 조회 (시간표용)
        /// </summary>
        public async Task<List<Teacher>> GetByIdsAsync(List<string> teacherIds)
        {
            if (teacherIds == null || teacherIds.Count == 0)
                return new List<Teacher>();

            var placeholders = string.Join(",", teacherIds.Select((_, i) => $"@id{i}"));
            var query = $@"
        SELECT * FROM Teacher 
        WHERE TeacherID IN ({placeholders}) 
          AND IsDeleted = 0
        ORDER BY Name";

            var teachers = new List<Teacher>();

            try
            {
                using var cmd = CreateCommand(query);
                for (int i = 0; i < teacherIds.Count; i++)
                {
                    cmd.Parameters.AddWithValue($"@id{i}", teacherIds[i]);
                }

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    teachers.Add(MapTeacher(reader));
                }

                LogInfo($"교사 일괄 조회 완료: {teachers.Count}명");
                return teachers;
            }
            catch (Exception ex)
            {
                LogError("교사 일괄 조회 실패", ex);
                throw;
            }
        }
        #endregion

        #region Update

        /// <summary>
        /// 교사 정보 수정
        /// </summary>
        public async Task<bool> UpdateAsync(Teacher teacher)
        {
            const string query = @"
                UPDATE Teacher SET
                    TeacherID = @TeacherID,
                    LoginID = @LoginID,
                    Name = @Name,
                    Status = @Status,
                    Position = @Position,
                    Subject = @Subject,
                    Phone = @Phone,
                    Email = @Email,
                    BirthDate = @BirthDate,
                    HireDate = @HireDate,
                    Photo = @Photo,
                    Memo = @Memo,
                    UpdatedAt = @UpdatedAt
                WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@No", teacher.No);
                AddTeacherParameters(cmd, teacher);

                int rowsAffected = await cmd.ExecuteNonQueryAsync();
                bool success = rowsAffected > 0;

                if (success)
                {
                    LogInfo($"교사 수정 완료: No={teacher.No}");
                }
                else
                {
                    LogWarning($"교사 수정 실패 (존재하지 않음): No={teacher.No}");
                }

                return success;
            }
            catch (Exception ex)
            {
                LogError($"교사 수정 실패: No={teacher.No}", ex);
                throw;
            }
        }

        /// <summary>
        /// 교사 상태 변경 (재직/휴직/퇴직)
        /// </summary>
        public async Task<bool> UpdateStatusAsync(int no, string status)
        {
            const string query = @"
                UPDATE Teacher 
                SET Status = @Status, UpdatedAt = @UpdatedAt 
                WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@No", no);
                cmd.Parameters.AddWithValue("@Status", status);
                cmd.Parameters.AddWithValue("@UpdatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                int rowsAffected = await cmd.ExecuteNonQueryAsync();
                LogInfo($"교사 상태 변경: No={no}, Status={status}");
                return rowsAffected > 0;
            }
            catch (Exception ex)
            {
                LogError($"교사 상태 변경 실패: No={no}", ex);
                throw;
            }
        }

        /// <summary>
        /// 마지막 로그인 시간 업데이트
        /// </summary>
        public async Task<bool> UpdateLastLoginAsync(int no)
        {
            const string query = @"
                UPDATE Teacher 
                SET LastLoginAt = @LastLoginAt 
                WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@No", no);
                cmd.Parameters.AddWithValue("@LastLoginAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                int rowsAffected = await cmd.ExecuteNonQueryAsync();
                return rowsAffected > 0;
            }
            catch (Exception ex)
            {
                LogError($"마지막 로그인 시간 업데이트 실패: No={no}", ex);
                throw;
            }
        }

        #endregion

        #region Delete

        /// <summary>
        /// 교사 논리 삭제
        /// </summary>
        public async Task<bool> DeleteAsync(int no)
        {
            const string query = @"
                UPDATE Teacher 
                SET IsDeleted = 1, UpdatedAt = @UpdatedAt 
                WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@No", no);
                cmd.Parameters.AddWithValue("@UpdatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                int rowsAffected = await cmd.ExecuteNonQueryAsync();
                bool success = rowsAffected > 0;

                if (success)
                {
                    LogInfo($"교사 논리 삭제 완료: No={no}");
                }
                else
                {
                    LogWarning($"교사 삭제 실패 (존재하지 않음): No={no}");
                }

                return success;
            }
            catch (Exception ex)
            {
                LogError($"교사 삭제 실패: No={no}", ex);
                throw;
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Teacher 파라미터 추가
        /// </summary>
        private void AddTeacherParameters(SqliteCommand cmd, Teacher teacher)
        {
            cmd.Parameters.AddWithValue("@TeacherID", teacher.TeacherID);
            cmd.Parameters.AddWithValue("@LoginID", teacher.LoginID);
            cmd.Parameters.AddWithValue("@Name", teacher.Name);
            cmd.Parameters.AddWithValue("@Status", teacher.Status);
            cmd.Parameters.AddWithValue("@Position", teacher.Position ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Subject", teacher.Subject ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Phone", teacher.Phone ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Email", teacher.Email ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@BirthDate", teacher.BirthDate ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@HireDate", teacher.HireDate ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Photo", teacher.Photo ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Memo", teacher.Memo ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@CreatedAt", teacher.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@UpdatedAt", teacher.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@LastLoginAt",
                teacher.LastLoginAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@IsDeleted", teacher.IsDeleted ? 1 : 0);
        }

        /// <summary>
        /// SqliteDataReader를 Teacher로 매핑
        /// </summary>
        private Teacher MapTeacher(SqliteDataReader reader)
        {
            return new Teacher
            {
                No = reader.GetInt32(reader.GetOrdinal("No")),
                TeacherID = reader.GetString(reader.GetOrdinal("TeacherID")),
                LoginID = reader.GetString(reader.GetOrdinal("LoginID")),
                Name = reader.GetString(reader.GetOrdinal("Name")),
                Status = reader.GetString(reader.GetOrdinal("Status")),
                Position = GetStringOrEmpty(reader, "Position"),
                Subject = GetStringOrEmpty(reader, "Subject"),
                Phone = GetStringOrEmpty(reader, "Phone"),
                Email = GetStringOrEmpty(reader, "Email"),
                BirthDate = GetStringOrEmpty(reader, "BirthDate"),
                HireDate = GetStringOrEmpty(reader, "HireDate"),
                Photo = GetStringOrEmpty(reader, "Photo"),
                Memo = GetStringOrEmpty(reader, "Memo"),
                CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
                UpdatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("UpdatedAt"))),
                LastLoginAt = reader.IsDBNull(reader.GetOrdinal("LastLoginAt"))
                    ? null
                    : DateTime.Parse(reader.GetString(reader.GetOrdinal("LastLoginAt"))),
                IsDeleted = reader.GetInt32(reader.GetOrdinal("IsDeleted")) == 1
            };
        }

        private string GetStringOrEmpty(SqliteDataReader reader, string columnName)
        {
            int ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
        }

        #endregion
    }
}

using System;
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
                var cache = new ReaderColumnCache();
                cache.Initialize(reader);   // 컬럼 인덱스를 행마다 다시 찾지 않도록 한 번만
                if (await reader.ReadAsync())
                {
                    return MapTeacher(reader, cache);
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
                var cache = new ReaderColumnCache();
                cache.Initialize(reader);   // 컬럼 인덱스를 행마다 다시 찾지 않도록 한 번만
                if (await reader.ReadAsync())
                {
                    return MapTeacher(reader, cache);
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
        private Teacher MapTeacher(SqliteDataReader reader, ReaderColumnCache cache)
        {
            return new Teacher
            {
                No = reader.GetInt32(cache.GetOrdinal("No")),
                TeacherID = reader.GetString(cache.GetOrdinal("TeacherID")),
                LoginID = reader.GetString(cache.GetOrdinal("LoginID")),
                Name = reader.GetString(cache.GetOrdinal("Name")),
                Status = reader.GetString(cache.GetOrdinal("Status")),
                Position = GetStringOrEmpty(reader, cache, "Position"),
                Subject = GetStringOrEmpty(reader, cache, "Subject"),
                Phone = GetStringOrEmpty(reader, cache, "Phone"),
                Email = GetStringOrEmpty(reader, cache, "Email"),
                BirthDate = GetStringOrEmpty(reader, cache, "BirthDate"),
                HireDate = GetStringOrEmpty(reader, cache, "HireDate"),
                Photo = GetStringOrEmpty(reader, cache, "Photo"),
                Memo = GetStringOrEmpty(reader, cache, "Memo"),
                CreatedAt = DateTime.Parse(reader.GetString(cache.GetOrdinal("CreatedAt"))),
                UpdatedAt = DateTime.Parse(reader.GetString(cache.GetOrdinal("UpdatedAt"))),
                LastLoginAt = reader.IsDBNull(cache.GetOrdinal("LastLoginAt"))
                    ? null
                    : DateTime.Parse(reader.GetString(cache.GetOrdinal("LastLoginAt"))),
                IsDeleted = reader.GetInt32(cache.GetOrdinal("IsDeleted")) == 1
            };
        }

        private string GetStringOrEmpty(SqliteDataReader reader, ReaderColumnCache cache, string columnName)
        {
            int ordinal = cache.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
        }

        #endregion
    }
}
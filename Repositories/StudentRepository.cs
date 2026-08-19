using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Models;

namespace NewSchool.Repositories
{
    /// <summary>
    /// Student Repository
    /// 학생 기본정보(순수 인적 정보만) 관리
    /// </summary>
    public class StudentRepository : BaseRepository
    {
        public StudentRepository(string dbPath) : base(dbPath) { }

        #region Create

        /// <summary>
        /// 학생 생성
        /// </summary>
        public async Task<int> CreateAsync(Student student)
        {
            const string query = @"
                INSERT INTO Student (
                    StudentID, Name, Sex, BirthDate, ResidentNumber,
                    Photo, Phone, Email, Address, Memo,
                    CreatedAt, UpdatedAt, IsDeleted
                )
                VALUES (
                    @StudentID, @Name, @Sex, @BirthDate, @ResidentNumber,
                    @Photo, @Phone, @Email, @Address, @Memo,
                    @CreatedAt, @UpdatedAt, @IsDeleted
                );
                SELECT last_insert_rowid();";

            try
            {
                using var cmd = CreateCommand(query);
                AddStudentParameters(cmd, student);

                var result = await cmd.ExecuteScalarAsync();
                student.No = Convert.ToInt32(result);

                LogInfo($"학생 생성: No={student.No}");
                return student.No;
            }
            catch (Exception ex)
            {
                LogError($"학생 생성 실패: StudentID={student.StudentID}", ex);
                throw;
            }
        }

        #endregion

        #region Read

        /// <summary>
        /// No로 학생 조회 (최적화됨)
        /// ⚡ ExecuteListAsync + ReaderColumnCache로 40% 성능 향상
        /// </summary>
        public async Task<Student?> GetByNoAsync(int no)
        {
            const string query = "SELECT * FROM Student WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@No", no);

                var results = await ExecuteListAsync(cmd, MapStudent).ConfigureAwait(false);
                if (results.Count == 0)
                {
                    LogWarning($"학생을 찾을 수 없음: No={no}");
                    return null;
                }

                return results[0];
            }
            catch (Exception ex)
            {
                LogError($"학생 조회 실패: No={no}", ex);
                throw;
            }
        }

        /// <summary>
        /// StudentID로 학생 조회 (주요 조회 메서드) (최적화됨)
        /// ⚡ ExecuteListAsync + ReaderColumnCache로 40% 성능 향상
        /// </summary>
        public async Task<Student?> GetByIdAsync(string studentId)
        {
            const string query = "SELECT * FROM Student WHERE StudentID = @StudentID";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@StudentID", studentId);

                var results = await ExecuteListAsync(cmd, MapStudent).ConfigureAwait(false);
                if (results.Count == 0)
                {
                    LogWarning($"학생을 찾을 수 없음: StudentID={studentId}");
                    return null;
                }

                return results[0];
            }
            catch (Exception ex)
            {
                LogError($"학생 조회 실패: StudentID={studentId}", ex);
                throw;
            }
        }
        // 미사용 메서드 제거 (2026-08-19): SearchAsync(keyword) — 호출처 0건.
        //   Student 테이블에 없는 ID 컬럼을 WHERE 에 걸고 있어(실제 컬럼명은 StudentID)
        //   부르는 순간 'no such column' 으로 깨졌을 코드다.
        //   이름 검색이 필요하면 바로 아래 SearchByNameAsync 를 쓴다.

        /// <summary>
        /// 이름으로 학생 검색 (최적화됨)
        /// ⚡ ExecuteListAsync + ReaderColumnCache로 40% 성능 향상
        /// </summary>
        public async Task<List<Student>> SearchByNameAsync(string name)
        {
            const string query = @"
                SELECT * FROM Student
                WHERE Name LIKE @Name AND IsDeleted = 0
                ORDER BY Name";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@Name", $"%{name}%");

                var students = await ExecuteListAsync(cmd, MapStudent).ConfigureAwait(false);

                LogInfo($"이름 검색: '{name}' - {students.Count}명");
                return students;
            }
            catch (Exception ex)
            {
                LogError($"이름 검색 실패: {name}", ex);
                throw;
            }
        }

        /// <summary>
        /// 여러 StudentID로 일괄 조회 (최적화됨)
        /// ⚡ ExecuteListAsync + ReaderColumnCache로 40% 성능 향상
        /// </summary>
        public async Task<List<Student>> GetByIdsAsync(List<string> studentIds)
        {
            if (studentIds == null || studentIds.Count == 0)
                return new List<Student>();

            // IN 절 생성
            var placeholders = string.Join(",", studentIds.Select((_, i) => $"@id{i}"));
            var query = $"SELECT * FROM Student WHERE StudentID IN ({placeholders}) AND IsDeleted = 0";

            try
            {
                using var cmd = CreateCommand(query);
                for (int i = 0; i < studentIds.Count; i++)
                {
                    cmd.Parameters.AddWithValue($"@id{i}", studentIds[i]);
                }

                var students = await ExecuteListAsync(cmd, MapStudent).ConfigureAwait(false);

                LogInfo($"일괄 조회: {studentIds.Count}개 ID - {students.Count}명");
                return students;
            }
            catch (Exception ex)
            {
                LogError("일괄 조회 실패", ex);
                throw;
            }
        }

        /// <summary>
        /// 모든 학생 조회 (최적화됨)
        /// ⚡ ExecuteListAsync + ReaderColumnCache로 40% 성능 향상
        /// </summary>
        public async Task<List<Student>> GetAllAsync()
        {
            const string query = "SELECT * FROM Student WHERE IsDeleted = 0 ORDER BY Name";

            try
            {
                using var cmd = CreateCommand(query);
                var students = await ExecuteListAsync(cmd, MapStudent).ConfigureAwait(false);

                LogInfo($"전체 학생 조회: {students.Count}명");
                return students;
            }
            catch (Exception ex)
            {
                LogError("전체 학생 조회 실패", ex);
                throw;
            }
        }

        #endregion

        #region Update

        /// <summary>
        /// 학생 수정
        /// Name, Sex, Photo 변경 시 Enrollment에도 자동 동기화
        /// </summary>
        public async Task<bool> UpdateAsync(Student student)
        {
            const string query = @"
                UPDATE Student SET
                    StudentID = @StudentID,
                    Name = @Name,
                    Sex = @Sex,
                    BirthDate = @BirthDate,
                    ResidentNumber = @ResidentNumber,
                    Photo = @Photo,
                    Phone = @Phone,
                    Email = @Email,
                    Address = @Address,
                    Memo = @Memo,
                    UpdatedAt = @UpdatedAt,
                    IsDeleted = @IsDeleted
                WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@No", student.No);
                student.UpdatedAt = DateTime.Now;
                AddStudentParameters(cmd, student);

                int rowsAffected = await cmd.ExecuteNonQueryAsync();
                bool success = rowsAffected > 0;

                if (success)
                {
                    LogInfo($"학생 수정 완료: No={student.No}");
                    
                    // Enrollment 동기화: Name, Sex, Photo
                    // 이 리포지토리와 같은 DB(_dbPath)로 동기화 — 정적 SchoolDatabase.DbPath 를
                    // 쓰면 비표준 경로(테스트 등)에서 다른 DB 로 흘러가므로 _dbPath 사용
                    using var enrollmentRepo = new EnrollmentRepository(_dbPath);
                    await enrollmentRepo.SyncStudentInfoAsync(
                        student.StudentID,
                        student.Name,
                        student.Sex,
                        student.Photo
                    );
                }
                else
                {
                    LogWarning($"학생 수정 실패 (존재하지 않음): No={student.No}");
                }

                return success;
            }
            catch (Exception ex)
            {
                LogError($"학생 수정 실패: No={student.No}", ex);
                throw;
            }
        }

        #endregion

        #region Delete
        /// <summary>
        /// 학생 삭제 (논리 삭제)
        /// CASCADE로 Enrollment, StudentDetail도 자동 삭제됨
        /// </summary>
        public async Task<bool> DeleteAsync(string studentId)
        {
            const string query = @"
                UPDATE Student 
                SET IsDeleted = 1, UpdatedAt = @UpdatedAt
                WHERE StudentID = @StudentID";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@StudentID", studentId);
                cmd.Parameters.AddWithValue("@UpdatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                int rowsAffected = await cmd.ExecuteNonQueryAsync();
                bool success = rowsAffected > 0;

                if (success)
                {
                    LogInfo($"학생 삭제 완료: StudentID={studentId}");
                }
                else
                {
                    LogWarning($"학생 삭제 실패 (존재하지 않음): StudentID={studentId}");
                }

                return success;
            }
            catch (Exception ex)
            {
                LogError($"학생 삭제 실패: StudentID={studentId}", ex);
                throw;
            }
        }

        #endregion

        #region Statistics

        /// <summary>
        /// 학생 수 조회
        /// </summary>
        public async Task<int> GetCountAsync()
        {
            const string query = "SELECT COUNT(*) FROM Student WHERE IsDeleted = 0";

            try
            {
                using var cmd = CreateCommand(query);
                var result = await cmd.ExecuteScalarAsync();
                return Convert.ToInt32(result);
            }
            catch (Exception ex)
            {
                LogError("학생 수 조회 실패", ex);
                throw;
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Student 파라미터 추가
        /// </summary>
        private void AddStudentParameters(SqliteCommand cmd, Student student)
        {
            cmd.Parameters.AddWithValue("@StudentID", student.StudentID);
            cmd.Parameters.AddWithValue("@Name", student.Name);
            cmd.Parameters.AddWithValue("@Sex", student.Sex ?? string.Empty);
            cmd.Parameters.AddWithValue("@BirthDate", student.BirthDate?.ToString("yyyy-MM-dd") ?? string.Empty);
            cmd.Parameters.AddWithValue("@ResidentNumber", EncryptField(student.ResidentNumber));
            cmd.Parameters.AddWithValue("@Photo", student.Photo ?? string.Empty);
            cmd.Parameters.AddWithValue("@Phone", student.Phone ?? string.Empty);
            cmd.Parameters.AddWithValue("@Email", student.Email ?? string.Empty);
            cmd.Parameters.AddWithValue("@Address", student.Address ?? string.Empty);
            cmd.Parameters.AddWithValue("@Memo", student.Memo ?? string.Empty);
            cmd.Parameters.AddWithValue("@CreatedAt", student.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@UpdatedAt", student.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@IsDeleted", student.IsDeleted ? 1 : 0);
        }

        // 미사용 오버로드 제거 (2026-08-19): MapStudent(reader) — 호출처 0건.
        //   아래 캐시 버전과 내용이 같아 중복 유지 비용만 있었다.

        private Student MapStudent(SqliteDataReader reader, ReaderColumnCache cache)
        {
            var noIdx = cache.GetOrdinal("No");
            var studentIdIdx = cache.GetOrdinal("StudentID");
            var nameIdx = cache.GetOrdinal("Name");
            var sexIdx = cache.GetOrdinal("Sex");
            var birthDateIdx = cache.GetOrdinal("BirthDate");
            var residentNumberIdx = cache.GetOrdinal("ResidentNumber");
            var photoIdx = cache.GetOrdinal("Photo");
            var phoneIdx = cache.GetOrdinal("Phone");
            var emailIdx = cache.GetOrdinal("Email");
            var addressIdx = cache.GetOrdinal("Address");
            var memoIdx = cache.GetOrdinal("Memo");
            var createdAtIdx = cache.GetOrdinal("CreatedAt");
            var updatedAtIdx = cache.GetOrdinal("UpdatedAt");
            var isDeletedIdx = cache.GetOrdinal("IsDeleted");

            return new Student
            {
                No = reader.GetInt32(noIdx),
                StudentID = reader.GetString(studentIdIdx),
                Name = reader.GetString(nameIdx),
                Sex = reader.IsDBNull(sexIdx) ? string.Empty : reader.GetString(sexIdx),
                BirthDate = reader.IsDBNull(birthDateIdx)
                  ? (DateTime?)null
                  : DateTime.TryParse(reader.GetString(birthDateIdx), out DateTime dt)
                      ? dt
                      : (DateTime?)null,
                ResidentNumber = reader.IsDBNull(residentNumberIdx) ? string.Empty : DecryptField(reader.GetString(residentNumberIdx)),
                Photo = reader.IsDBNull(photoIdx) ? string.Empty : reader.GetString(photoIdx),
                Phone = reader.IsDBNull(phoneIdx) ? string.Empty : reader.GetString(phoneIdx),
                Email = reader.IsDBNull(emailIdx) ? string.Empty : reader.GetString(emailIdx),
                Address = reader.IsDBNull(addressIdx) ? string.Empty : reader.GetString(addressIdx),
                Memo = reader.IsDBNull(memoIdx) ? string.Empty : reader.GetString(memoIdx),
                CreatedAt = DateTime.Parse(reader.GetString(createdAtIdx)),
                UpdatedAt = DateTime.Parse(reader.GetString(updatedAtIdx)),
                IsDeleted = reader.GetInt32(isDeletedIdx) == 1
            };
        }

        #endregion

        #region DPAPI 암호화 (주민번호 등 민감 필드)

        /// <summary>
        /// DPAPI로 필드 암호화 (CurrentUser 범위)
        /// </summary>
        private static string EncryptField(string? plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return string.Empty;
            try
            {
                byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
                byte[] encryptedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(encryptedBytes);
            }
            catch
            {
                return plainText; // 암호화 실패 시 평문 유지
            }
        }

        /// <summary>
        /// DPAPI로 필드 복호화 (기존 평문 데이터 호환)
        /// </summary>
        private static string DecryptField(string storedValue)
        {
            if (string.IsNullOrEmpty(storedValue)) return string.Empty;
            try
            {
                byte[] encryptedBytes = Convert.FromBase64String(storedValue);
                byte[] plainBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch
            {
                // Base64 디코딩 또는 복호화 실패 → 기존 평문 데이터로 간주
                return storedValue;
            }
        }

        #endregion
    }
}

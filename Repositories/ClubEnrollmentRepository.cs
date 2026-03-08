using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Models;

namespace NewSchool.Repositories
{
    /// <summary>
    /// ClubEnrollment Repository
    /// 동아리 배정 정보 관리
    /// </summary>
    public class ClubEnrollmentRepository : BaseRepository
    {
        public ClubEnrollmentRepository(string dbPath) : base(dbPath) { }

        #region Create

        /// <summary>
        /// 동아리 배정 생성
        /// </summary>
        public async Task<int> CreateAsync(ClubEnrollment enrollment)
        {
            const string query = @"
                INSERT INTO ClubEnrollment (
                    StudentID, ClubNo, Status, Remark, CreatedAt, UpdatedAt
                ) VALUES (
                    @StudentID, @ClubNo, @Status, @Remark, @CreatedAt, @UpdatedAt
                );
                SELECT last_insert_rowid();";

            try
            {
                using var cmd = CreateCommand(query);
                AddEnrollmentParameters(cmd, enrollment);

                var result = await cmd.ExecuteScalarAsync();
                enrollment.No = Convert.ToInt32(result);

                LogInfo($"동아리 배정 생성 완료: No={enrollment.No}, StudentID={enrollment.StudentID}");
                return enrollment.No;
            }
            catch (Exception ex)
            {
                LogError($"동아리 배정 생성 실패: StudentID={enrollment.StudentID}", ex);
                throw;
            }
        }

        /// <summary>
        /// 여러 학생 일괄 동아리 배정
        /// </summary>
        public async Task<int> BulkCreateAsync(List<ClubEnrollment> enrollments)
        {
            if (enrollments == null || enrollments.Count == 0)
                return 0;

            try
            {
                BeginTransaction();

                int count = 0;
                foreach (var enrollment in enrollments)
                {
                    await CreateAsync(enrollment);
                    count++;
                }

                Commit();
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
        /// No로 동아리 배정 조회
        /// </summary>
        public async Task<ClubEnrollment?> GetByIdAsync(int no)
        {
            const string query = "SELECT * FROM ClubEnrollment WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@No", no);

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return MapEnrollment(reader);
                }

                return null;
            }
            catch (Exception ex)
            {
                LogError($"동아리 배정 조회 실패: No={no}", ex);
                throw;
            }
        }

        /// <summary>
        /// 동아리별 학생 목록 조회
        /// </summary>
        public async Task<List<ClubEnrollment>> GetByClubAsync(int clubNo)
        {
            const string query = @"
                SELECT * FROM ClubEnrollment 
                WHERE ClubNo = @ClubNo
                ORDER BY StudentID";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@ClubNo", clubNo);

                var enrollments = new List<ClubEnrollment>();
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    enrollments.Add(MapEnrollment(reader));
                }

                return enrollments;
            }
            catch (Exception ex)
            {
                LogError($"동아리별 학생 조회 실패: ClubNo={clubNo}", ex);
                throw;
            }
        }

        /// <summary>
        /// 학생별 동아리 목록 조회
        /// </summary>
        public async Task<List<ClubEnrollment>> GetByStudentAsync(string studentId)
        {
            const string query = @"
                SELECT * FROM ClubEnrollment 
                WHERE StudentID = @StudentID
                ORDER BY ClubNo";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@StudentID", studentId);

                var enrollments = new List<ClubEnrollment>();
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    enrollments.Add(MapEnrollment(reader));
                }

                return enrollments;
            }
            catch (Exception ex)
            {
                LogError($"학생별 동아리 조회 실패: StudentID={studentId}", ex);
                throw;
            }
        }

        /// <summary>
        /// 학생의 특정 학년도 동아리 조회
        /// Club과 JOIN하여 조회
        /// </summary>
        public async Task<List<ClubEnrollment>> GetByStudentAndYearAsync(
            string studentId, int year)
        {
            const string query = @"
                SELECT ce.* FROM ClubEnrollment ce
                INNER JOIN Club c ON ce.ClubNo = c.No
                WHERE ce.StudentID = @StudentID
                  AND c.Year = @Year
                  AND c.IsDeleted = 0
                ORDER BY c.ClubName";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@StudentID", studentId);
                cmd.Parameters.AddWithValue("@Year", year);

                var enrollments = new List<ClubEnrollment>();
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    enrollments.Add(MapEnrollment(reader));
                }

                return enrollments;
            }
            catch (Exception ex)
            {
                LogError($"학생 동아리 조회 실패: StudentID={studentId}, Year={year}", ex);
                throw;
            }
        }

        /// <summary>
        /// 중복 동아리 배정 확인
        /// </summary>
        public async Task<bool> ExistsAsync(string studentId, int clubNo)
        {
            const string query = @"
                SELECT COUNT(*) FROM ClubEnrollment 
                WHERE StudentID = @StudentID AND ClubNo = @ClubNo";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@StudentID", studentId);
                cmd.Parameters.AddWithValue("@ClubNo", clubNo);

                var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                return count > 0;
            }
            catch (Exception ex)
            {
                LogError($"중복 동아리 확인 실패: StudentID={studentId}, ClubNo={clubNo}", ex);
                throw;
            }
        }

        #endregion

        #region Update

        /// <summary>
        /// 동아리 배정 정보 수정
        /// </summary>
        public async Task<bool> UpdateAsync(ClubEnrollment enrollment)
        {
            const string query = @"
                UPDATE ClubEnrollment SET
                    StudentID = @StudentID,
                    ClubNo = @ClubNo,
                    Status = @Status,
                    Remark = @Remark,
                    UpdatedAt = @UpdatedAt
                WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                AddEnrollmentParameters(cmd, enrollment);

                int affected = await cmd.ExecuteNonQueryAsync();
                bool success = affected > 0;

                if (success)
                    LogInfo($"동아리 배정 수정 완료: No={enrollment.No}");
                else
                    LogWarning($"동아리 배정 수정 실패: No={enrollment.No}");

                return success;
            }
            catch (Exception ex)
            {
                LogError($"동아리 배정 수정 실패: No={enrollment.No}", ex);
                throw;
            }
        }

        /// <summary>
        /// 동아리 활동 상태 변경
        /// </summary>
        public async Task<bool> UpdateStatusAsync(int no, string status)
        {
            const string query = @"
                UPDATE ClubEnrollment SET
                    Status = @Status,
                    UpdatedAt = @UpdatedAt
                WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@No", no);
                cmd.Parameters.AddWithValue("@Status", status);
                cmd.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);

                int affected = await cmd.ExecuteNonQueryAsync();
                return affected > 0;
            }
            catch (Exception ex)
            {
                LogError($"동아리 상태 변경 실패: No={no}", ex);
                throw;
            }
        }

        #endregion

        #region Delete

        /// <summary>
        /// 동아리 배정 취소 (삭제)
        /// </summary>
        public async Task<bool> DeleteAsync(int no)
        {
            const string query = "DELETE FROM ClubEnrollment WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@No", no);

                int affected = await cmd.ExecuteNonQueryAsync();
                bool success = affected > 0;

                if (success)
                    LogInfo($"동아리 배정 삭제 완료: No={no}");
                else
                    LogWarning($"동아리 배정 삭제 실패: No={no}");

                return success;
            }
            catch (Exception ex)
            {
                LogError($"동아리 배정 삭제 실패: No={no}", ex);
                throw;
            }
        }

        /// <summary>
        /// 동아리의 모든 배정 삭제
        /// </summary>
        public async Task<int> DeleteByClubAsync(int clubNo)
        {
            const string query = "DELETE FROM ClubEnrollment WHERE ClubNo = @ClubNo";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@ClubNo", clubNo);

                int affected = await cmd.ExecuteNonQueryAsync();

                LogInfo($"동아리 배정 일괄 삭제 완료: ClubNo={clubNo}, 삭제 건수={affected}");
                return affected;
            }
            catch (Exception ex)
            {
                LogError($"동아리 배정 일괄 삭제 실패: ClubNo={clubNo}", ex);
                throw;
            }
        }

        #endregion

        #region Helper Methods

        private void AddEnrollmentParameters(SqliteCommand cmd, ClubEnrollment enrollment)
        {
            cmd.Parameters.AddWithValue("@No", enrollment.No);
            cmd.Parameters.AddWithValue("@StudentID", enrollment.StudentID ?? string.Empty);
            cmd.Parameters.AddWithValue("@ClubNo", enrollment.ClubNo);
            cmd.Parameters.AddWithValue("@Status", enrollment.Status ?? ClubEnrollmentStatus.Active);
            cmd.Parameters.AddWithValue("@Remark", enrollment.Remark ?? string.Empty);
            cmd.Parameters.AddWithValue("@CreatedAt", enrollment.CreatedAt);
            cmd.Parameters.AddWithValue("@UpdatedAt", enrollment.UpdatedAt);
        }

        private ClubEnrollment MapEnrollment(SqliteDataReader reader)
        {
            return new ClubEnrollment
            {
                No = reader.GetInt32(reader.GetOrdinal("No")),
                StudentID = reader.GetString(reader.GetOrdinal("StudentID")),
                ClubNo = reader.GetInt32(reader.GetOrdinal("ClubNo")),
                Status = reader.GetString(reader.GetOrdinal("Status")),
                Remark = reader.IsDBNull(reader.GetOrdinal("Remark")) 
                    ? string.Empty 
                    : reader.GetString(reader.GetOrdinal("Remark")),
                CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
                UpdatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("UpdatedAt")))
            };
        }

        #endregion
    }
}

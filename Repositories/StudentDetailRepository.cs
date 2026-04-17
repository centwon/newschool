using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Models;

namespace NewSchool.Repositories
{
    /// <summary>
    /// StudentDetail Repository
    /// 학생 상세 정보 (보호자, 가족, 진로, 특기사항 등) 관리
    /// Student와 1:1 관계
    /// </summary>
    public class StudentDetailRepository : BaseRepository
    {
        public StudentDetailRepository(string dbPath) : base(dbPath) { }

        #region Create

        /// <summary>
        /// 학생 상세 정보 생성
        /// </summary>
        public async Task<int> CreateAsync(StudentDetail detail)
        {
            const string query = @"
                INSERT INTO StudentDetail (
                    StudentID, FatherName, FatherPhone, FatherJob,
                    MotherName, MotherPhone, MotherJob,
                    GuardianName, GuardianPhone, GuardianRelation,
                    FamilyInfo, Friends, Interests, Talents, CareerGoal,
                    HealthInfo, Allergies, SpecialNeeds, Memo,
                    CreatedAt, UpdatedAt
                ) VALUES (
                    @StudentID, @FatherName, @FatherPhone, @FatherJob,
                    @MotherName, @MotherPhone, @MotherJob,
                    @GuardianName, @GuardianPhone, @GuardianRelation,
                    @FamilyInfo, @Friends, @Interests, @Talents, @CareerGoal,
                    @HealthInfo, @Allergies, @SpecialNeeds, @Memo,
                    @CreatedAt, @UpdatedAt
                );
                SELECT last_insert_rowid();";

            try
            {
                using var cmd = CreateCommand(query);
                AddStudentDetailParameters(cmd, detail);

                var result = await cmd.ExecuteScalarAsync();
                detail.No = Convert.ToInt32(result);

                LogInfo($"학생 상세정보 생성 완료: No={detail.No}, StudentID={detail.StudentID}");
                return detail.No;
            }
            catch (Exception ex)
            {
                LogError($"학생 상세정보 생성 실패: StudentID={detail.StudentID}", ex);
                throw;
            }
        }

        #endregion

        #region Read

        /// <summary>
        /// StudentID로 상세 정보 조회
        /// </summary>
        public async Task<StudentDetail?> GetByStudentIdAsync(string studentId)
        {
            const string query = "SELECT * FROM StudentDetail WHERE StudentID = @StudentID";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@StudentID", studentId);

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return MapStudentDetail(reader);
                }

                LogWarning($"학생 상세정보를 찾을 수 없음: StudentID={studentId}");
                return null;
            }
            catch (Exception ex)
            {
                LogError($"학생 상세정보 조회 실패: StudentID={studentId}", ex);
                throw;
            }
        }

        /// <summary>
        /// No로 상세 정보 조회
        /// </summary>
        public async Task<StudentDetail?> GetByNoAsync(int no)
        {
            const string query = "SELECT * FROM StudentDetail WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@No", no);

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return MapStudentDetail(reader);
                }

                LogWarning($"학생 상세정보를 찾을 수 없음: No={no}");
                return null;
            }
            catch (Exception ex)
            {
                LogError($"학생 상세정보 조회 실패: No={no}", ex);
                throw;
            }
        }

        /// <summary>
        /// 여러 StudentID로 상세 정보 일괄 조회
        /// </summary>
        public async Task<List<StudentDetail>> GetByStudentIdsAsync(List<string> studentIds)
        {
            if (studentIds == null || studentIds.Count == 0)
                return [];

            var placeholders = string.Join(",", studentIds.Select((_, i) => $"@id{i}"));
            var query = $"SELECT * FROM StudentDetail WHERE StudentID IN ({placeholders})";

            try
            {
                using var cmd = CreateCommand(query);
                for (int i = 0; i < studentIds.Count; i++)
                {
                    cmd.Parameters.AddWithValue($"@id{i}", studentIds[i]);
                }

                var results = new List<StudentDetail>();
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    results.Add(MapStudentDetail(reader));
                }

                return results;
            }
            catch (Exception ex)
            {
                LogError($"학생 상세정보 일괄 조회 실패: {studentIds.Count}건", ex);
                throw;
            }
        }

        #endregion

        #region Update

        /// <summary>
        /// 학생 상세 정보 수정
        /// </summary>
        public async Task<bool> UpdateAsync(StudentDetail detail)
        {
            const string query = @"
                UPDATE StudentDetail SET
                    FatherName = @FatherName,
                    FatherPhone = @FatherPhone,
                    FatherJob = @FatherJob,
                    MotherName = @MotherName,
                    MotherPhone = @MotherPhone,
                    MotherJob = @MotherJob,
                    GuardianName = @GuardianName,
                    GuardianPhone = @GuardianPhone,
                    GuardianRelation = @GuardianRelation,
                    FamilyInfo = @FamilyInfo,
                    Friends = @Friends,
                    Interests = @Interests,
                    Talents = @Talents,
                    CareerGoal = @CareerGoal,
                    HealthInfo = @HealthInfo,
                    Allergies = @Allergies,
                    SpecialNeeds = @SpecialNeeds,
                    Memo = @Memo,
                    UpdatedAt = @UpdatedAt
                WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@No", detail.No);
                AddStudentDetailParameters(cmd, detail);

                int rowsAffected = await cmd.ExecuteNonQueryAsync();
                bool success = rowsAffected > 0;

                if (success)
                {
                    LogInfo($"학생 상세정보 수정 완료: No={detail.No}");
                }
                else
                {
                    LogWarning($"학생 상세정보 수정 실패 (존재하지 않음): No={detail.No}");
                }

                return success;
            }
            catch (Exception ex)
            {
                LogError($"학생 상세정보 수정 실패: No={detail.No}", ex);
                throw;
            }
        }

        #endregion

        #region Delete

        /// <summary>
        /// 학생 상세 정보 삭제
        /// </summary>
        public async Task<bool> DeleteAsync(int no)
        {
            const string query = "DELETE FROM StudentDetail WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@No", no);

                int rowsAffected = await cmd.ExecuteNonQueryAsync();
                bool success = rowsAffected > 0;

                if (success)
                {
                    LogInfo($"학생 상세정보 삭제 완료: No={no}");
                }
                else
                {
                    LogWarning($"학생 상세정보 삭제 실패 (존재하지 않음): No={no}");
                }

                return success;
            }
            catch (Exception ex)
            {
                LogError($"학생 상세정보 삭제 실패: No={no}", ex);
                throw;
            }
        }

        /// <summary>
        /// StudentID로 상세 정보 삭제
        /// </summary>
        public async Task<bool> DeleteByStudentIdAsync(string studentId)
        {
            const string query = "DELETE FROM StudentDetail WHERE StudentID = @StudentID";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@StudentID", studentId);

                int rowsAffected = await cmd.ExecuteNonQueryAsync();
                bool success = rowsAffected > 0;

                if (success)
                {
                    LogInfo($"학생 상세정보 삭제 완료: StudentID={studentId}");
                }

                return success;
            }
            catch (Exception ex)
            {
                LogError($"학생 상세정보 삭제 실패: StudentID={studentId}", ex);
                throw;
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// StudentDetail 파라미터 추가
        /// </summary>
        private void AddStudentDetailParameters(SqliteCommand cmd, StudentDetail detail)
        {
            cmd.Parameters.AddWithValue("@StudentID", detail.StudentID);
            cmd.Parameters.AddWithValue("@FatherName", detail.FatherName ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@FatherPhone", detail.FatherPhone ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@FatherJob", detail.FatherJob ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@MotherName", detail.MotherName ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@MotherPhone", detail.MotherPhone ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@MotherJob", detail.MotherJob ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@GuardianName", detail.GuardianName ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@GuardianPhone", detail.GuardianPhone ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@GuardianRelation", detail.GuardianRelation ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@FamilyInfo", detail.FamilyInfo ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Friends", detail.Friends ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Interests", detail.Interests ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Talents", detail.Talents ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@CareerGoal", detail.CareerGoal ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@HealthInfo", detail.HealthInfo ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Allergies", detail.Allergies ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@SpecialNeeds", detail.SpecialNeeds ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Memo", detail.Memo ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@CreatedAt", detail.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@UpdatedAt", detail.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        }

        /// <summary>
        /// SqliteDataReader를 StudentDetail로 매핑
        /// </summary>
        private StudentDetail MapStudentDetail(SqliteDataReader reader)
        {
            return new StudentDetail
            {
                No = reader.GetInt32(reader.GetOrdinal("No")),
                StudentID = reader.GetString(reader.GetOrdinal("StudentID")),
                FatherName = GetStringOrEmpty(reader, "FatherName"),
                FatherPhone = GetStringOrEmpty(reader, "FatherPhone"),
                FatherJob = GetStringOrEmpty(reader, "FatherJob"),
                MotherName = GetStringOrEmpty(reader, "MotherName"),
                MotherPhone = GetStringOrEmpty(reader, "MotherPhone"),
                MotherJob = GetStringOrEmpty(reader, "MotherJob"),
                GuardianName = GetStringOrEmpty(reader, "GuardianName"),
                GuardianPhone = GetStringOrEmpty(reader, "GuardianPhone"),
                GuardianRelation = GetStringOrEmpty(reader, "GuardianRelation"),
                FamilyInfo = GetStringOrEmpty(reader, "FamilyInfo"),
                Friends = GetStringOrEmpty(reader, "Friends"),
                Interests = GetStringOrEmpty(reader, "Interests"),
                Talents = GetStringOrEmpty(reader, "Talents"),
                CareerGoal = GetStringOrEmpty(reader, "CareerGoal"),
                HealthInfo = GetStringOrEmpty(reader, "HealthInfo"),
                Allergies = GetStringOrEmpty(reader, "Allergies"),
                SpecialNeeds = GetStringOrEmpty(reader, "SpecialNeeds"),
                Memo = GetStringOrEmpty(reader, "Memo"),
                CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
                UpdatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("UpdatedAt")))
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

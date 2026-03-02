using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Models;

namespace NewSchool.Repositories
{
    /// <summary>
    /// TeacherSchoolHistory Repository
    /// 교사 근무 이력 (학교별) 관리
    /// </summary>
    public class TeacherSchoolHistoryRepository : BaseRepository
    {
        public TeacherSchoolHistoryRepository(string dbPath) : base(dbPath) { }

        #region Create

        /// <summary>
        /// 교사 근무 이력 생성
        /// </summary>
        public async Task<int> CreateAsync(TeacherSchoolHistory history)
        {
            const string query = @"
                INSERT INTO TeacherSchoolHistory (
                    TeacherID, SchoolCode, StartDate, EndDate, IsCurrent,
                    Position, Role, Memo, CreatedAt, UpdatedAt
                ) VALUES (
                    @TeacherID, @SchoolCode, @StartDate, @EndDate, @IsCurrent,
                    @Position, @Role, @Memo, @CreatedAt, @UpdatedAt
                );
                SELECT last_insert_rowid();";

            try
            {
                using var cmd = CreateCommand(query);
                AddHistoryParameters(cmd, history);

                var result = await cmd.ExecuteScalarAsync();
                history.No = Convert.ToInt32(result);

                LogInfo($"교사 근무이력 생성 완료: No={history.No}, TeacherID={history.TeacherID}");
                return history.No;
            }
            catch (Exception ex)
            {
                LogError($"교사 근무이력 생성 실패: TeacherID={history.TeacherID}", ex);
                throw;
            }
        }

        #endregion

        #region Read

        /// <summary>
        /// No로 이력 조회
        /// </summary>
        public async Task<TeacherSchoolHistory?> GetByNoAsync(int no)
        {
            const string query = "SELECT * FROM TeacherSchoolHistory WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@No", no);

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return MapHistory(reader);
                }

                LogWarning($"교사 근무이력을 찾을 수 없음: No={no}");
                return null;
            }
            catch (Exception ex)
            {
                LogError($"교사 근무이력 조회 실패: No={no}", ex);
                throw;
            }
        }

        /// <summary>
        /// 교사의 모든 근무 이력 조회 (최신순)
        /// </summary>
        public async Task<List<TeacherSchoolHistory>> GetByTeacherIdAsync(string teacherId)
        {
            const string query = @"
                SELECT * FROM TeacherSchoolHistory 
                WHERE TeacherID = @TeacherID 
                ORDER BY StartDate DESC";

            var histories = new List<TeacherSchoolHistory>();

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@TeacherID", teacherId);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    histories.Add(MapHistory(reader));
                }

                LogInfo($"교사 근무이력 조회 완료: TeacherID={teacherId}, Count={histories.Count}");
                return histories;
            }
            catch (Exception ex)
            {
                LogError($"교사 근무이력 조회 실패: TeacherID={teacherId}", ex);
                throw;
            }
        }

        /// <summary>
        /// 교사의 현재 근무 이력 조회
        /// </summary>
        public async Task<TeacherSchoolHistory?> GetCurrentByTeacherIdAsync(string teacherId)
        {
            const string query = @"
                SELECT * FROM TeacherSchoolHistory 
                WHERE TeacherID = @TeacherID AND IsCurrent = 1 
                LIMIT 1";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@TeacherID", teacherId);

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return MapHistory(reader);
                }

                LogWarning($"현재 근무이력을 찾을 수 없음: TeacherID={teacherId}");
                return null;
            }
            catch (Exception ex)
            {
                LogError($"현재 근무이력 조회 실패: TeacherID={teacherId}", ex);
                throw;
            }
        }

        /// <summary>
        /// 특정 학교의 교사 근무 이력 조회 (현재 근무 중인 교사)
        /// </summary>
        public async Task<List<TeacherSchoolHistory>> GetCurrentBySchoolCodeAsync(string schoolCode)
        {
            const string query = @"
                SELECT * FROM TeacherSchoolHistory 
                WHERE SchoolCode = @SchoolCode AND IsCurrent = 1 
                ORDER BY StartDate DESC";

            var histories = new List<TeacherSchoolHistory>();

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@SchoolCode", schoolCode);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    histories.Add(MapHistory(reader));
                }

                LogInfo($"학교별 현재 근무이력 조회 완료: SchoolCode={schoolCode}, Count={histories.Count}");
                return histories;
            }
            catch (Exception ex)
            {
                LogError($"학교별 현재 근무이력 조회 실패: SchoolCode={schoolCode}", ex);
                throw;
            }
        }

        /// <summary>
        /// 특정 학교의 전체 교사 근무 이력 조회 (과거 포함)
        /// </summary>
        public async Task<List<TeacherSchoolHistory>> GetAllBySchoolCodeAsync(string schoolCode)
        {
            const string query = @"
                SELECT * FROM TeacherSchoolHistory 
                WHERE SchoolCode = @SchoolCode 
                ORDER BY StartDate DESC";

            var histories = new List<TeacherSchoolHistory>();

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@SchoolCode", schoolCode);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    histories.Add(MapHistory(reader));
                }

                LogInfo($"학교별 전체 근무이력 조회 완료: SchoolCode={schoolCode}, Count={histories.Count}");
                return histories;
            }
            catch (Exception ex)
            {
                LogError($"학교별 전체 근무이력 조회 실패: SchoolCode={schoolCode}", ex);
                throw;
            }
        }

        #endregion

        #region Update

        /// <summary>
        /// 교사 근무 이력 수정
        /// </summary>
        public async Task<bool> UpdateAsync(TeacherSchoolHistory history)
        {
            const string query = @"
                UPDATE TeacherSchoolHistory SET
                    TeacherID = @TeacherID,
                    SchoolCode = @SchoolCode,
                    StartDate = @StartDate,
                    EndDate = @EndDate,
                    IsCurrent = @IsCurrent,
                    Position = @Position,
                    Role = @Role,
                    Memo = @Memo,
                    UpdatedAt = @UpdatedAt
                WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@No", history.No);
                AddHistoryParameters(cmd, history);

                int rowsAffected = await cmd.ExecuteNonQueryAsync();
                bool success = rowsAffected > 0;

                if (success)
                {
                    LogInfo($"교사 근무이력 수정 완료: No={history.No}");
                }
                else
                {
                    LogWarning($"교사 근무이력 수정 실패 (존재하지 않음): No={history.No}");
                }

                return success;
            }
            catch (Exception ex)
            {
                LogError($"교사 근무이력 수정 실패: No={history.No}", ex);
                throw;
            }
        }

        /// <summary>
        /// 근무 종료 처리 (전보, 퇴직 등)
        /// </summary>
        public async Task<bool> EndCurrentAsync(string teacherId, string endDate)
        {
            const string query = @"
                UPDATE TeacherSchoolHistory 
                SET IsCurrent = 0, EndDate = @EndDate, UpdatedAt = @UpdatedAt 
                WHERE TeacherID = @TeacherID AND IsCurrent = 1";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@TeacherID", teacherId);
                cmd.Parameters.AddWithValue("@EndDate", endDate);
                cmd.Parameters.AddWithValue("@UpdatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                int rowsAffected = await cmd.ExecuteNonQueryAsync();
                LogInfo($"교사 현재 근무 종료 처리: TeacherID={teacherId}, EndDate={endDate}");
                return rowsAffected > 0;
            }
            catch (Exception ex)
            {
                LogError($"교사 근무 종료 처리 실패: TeacherID={teacherId}", ex);
                throw;
            }
        }

        /// <summary>
        /// IsCurrent 플래그 업데이트
        /// </summary>
        public async Task<bool> UpdateIsCurrentAsync(int no, bool isCurrent)
        {
            const string query = @"
                UPDATE TeacherSchoolHistory 
                SET IsCurrent = @IsCurrent, UpdatedAt = @UpdatedAt 
                WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@No", no);
                cmd.Parameters.AddWithValue("@IsCurrent", isCurrent ? 1 : 0);
                cmd.Parameters.AddWithValue("@UpdatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                int rowsAffected = await cmd.ExecuteNonQueryAsync();
                return rowsAffected > 0;
            }
            catch (Exception ex)
            {
                LogError($"IsCurrent 업데이트 실패: No={no}", ex);
                throw;
            }
        }

        #endregion

        #region Delete

        /// <summary>
        /// 교사 근무 이력 삭제
        /// </summary>
        public async Task<bool> DeleteAsync(int no)
        {
            const string query = "DELETE FROM TeacherSchoolHistory WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@No", no);

                int rowsAffected = await cmd.ExecuteNonQueryAsync();
                bool success = rowsAffected > 0;

                if (success)
                {
                    LogInfo($"교사 근무이력 삭제 완료: No={no}");
                }
                else
                {
                    LogWarning($"교사 근무이력 삭제 실패 (존재하지 않음): No={no}");
                }

                return success;
            }
            catch (Exception ex)
            {
                LogError($"교사 근무이력 삭제 실패: No={no}", ex);
                throw;
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// TeacherSchoolHistory 파라미터 추가
        /// </summary>
        private void AddHistoryParameters(SqliteCommand cmd, TeacherSchoolHistory history)
        {
            cmd.Parameters.AddWithValue("@TeacherID", history.TeacherID);
            cmd.Parameters.AddWithValue("@SchoolCode", history.SchoolCode);
            cmd.Parameters.AddWithValue("@StartDate", history.StartDate);
            cmd.Parameters.AddWithValue("@EndDate", history.EndDate ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@IsCurrent", history.IsCurrent ? 1 : 0);
            cmd.Parameters.AddWithValue("@Position", history.Position ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Role", history.Role ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Memo", history.Memo ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@CreatedAt", history.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@UpdatedAt", history.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        }

        /// <summary>
        /// SqliteDataReader를 TeacherSchoolHistory로 매핑
        /// </summary>
        private TeacherSchoolHistory MapHistory(SqliteDataReader reader)
        {
            return new TeacherSchoolHistory
            {
                No = reader.GetInt32(reader.GetOrdinal("No")),
                TeacherID = reader.GetString(reader.GetOrdinal("TeacherID")),
                SchoolCode = reader.GetString(reader.GetOrdinal("SchoolCode")),
                StartDate = reader.GetString(reader.GetOrdinal("StartDate")),
                EndDate = GetStringOrEmpty(reader, "EndDate"),
                IsCurrent = reader.GetInt32(reader.GetOrdinal("IsCurrent")) == 1,
                Position = GetStringOrEmpty(reader, "Position"),
                Role = GetStringOrEmpty(reader, "Role"),
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

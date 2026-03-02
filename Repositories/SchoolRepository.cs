using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Models;

namespace NewSchool.Repositories
{
    /// <summary>
    /// School Repository
    /// 학교 정보 (NEIS 표준) 관리
    /// </summary>
    public class SchoolRepository : BaseRepository
    {
        public SchoolRepository(string dbPath) : base(dbPath) { }

        #region Create

        /// <summary>
        /// 학교 정보 생성
        /// </summary>
        public async Task<int> CreateAsync(School school)
        {
            const string query = @"
                INSERT INTO School (
                    SchoolCode, ATPT_OFCDC_SC_CODE, ATPT_OFCDC_SC_NAME,
                    SchoolName, SchoolType, FoundationDate,
                    Address, Phone, Fax, Website,
                    PrincipalName, Memo, IsActive,
                    CreatedAt, UpdatedAt, IsDeleted
                ) VALUES (
                    @SchoolCode, @ATPT_OFCDC_SC_CODE, @ATPT_OFCDC_SC_NAME,
                    @SchoolName, @SchoolType, @FoundationDate,
                    @Address, @Phone, @Fax, @Website,
                    @PrincipalName, @Memo, @IsActive,
                    @CreatedAt, @UpdatedAt, @IsDeleted
                );
                SELECT last_insert_rowid();";

            try
            {
                using var cmd = CreateCommand(query);
                AddSchoolParameters(cmd, school);

                var result = await cmd.ExecuteScalarAsync();
                school.No = Convert.ToInt32(result);

                LogInfo($"학교 생성 완료: No={school.No}, SchoolCode={school.SchoolCode}, Name={school.SchoolName}");
                return school.No;
            }
            catch (Exception ex)
            {
                LogError($"학교 생성 실패: SchoolCode={school.SchoolCode}", ex);
                throw;
            }
        }

        #endregion

        #region Read

        /// <summary>
        /// No로 학교 조회
        /// </summary>
        public async Task<School?> GetByNoAsync(int no)
        {
            const string query = "SELECT * FROM School WHERE No = @No AND IsDeleted = 0";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@No", no);

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return MapSchool(reader);
                }

                LogWarning($"학교를 찾을 수 없음: No={no}");
                return null;
            }
            catch (Exception ex)
            {
                LogError($"학교 조회 실패: No={no}", ex);
                throw;
            }
        }

        /// <summary>
        /// SchoolCode로 학교 조회 (NEIS 표준 학교코드)
        /// </summary>
        public async Task<School?> GetBySchoolCodeAsync(string schoolCode)
        {
            const string query = "SELECT * FROM School WHERE SchoolCode = @SchoolCode AND IsDeleted = 0";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@SchoolCode", schoolCode);

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return MapSchool(reader);
                }

                LogWarning($"학교를 찾을 수 없음: SchoolCode={schoolCode}");
                return null;
            }
            catch (Exception ex)
            {
                LogError($"학교 조회 실패: SchoolCode={schoolCode}", ex);
                throw;
            }
        }

        /// <summary>
        /// 모든 활성 학교 조회
        /// </summary>
        public async Task<List<School>> GetAllActiveAsync()
        {
            const string query = @"
                SELECT * FROM School 
                WHERE IsActive = 1 AND IsDeleted = 0 
                ORDER BY SchoolName";

            var schools = new List<School>();

            try
            {
                using var cmd = CreateCommand(query);
                using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    schools.Add(MapSchool(reader));
                }

                LogInfo($"활성 학교 목록 조회 완료: {schools.Count}개");
                return schools;
            }
            catch (Exception ex)
            {
                LogError("활성 학교 목록 조회 실패", ex);
                throw;
            }
        }

        /// <summary>
        /// 시도교육청별 학교 조회
        /// </summary>
        public async Task<List<School>> GetByAtptCodeAsync(string atptCode)
        {
            const string query = @"
                SELECT * FROM School 
                WHERE ATPT_OFCDC_SC_CODE = @ATPT_OFCDC_SC_CODE 
                  AND IsActive = 1 
                  AND IsDeleted = 0 
                ORDER BY SchoolName";

            var schools = new List<School>();

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@ATPT_OFCDC_SC_CODE", atptCode);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    schools.Add(MapSchool(reader));
                }

                LogInfo($"시도교육청별 학교 조회 완료: Code={atptCode}, Count={schools.Count}");
                return schools;
            }
            catch (Exception ex)
            {
                LogError($"시도교육청별 학교 조회 실패: Code={atptCode}", ex);
                throw;
            }
        }

        /// <summary>
        /// 학교 종류별 조회 (초등학교/중학교/고등학교)
        /// </summary>
        public async Task<List<School>> GetBySchoolTypeAsync(string schoolType)
        {
            const string query = @"
                SELECT * FROM School 
                WHERE SchoolType = @SchoolType 
                  AND IsActive = 1 
                  AND IsDeleted = 0 
                ORDER BY SchoolName";

            var schools = new List<School>();

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@SchoolType", schoolType);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    schools.Add(MapSchool(reader));
                }

                LogInfo($"학교 종류별 조회 완료: Type={schoolType}, Count={schools.Count}");
                return schools;
            }
            catch (Exception ex)
            {
                LogError($"학교 종류별 조회 실패: Type={schoolType}", ex);
                throw;
            }
        }

        /// <summary>
        /// 학교 검색 (학교명)
        /// </summary>
        public async Task<List<School>> SearchAsync(string keyword)
        {
            const string query = @"
                SELECT * FROM School 
                WHERE (SchoolName LIKE @Keyword OR Address LIKE @Keyword)
                  AND IsDeleted = 0 
                ORDER BY SchoolName";

            var schools = new List<School>();

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@Keyword", $"%{keyword}%");

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    schools.Add(MapSchool(reader));
                }

                LogInfo($"학교 검색 완료: '{keyword}' - {schools.Count}개");
                return schools;
            }
            catch (Exception ex)
            {
                LogError($"학교 검색 실패: {keyword}", ex);
                throw;
            }
        }

        #endregion

        #region Update

        /// <summary>
        /// 학교 정보 수정
        /// </summary>
        public async Task<bool> UpdateAsync(School school)
        {
            const string query = @"
                UPDATE School SET
                    SchoolCode = @SchoolCode,
                    ATPT_OFCDC_SC_CODE = @ATPT_OFCDC_SC_CODE,
                    ATPT_OFCDC_SC_NAME = @ATPT_OFCDC_SC_NAME,
                    SchoolName = @SchoolName,
                    SchoolType = @SchoolType,
                    FoundationDate = @FoundationDate,
                    Address = @Address,
                    Phone = @Phone,
                    Fax = @Fax,
                    Website = @Website,
                    PrincipalName = @PrincipalName,
                    Memo = @Memo,
                    IsActive = @IsActive,
                    UpdatedAt = @UpdatedAt
                WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@No", school.No);
                AddSchoolParameters(cmd, school);

                int rowsAffected = await cmd.ExecuteNonQueryAsync();
                bool success = rowsAffected > 0;

                if (success)
                {
                    LogInfo($"학교 수정 완료: No={school.No}");
                }
                else
                {
                    LogWarning($"학교 수정 실패 (존재하지 않음): No={school.No}");
                }

                return success;
            }
            catch (Exception ex)
            {
                LogError($"학교 수정 실패: No={school.No}", ex);
                throw;
            }
        }

        /// <summary>
        /// 학교 활성 상태 변경 (폐교 처리 등)
        /// </summary>
        public async Task<bool> UpdateIsActiveAsync(int no, bool isActive)
        {
            const string query = @"
                UPDATE School 
                SET IsActive = @IsActive, UpdatedAt = @UpdatedAt 
                WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@No", no);
                cmd.Parameters.AddWithValue("@IsActive", isActive ? 1 : 0);
                cmd.Parameters.AddWithValue("@UpdatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                int rowsAffected = await cmd.ExecuteNonQueryAsync();
                LogInfo($"학교 활성 상태 변경: No={no}, IsActive={isActive}");
                return rowsAffected > 0;
            }
            catch (Exception ex)
            {
                LogError($"학교 활성 상태 변경 실패: No={no}", ex);
                throw;
            }
        }

        #endregion

        #region Delete

        /// <summary>
        /// 학교 논리 삭제
        /// </summary>
        public async Task<bool> DeleteAsync(int no)
        {
            const string query = @"
                UPDATE School 
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
                    LogInfo($"학교 논리 삭제 완료: No={no}");
                }
                else
                {
                    LogWarning($"학교 삭제 실패 (존재하지 않음): No={no}");
                }

                return success;
            }
            catch (Exception ex)
            {
                LogError($"학교 삭제 실패: No={no}", ex);
                throw;
            }
        }

        /// <summary>
        /// 학교 물리 삭제 (주의!)
        /// </summary>
        public async Task<bool> HardDeleteAsync(int no)
        {
            const string query = "DELETE FROM School WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@No", no);

                int rowsAffected = await cmd.ExecuteNonQueryAsync();
                bool success = rowsAffected > 0;

                if (success)
                {
                    LogInfo($"학교 물리 삭제 완료: No={no}");
                }

                return success;
            }
            catch (Exception ex)
            {
                LogError($"학교 물리 삭제 실패: No={no}", ex);
                throw;
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// School 파라미터 추가
        /// </summary>
        private void AddSchoolParameters(SqliteCommand cmd, School school)
        {
            cmd.Parameters.AddWithValue("@SchoolCode", school.SchoolCode);
            cmd.Parameters.AddWithValue("@ATPT_OFCDC_SC_CODE", school.ATPT_OFCDC_SC_CODE ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@ATPT_OFCDC_SC_NAME", school.ATPT_OFCDC_SC_NAME ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@SchoolName", school.SchoolName);
            cmd.Parameters.AddWithValue("@SchoolType", school.SchoolType ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@FoundationDate", school.FoundationDate ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Address", school.Address ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Phone", school.Phone ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Fax", school.Fax ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Website", school.Website ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@PrincipalName", school.PrincipalName ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Memo", school.Memo ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@IsActive", school.IsActive ? 1 : 0);
            cmd.Parameters.AddWithValue("@CreatedAt", school.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@UpdatedAt", school.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@IsDeleted", school.IsDeleted ? 1 : 0);
        }

        /// <summary>
        /// SqliteDataReader를 School로 매핑
        /// </summary>
        private School MapSchool(SqliteDataReader reader)
        {
            return new School
            {
                No = reader.GetInt32(reader.GetOrdinal("No")),
                SchoolCode = reader.GetString(reader.GetOrdinal("SchoolCode")),
                ATPT_OFCDC_SC_CODE = GetStringOrEmpty(reader, "ATPT_OFCDC_SC_CODE"),
                ATPT_OFCDC_SC_NAME = GetStringOrEmpty(reader, "ATPT_OFCDC_SC_NAME"),
                SchoolName = reader.GetString(reader.GetOrdinal("SchoolName")),
                SchoolType = GetStringOrEmpty(reader, "SchoolType"),
                FoundationDate = GetStringOrEmpty(reader, "FoundationDate"),
                Address = GetStringOrEmpty(reader, "Address"),
                Phone = GetStringOrEmpty(reader, "Phone"),
                Fax = GetStringOrEmpty(reader, "Fax"),
                Website = GetStringOrEmpty(reader, "Website"),
                PrincipalName = GetStringOrEmpty(reader, "PrincipalName"),
                Memo = GetStringOrEmpty(reader, "Memo"),
                IsActive = reader.GetInt32(reader.GetOrdinal("IsActive")) == 1,
                CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
                UpdatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("UpdatedAt"))),
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

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

        // 이 앱은 학교 한 곳만 다룬다. 그래서 여러 학교를 전제한 조회·수정
        // (GetByNoAsync·GetAllActiveAsync·GetByAtptCodeAsync·GetBySchoolTypeAsync·SearchAsync·
        //  UpdateIsActiveAsync·DeleteAsync)은 호출부가 없어 전부 지웠다(39차).
        // 남은 건 SchoolService 가 쓰는 CreateAsync·GetBySchoolCodeAsync·UpdateAsync 셋뿐이다.

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
                var cache = new ReaderColumnCache();
                cache.Initialize(reader);   // 컬럼 인덱스를 행마다 다시 찾지 않도록 한 번만
                if (await reader.ReadAsync())
                {
                    return MapSchool(reader, cache);
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
        private School MapSchool(SqliteDataReader reader, ReaderColumnCache cache)
        {
            return new School
            {
                No = reader.GetInt32(cache.GetOrdinal("No")),
                SchoolCode = reader.GetString(cache.GetOrdinal("SchoolCode")),
                ATPT_OFCDC_SC_CODE = GetStringOrEmpty(reader, cache, "ATPT_OFCDC_SC_CODE"),
                ATPT_OFCDC_SC_NAME = GetStringOrEmpty(reader, cache, "ATPT_OFCDC_SC_NAME"),
                SchoolName = reader.GetString(cache.GetOrdinal("SchoolName")),
                SchoolType = GetStringOrEmpty(reader, cache, "SchoolType"),
                FoundationDate = GetStringOrEmpty(reader, cache, "FoundationDate"),
                Address = GetStringOrEmpty(reader, cache, "Address"),
                Phone = GetStringOrEmpty(reader, cache, "Phone"),
                Fax = GetStringOrEmpty(reader, cache, "Fax"),
                Website = GetStringOrEmpty(reader, cache, "Website"),
                PrincipalName = GetStringOrEmpty(reader, cache, "PrincipalName"),
                Memo = GetStringOrEmpty(reader, cache, "Memo"),
                IsActive = reader.GetInt32(cache.GetOrdinal("IsActive")) == 1,
                CreatedAt = DateTime.Parse(reader.GetString(cache.GetOrdinal("CreatedAt"))),
                UpdatedAt = DateTime.Parse(reader.GetString(cache.GetOrdinal("UpdatedAt"))),
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

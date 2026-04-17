using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Models;

namespace NewSchool.Repositories
{
    /// <summary>
    /// SchoolSchedule Repository
    /// 학사일정 관리
    /// </summary>
    public class SchoolScheduleRepository : BaseRepository
    {
        public SchoolScheduleRepository(string dbPath) : base(dbPath) { }

        #region Create

        /// <summary>
        /// 학사일정 생성
        /// </summary>
        public async Task<int> CreateAsync(SchoolSchedule schedule)
        {
            const string query = @"
                INSERT INTO SchoolSchedule (
                    ATPT_OFCDC_SC_CODE, ATPT_OFCDC_SC_NM, SD_SCHUL_CODE, 
                    SCHUL_NM, AY, AA_YMD, EVENT_NM, EVENT_CNTNT, 
                    ONE_GRADE_EVENT_YN, TW_GRADE_EVENT_YN, THREE_GRADE_EVENT_YN, 
                    FR_GRADE_EVENT_YN, FIV_GRADE_EVENT_YN, SIX_GRADE_EVENT_YN, 
                    SBTR_DD_SC_NM, IsManual, CreatedAt, UpdatedAt, IsDeleted
                ) VALUES (
                    @ATPT_OFCDC_SC_CODE, @ATPT_OFCDC_SC_NM, @SD_SCHUL_CODE, 
                    @SCHUL_NM, @AY, @AA_YMD, @EVENT_NM, @EVENT_CNTNT,
                    @ONE_GRADE_EVENT_YN, @TW_GRADE_EVENT_YN, @THREE_GRADE_EVENT_YN,
                    @FR_GRADE_EVENT_YN, @FIV_GRADE_EVENT_YN, @SIX_GRADE_EVENT_YN,
                    @SBTR_DD_SC_NM, @IsManual, @CreatedAt, @UpdatedAt, @IsDeleted
                );
                SELECT last_insert_rowid();";

            try
            {
                using var cmd = CreateCommand(query);
                AddScheduleParameters(cmd, schedule);

                var result = await cmd.ExecuteScalarAsync();
                schedule.No = Convert.ToInt32(result);

                LogInfo($"학사일정 생성 완료: No={schedule.No}, Date={schedule.AA_YMD:yyyy-MM-dd}");
                return schedule.No;
            }
            catch (Exception ex)
            {
                LogError($"학사일정 생성 실패: Date={schedule.AA_YMD:yyyy-MM-dd}", ex);
                throw;
            }
        }

        /// <summary>
        /// 여러 학사일정 일괄 생성 (중복 체크)
        /// 일괄 SELECT로 기존 키 조회 → 메모리에서 필터 → 배치 INSERT
        /// </summary>
        public async Task<int> CreateBulkAsync(List<SchoolSchedule> schedules)
        {
            if (schedules == null || schedules.Count == 0)
                return 0;

            try
            {
                return await ExecuteInTransactionAsync(async () =>
                {
                    // 1. 기존 키(학교코드+날짜+행사명) 일괄 조회
                    var existingKeys = new HashSet<string>(StringComparer.Ordinal);
                    var schoolCodes = new HashSet<string>();
                    foreach (var s in schedules)
                        schoolCodes.Add(s.SD_SCHUL_CODE ?? string.Empty);

                    foreach (var code in schoolCodes)
                    {
                        const string selectQuery = @"
                            SELECT SD_SCHUL_CODE, AA_YMD, EVENT_NM
                            FROM SchoolSchedule
                            WHERE SD_SCHUL_CODE = @SchoolCode AND IsDeleted = 0";

                        using var selectCmd = CreateCommand(selectQuery);
                        selectCmd.Parameters.AddWithValue("@SchoolCode", code);
                        using var reader = await selectCmd.ExecuteReaderAsync();
                        while (await reader.ReadAsync())
                        {
                            var key = $"{reader.GetString(0)}|{reader.GetString(1)}|{reader.GetString(2)}";
                            existingKeys.Add(key);
                        }
                    }

                    // 2. 중복 필터링 후 배치 INSERT
                    int count = 0;
                    foreach (var schedule in schedules)
                    {
                        var key = $"{schedule.SD_SCHUL_CODE ?? ""}|{schedule.AA_YMD:yyyyMMdd}|{schedule.EVENT_NM ?? ""}";
                        if (existingKeys.Contains(key))
                        {
                            LogInfo($"중복 학사일정 건너뜀: {schedule.AA_YMD:yyyy-MM-dd} - {schedule.EVENT_NM}");
                            continue;
                        }

                        await CreateAsync(schedule);
                        existingKeys.Add(key); // 같은 배치 내 중복 방지
                        count++;
                    }
                    return count;
                });
            }
            catch (Exception ex)
            {
                LogError($"학사일정 일괄 생성 실패: {schedules.Count}개", ex);
                throw;
            }
        }

        /// <summary>
        /// 중복 학사일정 체크 (학교코드 + 날짜 + 행사명)
        /// </summary>
        private async Task<bool> IsDuplicateAsync(SchoolSchedule schedule)
        {
            const string query = @"
                SELECT EXISTS(SELECT 1 FROM SchoolSchedule
                WHERE SD_SCHUL_CODE = @SchoolCode
                AND AA_YMD = @Date
                AND EVENT_NM = @EventName
                AND IsDeleted = 0)";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@SchoolCode", schedule.SD_SCHUL_CODE ?? string.Empty);
                cmd.Parameters.AddWithValue("@Date", schedule.AA_YMD.ToString("yyyyMMdd"));
                cmd.Parameters.AddWithValue("@EventName", schedule.EVENT_NM ?? string.Empty);

                var result = await cmd.ExecuteScalarAsync();
                return Convert.ToInt32(result) == 1;
            }
            catch (Exception ex)
            {
                LogError($"중복 체크 실패: {schedule.AA_YMD:yyyy-MM-dd} - {schedule.EVENT_NM}", ex);
                return false; // 오류 시 중복으로 간주하지 않음
            }
        }

        #endregion

        #region Read

        /// <summary>
        /// ID로 학사일정 조회
        /// </summary>
        public async Task<SchoolSchedule?> GetByIdAsync(int no)
        {
            const string query = "SELECT * FROM SchoolSchedule WHERE No = @No AND IsDeleted = 0";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@No", no);

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return MapSchedule(reader);
                }

                LogWarning($"학사일정을 찾을 수 없음: No={no}");
                return null;
            }
            catch (Exception ex)
            {
                LogError($"학사일정 조회 실패: No={no}", ex);
                throw;
            }
        }

        /// <summary>
        /// 날짜 범위로 학사일정 조회
        /// </summary>
        public async Task<List<SchoolSchedule>> GetByDateRangeAsync(string schoolCode,DateTime startDate, DateTime endDate)
        {
            var schedules = new List<SchoolSchedule>();

            try
            {
                const string query = @"
                    SELECT * FROM SchoolSchedule 
                    WHERE SD_SCHUL_CODE = @SchoolCode AND AA_YMD >= @StartDate AND AA_YMD < @EndDate
                    AND IsDeleted = 0
                    ORDER BY AA_YMD ASC";

                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@SchoolCode", schoolCode);
                cmd.Parameters.AddWithValue("@StartDate", startDate.ToString("yyyyMMdd"));
                cmd.Parameters.AddWithValue("@EndDate", endDate.ToString("yyyyMMdd"));

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    schedules.Add(MapSchedule(reader));
                }

                LogInfo($"학사일정 조회 완료: {schedules.Count}개 ({startDate:yyyy-MM-dd} ~ {endDate:yyyy-MM-dd})");
                return schedules;
            }
            catch (Exception ex)
            {
                LogError($"학사일정 조회 실패: {startDate:yyyy-MM-dd}", ex);
                throw;
            }
        }

        /// <summary>
        /// 학교코드로 학사일정 조회
        /// </summary>
        public async Task<List<SchoolSchedule>> GetBySchoolYearAsync(string schoolCode, int schoolyear)
        {
            var schedules = new List<SchoolSchedule>();

            try
            {
                const string query = @"
                    SELECT * FROM SchoolSchedule 
                    WHERE SD_SCHUL_CODE = @SchoolCode AND AY = @Year AND IsDeleted = 0
                    ORDER BY AA_YMD ASC";

                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@SchoolCode", schoolCode);
                cmd.Parameters.AddWithValue("@Year", schoolyear);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    schedules.Add(MapSchedule(reader));
                }

                LogInfo($"학사일정 조회 완료: {schedules.Count}개 (학교 {schoolCode}, 학년도 {schoolyear})");
                return schedules;
            }
            catch (Exception ex)
            {
                LogError($"학사일정 조회 실패: 학교 {schoolCode}, 학년도 {schoolyear}", ex);
                throw;
            }
        }

        #endregion

        #region Update

        /// <summary>
        /// 학사일정 수정
        /// </summary>
        public async Task<bool> UpdateAsync(SchoolSchedule schedule)
        {
            const string query = @"
                UPDATE SchoolSchedule SET
                    ATPT_OFCDC_SC_CODE = @ATPT_OFCDC_SC_CODE,
                    ATPT_OFCDC_SC_NM = @ATPT_OFCDC_SC_NM,
                    SD_SCHUL_CODE = @SD_SCHUL_CODE,
                    SCHUL_NM = @SCHUL_NM,
                    AY = @AY,
                    AA_YMD = @AA_YMD,
                    EVENT_NM = @EVENT_NM,
                    EVENT_CNTNT = @EVENT_CNTNT,
                    ONE_GRADE_EVENT_YN = @ONE_GRADE_EVENT_YN,
                    TW_GRADE_EVENT_YN = @TW_GRADE_EVENT_YN,
                    THREE_GRADE_EVENT_YN = @THREE_GRADE_EVENT_YN,
                    FR_GRADE_EVENT_YN = @FR_GRADE_EVENT_YN,
                    FIV_GRADE_EVENT_YN = @FIV_GRADE_EVENT_YN,
                    SIX_GRADE_EVENT_YN = @SIX_GRADE_EVENT_YN,
                    SBTR_DD_SC_NM = @SBTR_DD_SC_NM,
                    IsManual = @IsManual,
                    UpdatedAt = @UpdatedAt
                WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@No", schedule.No);
                schedule.UpdatedAt = DateTime.Now;
                AddScheduleParameters(cmd, schedule);

                int rowsAffected = await cmd.ExecuteNonQueryAsync();
                bool success = rowsAffected > 0;

                if (success)
                {
                    LogInfo($"학사일정 수정 완료: No={schedule.No}");
                }
                else
                {
                    LogWarning($"학사일정 수정 실패 (존재하지 않음): No={schedule.No}");
                }

                return success;
            }
            catch (Exception ex)
            {
                LogError($"학사일정 수정 실패: No={schedule.No}", ex);
                throw;
            }
        }

        #endregion

        #region Delete

        /// <summary>
        /// 학사일정 삭제 (Soft Delete)
        /// </summary>
        public async Task<bool> DeleteAsync(int no)
        {
            const string query = @"
                UPDATE SchoolSchedule 
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
                    LogInfo($"학사일정 삭제 완료: No={no}");
                }
                else
                {
                    LogWarning($"학사일정 삭제 실패 (존재하지 않음): No={no}");
                }

                return success;
            }
            catch (Exception ex)
            {
                LogError($"학사일정 삭제 실패: No={no}", ex);
                throw;
            }
        }
        /// <summary>
        /// 여러 학사일정 일괄 삭제
        /// </summary>
        public async Task<int> DeleteBulkAsync(List<int> SchoolScheduleNoList)
        {
            if (SchoolScheduleNoList == null || SchoolScheduleNoList.Count == 0)
                return 0;

            try
            {
                return await ExecuteInTransactionAsync(async () =>
                {
                    int count = 0;
                    foreach (var no in SchoolScheduleNoList)
                    {
                        await DeleteAsync(no);
                        count++;
                    }
                    return count;
                });
            }
            catch (Exception ex)
            {
                LogError($"학사일정 일괄 삭제 실패: {SchoolScheduleNoList.Count}개", ex);
                throw;
            }
        }
        /// <summary>
        /// 학년도별 학사일정 삭제 (Soft Delete)
        /// </summary>
        public async Task<int> DeleteByYearAsync(int year)
        {
            const string query = @"
                UPDATE SchoolSchedule 
                SET IsDeleted = 1, UpdatedAt = @UpdatedAt 
                WHERE AY = @Year";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@Year", year);
                cmd.Parameters.AddWithValue("@UpdatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                int rowsAffected = await cmd.ExecuteNonQueryAsync();
                LogInfo($"학사일정 삭제 완료: {rowsAffected}개 (학년도 {year})");
                return rowsAffected;
            }
            catch (Exception ex)
            {
                LogError($"학사일정 삭제 실패: 학년도 {year}", ex);
                throw;
            }
        }

        /// <summary>
        /// 학사일정 영구 삭제 (Hard Delete)
        /// </summary>
        public async Task<bool> PermanentDeleteAsync(int no)
        {
            const string query = "DELETE FROM SchoolSchedule WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@No", no);

                int rowsAffected = await cmd.ExecuteNonQueryAsync();
                bool success = rowsAffected > 0;

                if (success)
                {
                    LogInfo($"학사일정 영구 삭제 완료: No={no}");
                }
                else
                {
                    LogWarning($"학사일정 영구 삭제 실패 (존재하지 않음): No={no}");
                }

                return success;
            }
            catch (Exception ex)
            {
                LogError($"학사일정 영구 삭제 실패: No={no}", ex);
                throw;
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// SchoolSchedule 파라미터 추가
        /// </summary>
        private void AddScheduleParameters(SqliteCommand cmd, SchoolSchedule schedule)
        {
            cmd.Parameters.AddWithValue("@ATPT_OFCDC_SC_CODE", schedule.ATPT_OFCDC_SC_CODE ?? string.Empty);
            cmd.Parameters.AddWithValue("@ATPT_OFCDC_SC_NM", schedule.ATPT_OFCDC_SC_NM ?? string.Empty);
            cmd.Parameters.AddWithValue("@SD_SCHUL_CODE", schedule.SD_SCHUL_CODE ?? string.Empty);
            cmd.Parameters.AddWithValue("@SCHUL_NM", schedule.SCHUL_NM ?? string.Empty);
            cmd.Parameters.AddWithValue("@AY", schedule.AY);
            cmd.Parameters.AddWithValue("@AA_YMD", schedule.AA_YMD.ToString("yyyyMMdd"));
            cmd.Parameters.AddWithValue("@EVENT_NM", schedule.EVENT_NM ?? string.Empty);
            cmd.Parameters.AddWithValue("@EVENT_CNTNT", schedule.EVENT_CNTNT ?? string.Empty);
            cmd.Parameters.AddWithValue("@ONE_GRADE_EVENT_YN", schedule.ONE_GRADE_EVENT_YN ? 1 : 0);
            cmd.Parameters.AddWithValue("@TW_GRADE_EVENT_YN", schedule.TW_GRADE_EVENT_YN ? 1 : 0);
            cmd.Parameters.AddWithValue("@THREE_GRADE_EVENT_YN", schedule.THREE_GRADE_EVENT_YN ? 1 : 0);
            cmd.Parameters.AddWithValue("@FR_GRADE_EVENT_YN", schedule.FR_GRADE_EVENT_YN ? 1 : 0);
            cmd.Parameters.AddWithValue("@FIV_GRADE_EVENT_YN", schedule.FIV_GRADE_EVENT_YN ? 1 : 0);
            cmd.Parameters.AddWithValue("@SIX_GRADE_EVENT_YN", schedule.SIX_GRADE_EVENT_YN ? 1 : 0);
            cmd.Parameters.AddWithValue("@SBTR_DD_SC_NM", schedule.SBTR_DD_SC_NM ?? string.Empty);
            cmd.Parameters.AddWithValue("@IsManual", schedule.IsManual ? 1 : 0);
            cmd.Parameters.AddWithValue("@CreatedAt", schedule.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@UpdatedAt", schedule.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@IsDeleted", schedule.IsDeleted ? 1 : 0);
        }

        /// <summary>
        /// SqliteDataReader를 SchoolSchedule로 매핑
        /// </summary>
        private SchoolSchedule MapSchedule(SqliteDataReader reader)
        {
            var dateString = reader.GetString(reader.GetOrdinal("AA_YMD"));

            return new SchoolSchedule
            {
                No = reader.GetInt32(reader.GetOrdinal("No")),
                ATPT_OFCDC_SC_CODE = reader.GetString(reader.GetOrdinal("ATPT_OFCDC_SC_CODE")),
                ATPT_OFCDC_SC_NM = reader.GetString(reader.GetOrdinal("ATPT_OFCDC_SC_NM")),
                SD_SCHUL_CODE = reader.GetString(reader.GetOrdinal("SD_SCHUL_CODE")),
                SCHUL_NM = reader.GetString(reader.GetOrdinal("SCHUL_NM")),
                AY = reader.GetInt32(reader.GetOrdinal("AY")),
                AA_YMD = DateTime.ParseExact(dateString, "yyyyMMdd",
                    System.Globalization.CultureInfo.InvariantCulture),
                EVENT_NM = reader.GetString(reader.GetOrdinal("EVENT_NM")),
                EVENT_CNTNT = reader.GetString(reader.GetOrdinal("EVENT_CNTNT")),
                ONE_GRADE_EVENT_YN = reader.GetInt32(reader.GetOrdinal("ONE_GRADE_EVENT_YN")) == 1,
                TW_GRADE_EVENT_YN = reader.GetInt32(reader.GetOrdinal("TW_GRADE_EVENT_YN")) == 1,
                THREE_GRADE_EVENT_YN = reader.GetInt32(reader.GetOrdinal("THREE_GRADE_EVENT_YN")) == 1,
                FR_GRADE_EVENT_YN = reader.GetInt32(reader.GetOrdinal("FR_GRADE_EVENT_YN")) == 1,
                FIV_GRADE_EVENT_YN = reader.GetInt32(reader.GetOrdinal("FIV_GRADE_EVENT_YN")) == 1,
                SIX_GRADE_EVENT_YN = reader.GetInt32(reader.GetOrdinal("SIX_GRADE_EVENT_YN")) == 1,
                SBTR_DD_SC_NM = reader.GetString(reader.GetOrdinal("SBTR_DD_SC_NM")),
                IsManual = reader.GetInt32(reader.GetOrdinal("IsManual")) == 1,
                CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
                UpdatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("UpdatedAt"))),
                IsDeleted = reader.GetInt32(reader.GetOrdinal("IsDeleted")) == 1
            };
        }

        #endregion
    }
}

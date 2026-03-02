using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Models;

namespace NewSchool.Repositories
{
    /// <summary>
    /// WeeklyLessonHours Repository
    /// 주차별 수업 시수 관리
    /// </summary>
    public class WeeklyLessonHoursRepository : BaseRepository
    {
        public WeeklyLessonHoursRepository(string dbPath) : base(dbPath)
        {
            EnsureTableExists();
        }

        #region Table Setup

        private void EnsureTableExists()
        {
            const string sql = @"
                CREATE TABLE IF NOT EXISTS WeeklyLessonHours (
                    No INTEGER PRIMARY KEY AUTOINCREMENT,
                    YearPlanNo INTEGER NOT NULL,
                    Week INTEGER NOT NULL,
                    WeekStartDate TEXT NOT NULL,
                    WeekEndDate TEXT NOT NULL,
                    AutoCalculatedHours INTEGER NOT NULL,
                    PlannedHours INTEGER NOT NULL,
                    ActualHours INTEGER DEFAULT 0,
                    IsModified INTEGER DEFAULT 0,
                    Notes TEXT,
                    FOREIGN KEY (YearPlanNo) REFERENCES SubjectYearPlan(No) ON DELETE CASCADE,
                    UNIQUE(YearPlanNo, Week)
                );

                CREATE INDEX IF NOT EXISTS idx_weekly_hours_plan
                    ON WeeklyLessonHours(YearPlanNo, Week);
            ";

            using var cmd = Connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();

            LogInfo("WeeklyLessonHours 테이블 확인/생성 완료");
        }

        #endregion

        #region Create

        /// <summary>
        /// 주차별 시수 생성
        /// </summary>
        public async Task<int> CreateAsync(WeeklyLessonHours hours)
        {
            const string query = @"
                INSERT INTO WeeklyLessonHours (
                    YearPlanNo, Week, WeekStartDate, WeekEndDate,
                    AutoCalculatedHours, PlannedHours, ActualHours, IsModified, Notes
                ) VALUES (
                    @YearPlanNo, @Week, @WeekStartDate, @WeekEndDate,
                    @AutoCalculatedHours, @PlannedHours, @ActualHours, @IsModified, @Notes
                );
                SELECT last_insert_rowid();";

            try
            {
                using var cmd = CreateCommand(query);
                AddParameters(cmd, hours);

                var result = await cmd.ExecuteScalarAsync();
                hours.No = Convert.ToInt32(result);

                LogInfo($"주차별 시수 생성 완료: No={hours.No}, Week={hours.Week}");
                return hours.No;
            }
            catch (Exception ex)
            {
                LogError($"주차별 시수 생성 실패: YearPlanNo={hours.YearPlanNo}, Week={hours.Week}", ex);
                throw;
            }
        }

        /// <summary>
        /// 주차별 시수 일괄 생성
        /// </summary>
        public async Task<int> CreateBatchAsync(List<WeeklyLessonHours> hoursList)
        {
            if (hoursList == null || hoursList.Count == 0)
                return 0;

            int count = 0;

            try
            {
                BeginTransaction();

                foreach (var hours in hoursList)
                {
                    await CreateAsync(hours);
                    count++;
                }

                Commit();
                LogInfo($"주차별 시수 일괄 생성 완료: {count}개");
                return count;
            }
            catch (Exception ex)
            {
                Rollback();
                LogError($"주차별 시수 일괄 생성 실패", ex);
                throw;
            }
        }

        #endregion

        #region Read

        /// <summary>
        /// No로 조회
        /// </summary>
        public async Task<WeeklyLessonHours?> GetByIdAsync(int no)
        {
            const string query = "SELECT * FROM WeeklyLessonHours WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@No", no);

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return MapHours(reader);
                }
                return null;
            }
            catch (Exception ex)
            {
                LogError($"주차별 시수 조회 실패: No={no}", ex);
                throw;
            }
        }

        /// <summary>
        /// 연간계획별 주차 시수 목록 조회
        /// </summary>
        public async Task<List<WeeklyLessonHours>> GetByYearPlanAsync(int yearPlanNo)
        {
            const string query = @"
                SELECT * FROM WeeklyLessonHours
                WHERE YearPlanNo = @YearPlanNo
                ORDER BY Week";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@YearPlanNo", yearPlanNo);

                var hoursList = new List<WeeklyLessonHours>();
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    hoursList.Add(MapHours(reader));
                }
                return hoursList;
            }
            catch (Exception ex)
            {
                LogError($"연간계획별 주차 시수 조회 실패: YearPlanNo={yearPlanNo}", ex);
                throw;
            }
        }

        /// <summary>
        /// 특정 주차 조회
        /// </summary>
        public async Task<WeeklyLessonHours?> GetByWeekAsync(int yearPlanNo, int week)
        {
            const string query = @"
                SELECT * FROM WeeklyLessonHours
                WHERE YearPlanNo = @YearPlanNo AND Week = @Week";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@YearPlanNo", yearPlanNo);
                cmd.Parameters.AddWithValue("@Week", week);

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return MapHours(reader);
                }
                return null;
            }
            catch (Exception ex)
            {
                LogError($"주차 조회 실패: YearPlanNo={yearPlanNo}, Week={week}", ex);
                throw;
            }
        }

        /// <summary>
        /// 수정된 주차만 조회
        /// </summary>
        public async Task<List<WeeklyLessonHours>> GetModifiedWeeksAsync(int yearPlanNo)
        {
            const string query = @"
                SELECT * FROM WeeklyLessonHours
                WHERE YearPlanNo = @YearPlanNo AND IsModified = 1
                ORDER BY Week";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@YearPlanNo", yearPlanNo);

                var hoursList = new List<WeeklyLessonHours>();
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    hoursList.Add(MapHours(reader));
                }
                return hoursList;
            }
            catch (Exception ex)
            {
                LogError($"수정된 주차 조회 실패: YearPlanNo={yearPlanNo}", ex);
                throw;
            }
        }

        /// <summary>
        /// 총 계획 시수 합계 조회
        /// </summary>
        public async Task<int> GetTotalPlannedHoursAsync(int yearPlanNo)
        {
            const string query = @"
                SELECT COALESCE(SUM(PlannedHours), 0)
                FROM WeeklyLessonHours
                WHERE YearPlanNo = @YearPlanNo";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@YearPlanNo", yearPlanNo);

                var result = await cmd.ExecuteScalarAsync();
                return Convert.ToInt32(result);
            }
            catch (Exception ex)
            {
                LogError($"총 계획 시수 조회 실패: YearPlanNo={yearPlanNo}", ex);
                throw;
            }
        }

        /// <summary>
        /// 총 실제 시수 합계 조회
        /// </summary>
        public async Task<int> GetTotalActualHoursAsync(int yearPlanNo)
        {
            const string query = @"
                SELECT COALESCE(SUM(ActualHours), 0)
                FROM WeeklyLessonHours
                WHERE YearPlanNo = @YearPlanNo";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@YearPlanNo", yearPlanNo);

                var result = await cmd.ExecuteScalarAsync();
                return Convert.ToInt32(result);
            }
            catch (Exception ex)
            {
                LogError($"총 실제 시수 조회 실패: YearPlanNo={yearPlanNo}", ex);
                throw;
            }
        }

        #endregion

        #region Update

        /// <summary>
        /// 주차별 시수 수정
        /// </summary>
        public async Task<bool> UpdateAsync(WeeklyLessonHours hours)
        {
            const string query = @"
                UPDATE WeeklyLessonHours SET
                    YearPlanNo = @YearPlanNo,
                    Week = @Week,
                    WeekStartDate = @WeekStartDate,
                    WeekEndDate = @WeekEndDate,
                    AutoCalculatedHours = @AutoCalculatedHours,
                    PlannedHours = @PlannedHours,
                    ActualHours = @ActualHours,
                    IsModified = @IsModified,
                    Notes = @Notes
                WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                AddParameters(cmd, hours);

                int affected = await cmd.ExecuteNonQueryAsync();
                bool success = affected > 0;

                if (success)
                    LogInfo($"주차별 시수 수정 완료: No={hours.No}");

                return success;
            }
            catch (Exception ex)
            {
                LogError($"주차별 시수 수정 실패: No={hours.No}", ex);
                throw;
            }
        }

        /// <summary>
        /// 계획 시수 수정 (사용자 수정)
        /// </summary>
        public async Task<bool> UpdatePlannedHoursAsync(int no, int plannedHours, string? notes = null)
        {
            var query = @"
                UPDATE WeeklyLessonHours SET
                    PlannedHours = @PlannedHours,
                    IsModified = 1";

            if (notes != null)
                query += ", Notes = @Notes";

            query += " WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@No", no);
                cmd.Parameters.AddWithValue("@PlannedHours", plannedHours);
                if (notes != null)
                    cmd.Parameters.AddWithValue("@Notes", notes);

                int affected = await cmd.ExecuteNonQueryAsync();
                bool success = affected > 0;

                if (success)
                    LogInfo($"계획 시수 수정 완료: No={no}, PlannedHours={plannedHours}");

                return success;
            }
            catch (Exception ex)
            {
                LogError($"계획 시수 수정 실패: No={no}", ex);
                throw;
            }
        }

        /// <summary>
        /// 실제 시수 업데이트
        /// </summary>
        public async Task<bool> UpdateActualHoursAsync(int no, int actualHours)
        {
            const string query = @"
                UPDATE WeeklyLessonHours SET
                    ActualHours = @ActualHours
                WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@No", no);
                cmd.Parameters.AddWithValue("@ActualHours", actualHours);

                int affected = await cmd.ExecuteNonQueryAsync();
                return affected > 0;
            }
            catch (Exception ex)
            {
                LogError($"실제 시수 업데이트 실패: No={no}", ex);
                throw;
            }
        }

        #endregion

        #region Delete

        /// <summary>
        /// 삭제
        /// </summary>
        public async Task<bool> DeleteAsync(int no)
        {
            const string query = "DELETE FROM WeeklyLessonHours WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@No", no);

                int affected = await cmd.ExecuteNonQueryAsync();
                bool success = affected > 0;

                if (success)
                    LogInfo($"주차별 시수 삭제 완료: No={no}");

                return success;
            }
            catch (Exception ex)
            {
                LogError($"주차별 시수 삭제 실패: No={no}", ex);
                throw;
            }
        }

        /// <summary>
        /// 연간계획별 전체 삭제
        /// </summary>
        public async Task<int> DeleteByYearPlanAsync(int yearPlanNo)
        {
            const string query = "DELETE FROM WeeklyLessonHours WHERE YearPlanNo = @YearPlanNo";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@YearPlanNo", yearPlanNo);

                int affected = await cmd.ExecuteNonQueryAsync();
                LogInfo($"연간계획별 주차 시수 삭제 완료: YearPlanNo={yearPlanNo}, 삭제 수={affected}");
                return affected;
            }
            catch (Exception ex)
            {
                LogError($"연간계획별 주차 시수 삭제 실패: YearPlanNo={yearPlanNo}", ex);
                throw;
            }
        }

        /// <summary>
        /// 특정 주차 이후 삭제
        /// </summary>
        public async Task<int> DeleteFromWeekAsync(int yearPlanNo, int fromWeek)
        {
            const string query = @"
                DELETE FROM WeeklyLessonHours
                WHERE YearPlanNo = @YearPlanNo AND Week >= @FromWeek";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@YearPlanNo", yearPlanNo);
                cmd.Parameters.AddWithValue("@FromWeek", fromWeek);

                int affected = await cmd.ExecuteNonQueryAsync();
                LogInfo($"특정 주차 이후 삭제 완료: YearPlanNo={yearPlanNo}, FromWeek={fromWeek}, 삭제 수={affected}");
                return affected;
            }
            catch (Exception ex)
            {
                LogError($"특정 주차 이후 삭제 실패: YearPlanNo={yearPlanNo}", ex);
                throw;
            }
        }

        #endregion

        #region Helper Methods

        private void AddParameters(SqliteCommand cmd, WeeklyLessonHours hours)
        {
            cmd.Parameters.AddWithValue("@No", hours.No);
            cmd.Parameters.AddWithValue("@YearPlanNo", hours.YearPlanNo);
            cmd.Parameters.AddWithValue("@Week", hours.Week);
            cmd.Parameters.AddWithValue("@WeekStartDate", hours.WeekStartDate ?? string.Empty);
            cmd.Parameters.AddWithValue("@WeekEndDate", hours.WeekEndDate ?? string.Empty);
            cmd.Parameters.AddWithValue("@AutoCalculatedHours", hours.AutoCalculatedHours);
            cmd.Parameters.AddWithValue("@PlannedHours", hours.PlannedHours);
            cmd.Parameters.AddWithValue("@ActualHours", hours.ActualHours);
            cmd.Parameters.AddWithValue("@IsModified", hours.IsModified);
            cmd.Parameters.AddWithValue("@Notes", hours.Notes ?? string.Empty);
        }

        private WeeklyLessonHours MapHours(SqliteDataReader reader)
        {
            return new WeeklyLessonHours
            {
                No = reader.GetInt32(reader.GetOrdinal("No")),
                YearPlanNo = reader.GetInt32(reader.GetOrdinal("YearPlanNo")),
                Week = reader.GetInt32(reader.GetOrdinal("Week")),
                WeekStartDate = reader.GetString(reader.GetOrdinal("WeekStartDate")),
                WeekEndDate = reader.GetString(reader.GetOrdinal("WeekEndDate")),
                AutoCalculatedHours = reader.GetInt32(reader.GetOrdinal("AutoCalculatedHours")),
                PlannedHours = reader.GetInt32(reader.GetOrdinal("PlannedHours")),
                ActualHours = reader.GetInt32(reader.GetOrdinal("ActualHours")),
                IsModified = reader.GetInt32(reader.GetOrdinal("IsModified")),
                Notes = reader.IsDBNull(reader.GetOrdinal("Notes"))
                    ? string.Empty
                    : reader.GetString(reader.GetOrdinal("Notes"))
            };
        }

        #endregion
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Models;

namespace NewSchool.Repositories
{
    /// <summary>
    /// SubjectYearPlan Repository
    /// 연간수업계획 기본정보 관리
    /// </summary>
    public class SubjectYearPlanRepository : BaseRepository
    {
        public SubjectYearPlanRepository(string dbPath) : base(dbPath)
        {
            EnsureTableExists();
        }

        #region Table Setup

        private void EnsureTableExists()
        {
            const string sql = @"
                CREATE TABLE IF NOT EXISTS SubjectYearPlan (
                    No INTEGER PRIMARY KEY AUTOINCREMENT,
                    CourseNo INTEGER NOT NULL,
                    TargetGrade INTEGER NOT NULL,
                    TargetClass INTEGER,
                    Year INTEGER NOT NULL,
                    Semester INTEGER NOT NULL,
                    WeeklyHours INTEGER NOT NULL,
                    TotalPlannedHours INTEGER,
                    Status TEXT DEFAULT 'DRAFT',
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    FOREIGN KEY (CourseNo) REFERENCES Course(No) ON DELETE CASCADE,
                    UNIQUE(CourseNo, TargetGrade, TargetClass)
                );

                CREATE INDEX IF NOT EXISTS idx_yearplan_course
                    ON SubjectYearPlan(CourseNo);
                CREATE INDEX IF NOT EXISTS idx_yearplan_status
                    ON SubjectYearPlan(Year, Semester, Status);
            ";

            using var cmd = Connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();

            LogInfo("SubjectYearPlan 테이블 확인/생성 완료");
        }

        #endregion

        #region Create

        /// <summary>
        /// 연간계획 생성
        /// </summary>
        public async Task<int> CreateAsync(SubjectYearPlan plan)
        {
            const string query = @"
                INSERT INTO SubjectYearPlan (
                    CourseNo, TargetGrade, TargetClass, Year, Semester,
                    WeeklyHours, TotalPlannedHours, Status, CreatedAt, UpdatedAt
                ) VALUES (
                    @CourseNo, @TargetGrade, @TargetClass, @Year, @Semester,
                    @WeeklyHours, @TotalPlannedHours, @Status, @CreatedAt, @UpdatedAt
                );
                SELECT last_insert_rowid();";

            try
            {
                plan.CreatedAt = DateTime.Now;
                plan.UpdatedAt = DateTime.Now;

                using var cmd = CreateCommand(query);
                AddParameters(cmd, plan);

                var result = await cmd.ExecuteScalarAsync();
                plan.No = Convert.ToInt32(result);

                LogInfo($"연간계획 생성 완료: No={plan.No}, CourseNo={plan.CourseNo}");
                return plan.No;
            }
            catch (Exception ex)
            {
                LogError($"연간계획 생성 실패: CourseNo={plan.CourseNo}", ex);
                throw;
            }
        }

        #endregion

        #region Read

        /// <summary>
        /// No로 연간계획 조회
        /// </summary>
        public async Task<SubjectYearPlan?> GetByIdAsync(int no)
        {
            const string query = "SELECT * FROM SubjectYearPlan WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@No", no);

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return MapPlan(reader);
                }
                return null;
            }
            catch (Exception ex)
            {
                LogError($"연간계획 조회 실패: No={no}", ex);
                throw;
            }
        }

        /// <summary>
        /// Course + 학급으로 연간계획 조회
        /// </summary>
        public async Task<SubjectYearPlan?> GetByCourseAsync(int courseNo, int targetGrade, int? targetClass)
        {
            var query = @"
                SELECT * FROM SubjectYearPlan
                WHERE CourseNo = @CourseNo
                  AND TargetGrade = @TargetGrade";

            if (targetClass.HasValue)
                query += " AND TargetClass = @TargetClass";
            else
                query += " AND TargetClass IS NULL";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@CourseNo", courseNo);
                cmd.Parameters.AddWithValue("@TargetGrade", targetGrade);
                if (targetClass.HasValue)
                    cmd.Parameters.AddWithValue("@TargetClass", targetClass.Value);

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return MapPlan(reader);
                }
                return null;
            }
            catch (Exception ex)
            {
                LogError($"연간계획 조회 실패: CourseNo={courseNo}, Grade={targetGrade}", ex);
                throw;
            }
        }

        /// <summary>
        /// Course별 연간계획 목록 조회
        /// </summary>
        public async Task<List<SubjectYearPlan>> GetByCourseNoAsync(int courseNo)
        {
            const string query = @"
                SELECT * FROM SubjectYearPlan
                WHERE CourseNo = @CourseNo
                ORDER BY TargetGrade, TargetClass";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@CourseNo", courseNo);

                var plans = new List<SubjectYearPlan>();
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    plans.Add(MapPlan(reader));
                }
                return plans;
            }
            catch (Exception ex)
            {
                LogError($"Course별 연간계획 조회 실패: CourseNo={courseNo}", ex);
                throw;
            }
        }

        /// <summary>
        /// 교사별 연간계획 목록 조회 (Course JOIN)
        /// </summary>
        public async Task<List<SubjectYearPlan>> GetByTeacherAsync(string teacherId, int year, int semester)
        {
            const string query = @"
                SELECT p.* FROM SubjectYearPlan p
                INNER JOIN Course c ON p.CourseNo = c.No
                WHERE c.TeacherID = @TeacherID
                  AND p.Year = @Year
                  AND p.Semester = @Semester
                ORDER BY c.Subject, p.TargetGrade, p.TargetClass";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@TeacherID", teacherId);
                cmd.Parameters.AddWithValue("@Year", year);
                cmd.Parameters.AddWithValue("@Semester", semester);

                var plans = new List<SubjectYearPlan>();
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    plans.Add(MapPlan(reader));
                }
                return plans;
            }
            catch (Exception ex)
            {
                LogError($"교사별 연간계획 조회 실패: TeacherID={teacherId}", ex);
                throw;
            }
        }

        /// <summary>
        /// 상태별 연간계획 목록 조회
        /// </summary>
        public async Task<List<SubjectYearPlan>> GetByStatusAsync(int year, int semester, string status)
        {
            const string query = @"
                SELECT * FROM SubjectYearPlan
                WHERE Year = @Year
                  AND Semester = @Semester
                  AND Status = @Status
                ORDER BY CourseNo, TargetGrade, TargetClass";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@Year", year);
                cmd.Parameters.AddWithValue("@Semester", semester);
                cmd.Parameters.AddWithValue("@Status", status);

                var plans = new List<SubjectYearPlan>();
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    plans.Add(MapPlan(reader));
                }
                return plans;
            }
            catch (Exception ex)
            {
                LogError($"상태별 연간계획 조회 실패: Status={status}", ex);
                throw;
            }
        }

        #endregion

        #region Update

        /// <summary>
        /// 연간계획 수정
        /// </summary>
        public async Task<bool> UpdateAsync(SubjectYearPlan plan)
        {
            const string query = @"
                UPDATE SubjectYearPlan SET
                    CourseNo = @CourseNo,
                    TargetGrade = @TargetGrade,
                    TargetClass = @TargetClass,
                    Year = @Year,
                    Semester = @Semester,
                    WeeklyHours = @WeeklyHours,
                    TotalPlannedHours = @TotalPlannedHours,
                    Status = @Status,
                    UpdatedAt = @UpdatedAt
                WHERE No = @No";

            try
            {
                plan.UpdatedAt = DateTime.Now;

                using var cmd = CreateCommand(query);
                AddParameters(cmd, plan);

                int affected = await cmd.ExecuteNonQueryAsync();
                bool success = affected > 0;

                if (success)
                    LogInfo($"연간계획 수정 완료: No={plan.No}");
                else
                    LogWarning($"연간계획 수정 실패 (not found): No={plan.No}");

                return success;
            }
            catch (Exception ex)
            {
                LogError($"연간계획 수정 실패: No={plan.No}", ex);
                throw;
            }
        }

        /// <summary>
        /// 상태 변경
        /// </summary>
        public async Task<bool> UpdateStatusAsync(int no, string status)
        {
            const string query = @"
                UPDATE SubjectYearPlan SET
                    Status = @Status,
                    UpdatedAt = @UpdatedAt
                WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@No", no);
                cmd.Parameters.AddWithValue("@Status", status);
                cmd.Parameters.AddWithValue("@UpdatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                int affected = await cmd.ExecuteNonQueryAsync();
                bool success = affected > 0;

                if (success)
                    LogInfo($"연간계획 상태 변경 완료: No={no}, Status={status}");

                return success;
            }
            catch (Exception ex)
            {
                LogError($"연간계획 상태 변경 실패: No={no}", ex);
                throw;
            }
        }

        /// <summary>
        /// 총 계획 시수 업데이트
        /// </summary>
        public async Task<bool> UpdateTotalPlannedHoursAsync(int no, int totalPlannedHours)
        {
            const string query = @"
                UPDATE SubjectYearPlan SET
                    TotalPlannedHours = @TotalPlannedHours,
                    UpdatedAt = @UpdatedAt
                WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@No", no);
                cmd.Parameters.AddWithValue("@TotalPlannedHours", totalPlannedHours);
                cmd.Parameters.AddWithValue("@UpdatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                int affected = await cmd.ExecuteNonQueryAsync();
                return affected > 0;
            }
            catch (Exception ex)
            {
                LogError($"총 계획 시수 업데이트 실패: No={no}", ex);
                throw;
            }
        }

        #endregion

        #region Delete

        /// <summary>
        /// 연간계획 삭제
        /// </summary>
        public async Task<bool> DeleteAsync(int no)
        {
            const string query = "DELETE FROM SubjectYearPlan WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@No", no);

                int affected = await cmd.ExecuteNonQueryAsync();
                bool success = affected > 0;

                if (success)
                    LogInfo($"연간계획 삭제 완료: No={no}");
                else
                    LogWarning($"연간계획 삭제 실패 (not found): No={no}");

                return success;
            }
            catch (Exception ex)
            {
                LogError($"연간계획 삭제 실패: No={no}", ex);
                throw;
            }
        }

        /// <summary>
        /// Course별 연간계획 전체 삭제
        /// </summary>
        public async Task<int> DeleteByCourseAsync(int courseNo)
        {
            const string query = "DELETE FROM SubjectYearPlan WHERE CourseNo = @CourseNo";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@CourseNo", courseNo);

                int affected = await cmd.ExecuteNonQueryAsync();
                LogInfo($"Course별 연간계획 삭제 완료: CourseNo={courseNo}, 삭제 수={affected}");
                return affected;
            }
            catch (Exception ex)
            {
                LogError($"Course별 연간계획 삭제 실패: CourseNo={courseNo}", ex);
                throw;
            }
        }

        #endregion

        #region Helper Methods

        private void AddParameters(SqliteCommand cmd, SubjectYearPlan plan)
        {
            cmd.Parameters.AddWithValue("@No", plan.No);
            cmd.Parameters.AddWithValue("@CourseNo", plan.CourseNo);
            cmd.Parameters.AddWithValue("@TargetGrade", plan.TargetGrade);
            cmd.Parameters.AddWithValue("@TargetClass",
                plan.TargetClass.HasValue ? plan.TargetClass.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@Year", plan.Year);
            cmd.Parameters.AddWithValue("@Semester", plan.Semester);
            cmd.Parameters.AddWithValue("@WeeklyHours", plan.WeeklyHours);
            cmd.Parameters.AddWithValue("@TotalPlannedHours",
                plan.TotalPlannedHours.HasValue ? plan.TotalPlannedHours.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@Status", plan.Status ?? "DRAFT");
            cmd.Parameters.AddWithValue("@CreatedAt", plan.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@UpdatedAt", plan.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        }

        private SubjectYearPlan MapPlan(SqliteDataReader reader)
        {
            return new SubjectYearPlan
            {
                No = reader.GetInt32(reader.GetOrdinal("No")),
                CourseNo = reader.GetInt32(reader.GetOrdinal("CourseNo")),
                TargetGrade = reader.GetInt32(reader.GetOrdinal("TargetGrade")),
                TargetClass = reader.IsDBNull(reader.GetOrdinal("TargetClass"))
                    ? null
                    : reader.GetInt32(reader.GetOrdinal("TargetClass")),
                Year = reader.GetInt32(reader.GetOrdinal("Year")),
                Semester = reader.GetInt32(reader.GetOrdinal("Semester")),
                WeeklyHours = reader.GetInt32(reader.GetOrdinal("WeeklyHours")),
                TotalPlannedHours = reader.IsDBNull(reader.GetOrdinal("TotalPlannedHours"))
                    ? null
                    : reader.GetInt32(reader.GetOrdinal("TotalPlannedHours")),
                Status = reader.GetString(reader.GetOrdinal("Status")),
                CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
                UpdatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("UpdatedAt")))
            };
        }

        #endregion
    }
}

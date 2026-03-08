using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Models;

namespace NewSchool.Repositories
{
    /// <summary>
    /// CourseEnrollment Repository
    /// 수강 신청 정보 관리
    /// </summary>
    public class CourseEnrollmentRepository : BaseRepository
    {
        public CourseEnrollmentRepository(string dbPath) : base(dbPath) { }

        #region Create

        /// <summary>
        /// 수강 신청 생성
        /// </summary>
        public async Task<int> CreateAsync(CourseEnrollment enrollment)
        {
            const string query = @"
                INSERT INTO CourseEnrollment (
                    StudentID, CourseNo, Status, Remark, Room, CreatedAt, UpdatedAt
                ) VALUES (
                    @StudentID, @CourseNo, @Status, @Remark, @Room, @CreatedAt, @UpdatedAt
                );
                SELECT last_insert_rowid();";

            try
            {
                using var cmd = CreateCommand(query);
                AddEnrollmentParameters(cmd, enrollment);

                var result = await cmd.ExecuteScalarAsync();
                enrollment.No = Convert.ToInt32(result);

                LogInfo($"수강 신청 생성 완료: No={enrollment.No}, StudentID={enrollment.StudentID}");
                return enrollment.No;
            }
            catch (Exception ex)
            {
                LogError($"수강 신청 생성 실패: StudentID={enrollment.StudentID}", ex);
                throw;
            }
        }

        /// <summary>
        /// 여러 학생 일괄 수강 신청
        /// </summary>
        public async Task<int> BulkCreateAsync(List<CourseEnrollment> enrollments)
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
        /// No로 수강 신청 조회
        /// </summary>
        public async Task<CourseEnrollment?> GetByIdAsync(int no)
        {
            const string query = "SELECT * FROM CourseEnrollment WHERE No = @No";

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
                LogError($"수강 신청 조회 실패: No={no}", ex);
                throw;
            }
        }

        /// <summary>
        /// 과목별 수강 학생 목록 조회
        /// </summary>
        public async Task<List<CourseEnrollment>> GetByCourseAsync(int courseNo)
        {
            const string query = @"
                SELECT * FROM CourseEnrollment 
                WHERE CourseNo = @CourseNo
                ORDER BY StudentID";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@CourseNo", courseNo);

                var enrollments = new List<CourseEnrollment>();
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    enrollments.Add(MapEnrollment(reader));
                }

                return enrollments;
            }
            catch (Exception ex)
            {
                LogError($"과목별 수강 학생 조회 실패: CourseNo={courseNo}", ex);
                throw;
            }
        }

        /// <summary>
        /// 학생별 수강 과목 목록 조회
        /// </summary>
        public async Task<List<CourseEnrollment>> GetByStudentAsync(string studentId)
        {
            const string query = @"
                SELECT * FROM CourseEnrollment 
                WHERE StudentID = @StudentID
                ORDER BY CourseNo";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@StudentID", studentId);

                var enrollments = new List<CourseEnrollment>();
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    enrollments.Add(MapEnrollment(reader));
                }

                return enrollments;
            }
            catch (Exception ex)
            {
                LogError($"학생별 수강 과목 조회 실패: StudentID={studentId}", ex);
                throw;
            }
        }

        /// <summary>
        /// 학생의 특정 학년도/학기 수강 과목 조회
        /// Course와 JOIN하여 조회
        /// </summary>
        public async Task<List<CourseEnrollment>> GetByStudentAndPeriodAsync(
            string studentId, int year, int semester)
        {
            const string query = @"
                SELECT ce.* FROM CourseEnrollment ce
                INNER JOIN Course c ON ce.CourseNo = c.No
                WHERE ce.StudentID = @StudentID
                  AND c.Year = @Year
                  AND c.Semester = @Semester
                ORDER BY c.Grade, c.Class, c.Subject";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@StudentID", studentId);
                cmd.Parameters.AddWithValue("@Year", year);
                cmd.Parameters.AddWithValue("@Semester", semester);

                var enrollments = new List<CourseEnrollment>();
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    enrollments.Add(MapEnrollment(reader));
                }

                return enrollments;
            }
            catch (Exception ex)
            {
                LogError($"학생 수강 과목 조회 실패: StudentID={studentId}, Year={year}, Semester={semester}", ex);
                throw;
            }
        }

        /// <summary>
        /// 중복 수강 신청 확인
        /// </summary>
        public async Task<bool> ExistsAsync(string studentId, int courseNo)
        {
            const string query = @"
                SELECT COUNT(*) FROM CourseEnrollment 
                WHERE StudentID = @StudentID AND CourseNo = @CourseNo";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@StudentID", studentId);
                cmd.Parameters.AddWithValue("@CourseNo", courseNo);

                var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                return count > 0;
            }
            catch (Exception ex)
            {
                LogError($"중복 수강 확인 실패: StudentID={studentId}, CourseNo={courseNo}", ex);
                throw;
            }
        }

        #endregion

        #region Update

        /// <summary>
        /// 수강 신청 정보 수정
        /// </summary>
        public async Task<bool> UpdateAsync(CourseEnrollment enrollment)
        {
            const string query = @"
                UPDATE CourseEnrollment SET
                    StudentID = @StudentID,
                    CourseNo = @CourseNo,
                    Status = @Status,
                    Remark = @Remark,
                    Room = @Room,
                    UpdatedAt = @UpdatedAt
                WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                AddEnrollmentParameters(cmd, enrollment);

                int affected = await cmd.ExecuteNonQueryAsync();
                bool success = affected > 0;

                if (success)
                    LogInfo($"수강 신청 수정 완료: No={enrollment.No}");
                else
                    LogWarning($"수강 신청 수정 실패: No={enrollment.No}");

                return success;
            }
            catch (Exception ex)
            {
                LogError($"수강 신청 수정 실패: No={enrollment.No}", ex);
                throw;
            }
        }

        /// <summary>
        /// 수강 상태 변경
        /// </summary>
        public async Task<bool> UpdateStatusAsync(int no, string status)
        {
            const string query = @"
                UPDATE CourseEnrollment SET
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
                LogError($"수강 상태 변경 실패: No={no}", ex);
                throw;
            }
        }

        #endregion

        #region Delete

        /// <summary>
        /// 수강 신청 취소 (삭제)
        /// </summary>
        public async Task<bool> DeleteAsync(int no)
        {
            const string query = "DELETE FROM CourseEnrollment WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@No", no);

                int affected = await cmd.ExecuteNonQueryAsync();
                bool success = affected > 0;

                if (success)
                    LogInfo($"수강 신청 삭제 완료: No={no}");
                else
                    LogWarning($"수강 신청 삭제 실패: No={no}");

                return success;
            }
            catch (Exception ex)
            {
                LogError($"수강 신청 삭제 실패: No={no}", ex);
                throw;
            }
        }

        /// <summary>
        /// 과목의 모든 수강 신청 삭제
        /// </summary>
        public async Task<int> DeleteByCourseAsync(int courseNo)
        {
            const string query = "DELETE FROM CourseEnrollment WHERE CourseNo = @CourseNo";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@CourseNo", courseNo);

                int affected = await cmd.ExecuteNonQueryAsync();

                LogInfo($"과목 수강 신청 일괄 삭제 완료: CourseNo={courseNo}, 삭제 건수={affected}");
                return affected;
            }
            catch (Exception ex)
            {
                LogError($"과목 수강 신청 일괄 삭제 실패: CourseNo={courseNo}", ex);
                throw;
            }
        }

        #endregion

        #region Helper Methods

        private void AddEnrollmentParameters(SqliteCommand cmd, CourseEnrollment enrollment)
        {
            cmd.Parameters.AddWithValue("@No", enrollment.No);
            cmd.Parameters.AddWithValue("@StudentID", enrollment.StudentID ?? string.Empty);
            cmd.Parameters.AddWithValue("@CourseNo", enrollment.CourseNo);
            cmd.Parameters.AddWithValue("@Status", enrollment.Status ?? "수강중");
            cmd.Parameters.AddWithValue("@Remark", enrollment.Remark ?? string.Empty);
            cmd.Parameters.AddWithValue("@Room", enrollment.Room ?? string.Empty);
            cmd.Parameters.AddWithValue("@CreatedAt", enrollment.CreatedAt);
            cmd.Parameters.AddWithValue("@UpdatedAt", enrollment.UpdatedAt);
        }

        private CourseEnrollment MapEnrollment(SqliteDataReader reader)
        {
            var enrollment = new CourseEnrollment
            {
                No = reader.GetInt32(reader.GetOrdinal("No")),
                StudentID = reader.GetString(reader.GetOrdinal("StudentID")),
                CourseNo = reader.GetInt32(reader.GetOrdinal("CourseNo")),
                Status = reader.GetString(reader.GetOrdinal("Status")),
                Remark = reader.GetString(reader.GetOrdinal("Remark")),
                CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
                UpdatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("UpdatedAt")))
            };

            // Room 컬럼 (기존 DB 호환)
            try
            {
                var roomOrdinal = reader.GetOrdinal("Room");
                enrollment.Room = reader.IsDBNull(roomOrdinal) ? string.Empty : reader.GetString(roomOrdinal);
            }
            catch { enrollment.Room = string.Empty; }

            return enrollment;
        }

        #endregion
    }
}

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
                    EnrollmentNo, CourseNo, Status, Remark, Room
                ) VALUES (
                    @EnrollmentNo, @CourseNo, @Status, @Remark, @Room
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
        /// 단일 트랜잭션 + 파라미터 재사용으로 배치 INSERT
        /// </summary>
        public async Task<int> BulkCreateAsync(List<CourseEnrollment> enrollments)
        {
            if (enrollments == null || enrollments.Count == 0)
                return 0;

            const string query = @"
                INSERT INTO CourseEnrollment (
                    EnrollmentNo, CourseNo, Status, Remark, Room
                ) VALUES (
                    @EnrollmentNo, @CourseNo, @Status, @Remark, @Room
                );
                SELECT last_insert_rowid();";

            try
            {
                BeginTransaction();

                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@EnrollmentNo", 0);
                cmd.Parameters.AddWithValue("@CourseNo", 0);
                cmd.Parameters.AddWithValue("@Status", string.Empty);
                cmd.Parameters.AddWithValue("@Remark", string.Empty);
                cmd.Parameters.AddWithValue("@Room", string.Empty);

                int count = 0;
                foreach (var enrollment in enrollments)
                {
                    cmd.Parameters["@EnrollmentNo"].Value = enrollment.EnrollmentNo;
                    cmd.Parameters["@CourseNo"].Value = enrollment.CourseNo;
                    cmd.Parameters["@Status"].Value = enrollment.Status ?? CourseEnrollmentStatus.Active;
                    cmd.Parameters["@Remark"].Value = enrollment.Remark ?? string.Empty;
                    cmd.Parameters["@Room"].Value = enrollment.Room ?? string.Empty;

                    var result = await cmd.ExecuteScalarAsync();
                    enrollment.No = Convert.ToInt32(result);
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
            const string query = "SELECT * FROM CourseEnrollmentFull WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@No", no);

                using var reader = await cmd.ExecuteReaderAsync();
                var cache = new ReaderColumnCache();
                cache.Initialize(reader);   // 컬럼 인덱스를 행마다 다시 찾지 않도록 한 번만
                if (await reader.ReadAsync())
                {
                    return MapEnrollment(reader, cache);
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
        /// 과목별 수강 학생 목록 조회.
        /// </summary>
        /// <param name="includeInactive">
        /// 전출·졸업으로 명단에서 빠진 학생까지 포함할지. 기본은 아니오다 —
        /// 배정이 학적을 가리키게 되면서 재적 여부가 여기까지 따라온다.
        /// </param>
        public async Task<List<CourseEnrollment>> GetByCourseAsync(int courseNo, bool includeInactive = false)
        {
            string query = $@"
                SELECT * FROM CourseEnrollmentFull
                WHERE CourseNo = @CourseNo
                  {(includeInactive ? string.Empty : "AND IsActive = 1")}
                ORDER BY Grade, Class, Number";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@CourseNo", courseNo);

                var enrollments = new List<CourseEnrollment>();
                using var reader = await cmd.ExecuteReaderAsync();
                var cache = new ReaderColumnCache();
                cache.Initialize(reader);   // 컬럼 인덱스를 행마다 다시 찾지 않도록 한 번만
                while (await reader.ReadAsync())
                {
                    enrollments.Add(MapEnrollment(reader, cache));
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
            // 뷰가 학적을 거쳐 StudentID 를 내주므로 학생 ID 로 묻는 이 질문은 그대로 된다.
            // 여러 학년도 배정이 함께 나오는데, 그게 "이 학생이 그동안 들은 수업" 이라는
            // 이 메서드의 뜻에 맞다.
            const string query = @"
                SELECT * FROM CourseEnrollmentFull
                WHERE StudentID = @StudentID
                ORDER BY Year DESC, CourseNo";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@StudentID", studentId);

                var enrollments = new List<CourseEnrollment>();
                using var reader = await cmd.ExecuteReaderAsync();
                var cache = new ReaderColumnCache();
                cache.Initialize(reader);   // 컬럼 인덱스를 행마다 다시 찾지 않도록 한 번만
                while (await reader.ReadAsync())
                {
                    enrollments.Add(MapEnrollment(reader, cache));
                }

                return enrollments;
            }
            catch (Exception ex)
            {
                LogError($"학생별 수강 과목 조회 실패: StudentID={studentId}", ex);
                throw;
            }
        }

        // 미사용 메서드 제거 (2026-08-19): GetByStudentAndPeriodAsync — 호출처 0건.
        //   ORDER BY 에 Course 테이블에 없는 c.Class 를 걸고 있어 부르는 순간 깨졌을 코드다.

        // 중복 수강 확인(ExistsAsync)은 호출부가 없어 지웠다(39차) —
        // 수강 배정 화면은 현재 명단을 통째로 받아 화면에서 비교한다.

        #endregion

        #region Update

        /// <summary>
        /// 수강 신청 정보 수정
        /// </summary>
        public async Task<bool> UpdateAsync(CourseEnrollment enrollment)
        {
            const string query = @"
                UPDATE CourseEnrollment SET
                    EnrollmentNo = @EnrollmentNo,
                    CourseNo = @CourseNo,
                    Status = @Status,
                    Remark = @Remark,
                    Room = @Room
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
            // StudentID·Name·IsActive 는 넣지 않는다 — 이 표의 컬럼이 아니라
            // CourseEnrollmentFull 뷰가 학적을 거쳐 읽어 오는 값이다.
            cmd.Parameters.AddWithValue("@No", enrollment.No);
            cmd.Parameters.AddWithValue("@EnrollmentNo", enrollment.EnrollmentNo);
            cmd.Parameters.AddWithValue("@CourseNo", enrollment.CourseNo);
            cmd.Parameters.AddWithValue("@Status", enrollment.Status ?? CourseEnrollmentStatus.Active);
            cmd.Parameters.AddWithValue("@Remark", enrollment.Remark ?? string.Empty);
            cmd.Parameters.AddWithValue("@Room", enrollment.Room ?? string.Empty);
        }

        private CourseEnrollment MapEnrollment(SqliteDataReader reader, ReaderColumnCache cache)
        {
            var enrollment = new CourseEnrollment
            {
                No = reader.GetInt32(cache.GetOrdinal("No")),
                EnrollmentNo = reader.GetInt32(cache.GetOrdinal("EnrollmentNo")),
                CourseNo = reader.GetInt32(cache.GetOrdinal("CourseNo")),
                Status = reader.GetString(cache.GetOrdinal("Status")),
                Remark = reader.GetString(cache.GetOrdinal("Remark")),
                // 학적을 거쳐 온 값
                StudentID = reader.GetString(cache.GetOrdinal("StudentID")),
                Name = reader.GetString(cache.GetOrdinal("Name")),
                IsActive = reader.GetInt32(cache.GetOrdinal("IsActive")) == 1,
            };

            // Room 컬럼 (기존 DB 호환)
            try
            {
                var roomOrdinal = cache.GetOrdinal("Room");
                enrollment.Room = reader.IsDBNull(roomOrdinal) ? string.Empty : reader.GetString(roomOrdinal);
            }
            catch { enrollment.Room = string.Empty; }

            return enrollment;
        }

        #endregion
    }
}

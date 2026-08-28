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
                    EnrollmentNo, ClubNo, Status, Remark
                ) VALUES (
                    @EnrollmentNo, @ClubNo, @Status, @Remark
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
            const string query = "SELECT * FROM ClubEnrollmentFull WHERE No = @No";

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
                LogError($"동아리 배정 조회 실패: No={no}", ex);
                throw;
            }
        }

        /// <summary>
        /// 동아리별 학생 목록 조회
        /// </summary>
        /// <param name="includeInactive">
        /// 전출·졸업으로 명단에서 빠진 학생까지 포함할지. 기본은 아니오다 —
        /// 배정이 학적을 가리키게 되면서 재적 여부가 여기까지 따라온다.
        /// </param>
        public async Task<List<ClubEnrollment>> GetByClubAsync(int clubNo, bool includeInactive = false)
        {
            string query = $@"
                SELECT * FROM ClubEnrollmentFull
                WHERE ClubNo = @ClubNo
                  {(includeInactive ? string.Empty : "AND IsActive = 1")}
                ORDER BY Grade, Class, Number";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@ClubNo", clubNo);

                var enrollments = new List<ClubEnrollment>();
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
                LogError($"동아리별 학생 조회 실패: ClubNo={clubNo}", ex);
                throw;
            }
        }

        /// <summary>
        /// 학생별 동아리 목록 조회
        /// </summary>
        public async Task<List<ClubEnrollment>> GetByStudentAsync(string studentId)
        {
            // 뷰가 학적을 거쳐 StudentID 를 내주므로 학생 ID 로 묻는 질문은 그대로 된다.
            const string query = @"
                SELECT * FROM ClubEnrollmentFull
                WHERE StudentID = @StudentID
                ORDER BY Year DESC, ClubNo";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@StudentID", studentId);

                var enrollments = new List<ClubEnrollment>();
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
                LogError($"학생별 동아리 조회 실패: StudentID={studentId}", ex);
                throw;
            }
        }

        // 학년도별 학생 동아리 조회(GetByStudentAndYearAsync)와 중복 배정 확인(ExistsAsync)은
        // 호출부가 없어 지웠다(39차). 배정 화면은 동아리 기준 목록을 받아 화면에서 비교한다.


        #endregion

        #region Update

        /// <summary>
        /// 동아리 배정 정보 수정
        /// </summary>
        public async Task<bool> UpdateAsync(ClubEnrollment enrollment)
        {
            const string query = @"
                UPDATE ClubEnrollment SET
                    EnrollmentNo = @EnrollmentNo,
                    ClubNo = @ClubNo,
                    Status = @Status,
                    Remark = @Remark
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
                    Status = @Status
                WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@No", no);
                cmd.Parameters.AddWithValue("@Status", status);

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

        // 동아리 배정 일괄 삭제(DeleteByClubAsync)는 호출부가 없어 지웠다(39차).

        #endregion

        #region Helper Methods

        private void AddEnrollmentParameters(SqliteCommand cmd, ClubEnrollment enrollment)
        {
            // StudentID·Name·IsActive 는 넣지 않는다 — 이 표의 컬럼이 아니라
            // ClubEnrollmentFull 뷰가 학적을 거쳐 읽어 오는 값이다.
            cmd.Parameters.AddWithValue("@No", enrollment.No);
            cmd.Parameters.AddWithValue("@EnrollmentNo", enrollment.EnrollmentNo);
            cmd.Parameters.AddWithValue("@ClubNo", enrollment.ClubNo);
            cmd.Parameters.AddWithValue("@Status", enrollment.Status ?? ClubEnrollmentStatus.Active);
            cmd.Parameters.AddWithValue("@Remark", enrollment.Remark ?? string.Empty);
        }

        private ClubEnrollment MapEnrollment(SqliteDataReader reader, ReaderColumnCache cache)
        {
            return new ClubEnrollment
            {
                No = reader.GetInt32(cache.GetOrdinal("No")),
                EnrollmentNo = reader.GetInt32(cache.GetOrdinal("EnrollmentNo")),
                ClubNo = reader.GetInt32(cache.GetOrdinal("ClubNo")),
                Status = reader.GetString(cache.GetOrdinal("Status")),
                Remark = reader.IsDBNull(cache.GetOrdinal("Remark"))
                    ? string.Empty
                    : reader.GetString(cache.GetOrdinal("Remark")),
                // 학적을 거쳐 온 값
                StudentID = reader.GetString(cache.GetOrdinal("StudentID")),
                Name = reader.GetString(cache.GetOrdinal("Name")),
                IsActive = reader.GetInt32(cache.GetOrdinal("IsActive")) == 1,
            };
        }

        #endregion
    }
}

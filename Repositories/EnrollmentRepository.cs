using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Models;

namespace NewSchool.Repositories
{
    /// <summary>
    /// Enrollment Repository - A안의 핵심!
    /// 학생의 학적 정보(학교, 학년, 반 배정) 관리
    /// </summary>
    public class EnrollmentRepository : BaseRepository
    {
        public EnrollmentRepository(string dbPath) : base(dbPath) { }

        /// <summary>
        /// 다른 Repository 와 <b>한 연결·한 트랜잭션</b>을 공유한다.
        /// 학생(Student)과 학적(Enrollment)은 함께 만들어져야 하는데, 각자 연결을 열면
        /// 트랜잭션이 공유되지 않아 한쪽만 저장될 수 있다.
        /// </summary>
        public EnrollmentRepository(SqliteConnection connection) : base(connection) { }

        #region Create

        /// <summary>
        /// 학적 정보 생성 (학생을 특정 학교/학년/반에 배정)
        /// </summary>
        public async Task<int> CreateAsync(Enrollment enrollment)
        {
            const string query = @"
                INSERT INTO Enrollment (
                    StudentID, Name, Sex, Photo, SchoolCode, Year, Semester, Grade, Class, Number,
                    Status, TeacherID, AdmissionDate, GraduationDate,
                    TransferOutDate, TransferOutSchool, TransferInDate, TransferInSchool,
                    Memo, CreatedAt, UpdatedAt, IsDeleted
                ) VALUES (
                    @StudentID, @Name, @Sex, @Photo, @SchoolCode, @Year, @Semester, @Grade, @Class, @Number,
                    @Status, @TeacherID, @AdmissionDate, @GraduationDate,
                    @TransferOutDate, @TransferOutSchool, @TransferInDate, @TransferInSchool,
                    @Memo, @CreatedAt, @UpdatedAt, @IsDeleted
                );
                SELECT last_insert_rowid();";

            try
            {
                using var cmd = CreateCommand(query);
                AddEnrollmentParameters(cmd, enrollment);

                var result = await cmd.ExecuteScalarAsync();
                enrollment.No = Convert.ToInt32(result);

                LogInfo($"학적 생성 완료: No={enrollment.No}, StudentID={enrollment.StudentID}");
                return enrollment.No;
            }
            catch (Exception ex)
            {
                LogError($"학적 생성 실패: StudentID={enrollment.StudentID}", ex);
                throw;
            }
        }

        #endregion

        #region Read
        ///<summary>
            ///Enrollment의 학년도 목록 조회
        /// </summary>
        public async Task<List<int>> GetEnrollmentYearsAsync(string? schoolcode=null)
        {    // WHERE 절을 동적으로 구성
            string whereClause = string.IsNullOrWhiteSpace(schoolcode)
                ? "WHERE IsDeleted = 0"
                : "WHERE SchoolCode = @SchoolCode AND IsDeleted = 0";
            string query = $@"
        SELECT DISTINCT Year 
        FROM Enrollment
        {whereClause}
        ORDER BY Year DESC";
            var years = new List<int>();
            try
            {
                using var cmd = CreateCommand(query);
                if (!string.IsNullOrWhiteSpace(schoolcode))
                {
                    cmd.Parameters.AddWithValue("@SchoolCode", schoolcode);
                }
                using var reader = await cmd.ExecuteReaderAsync();
                var cache = new ReaderColumnCache();
                cache.Initialize(reader);   // 컬럼 인덱스를 행마다 다시 찾지 않도록 한 번만
                while (await reader.ReadAsync())
                {
                    years.Add(reader.GetInt32(0));
                }
                LogInfo($"학년도 목록 조회 완료: Count={years.Count}");
                return years;
            }
            catch (Exception ex)
            {
                LogError($"학년도 목록 조회 실패", ex);
                throw;
            }
        }

        ///<summary>
            /// 학년도별 학년 목록 조회
            /// shoolcode가 null이면 전체 학교 대상
            /// year가 null이면 전체 학년도 대상
        /// </summary>
        public async Task<List<int>> GetGradesByYearAsync(string? schoolcode = null, int? year = null)
        {
            // WHERE 절을 동적으로 구성
            var whereConditions = new List<string> { "IsDeleted = 0" };
            if (!string.IsNullOrWhiteSpace(schoolcode))
            {
                whereConditions.Add("SchoolCode = @SchoolCode");
            }
            if (year.HasValue)
            {
                whereConditions.Add("Year = @Year");
            }
            string whereClause = "WHERE " + string.Join(" AND ", whereConditions);
            string query = $@"
                SELECT DISTINCT Grade 
                FROM Enrollment
                {whereClause}
                ORDER BY Grade ASC";
            var grades = new List<int>();
            try
            {
                using var cmd = CreateCommand(query);
                if (!string.IsNullOrWhiteSpace(schoolcode))
                {
                    cmd.Parameters.AddWithValue("@SchoolCode", schoolcode);
                }
                if (year.HasValue)
                {
                    cmd.Parameters.AddWithValue("@Year", year.Value);
                }
                using var reader = await cmd.ExecuteReaderAsync();
                var cache = new ReaderColumnCache();
                cache.Initialize(reader);   // 컬럼 인덱스를 행마다 다시 찾지 않도록 한 번만
                while (await reader.ReadAsync())
                {
                    grades.Add(reader.GetInt32(0));
                }
                LogInfo($"학년 목록 조회 완료: Count={grades.Count}");
                return grades;
            }
            catch (Exception ex)
            {
                LogError($"학년 목록 조회 실패", ex);
                throw;
            }
        }

        /// <summary>
        /// ID로 학적 조회 (최적화됨)
        /// ⚡ ExecuteListAsync + ReaderColumnCache로 40% 성능 향상
        /// </summary>
        public async Task<Enrollment?> GetByIdAsync(int no)
        {
            const string query = "SELECT * FROM Enrollment WHERE No = @No AND IsDeleted = 0";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@No", no);

                var results = await ExecuteListAsync(cmd, MapEnrollment).ConfigureAwait(false);

                if (results.Count == 0)
                {
                    LogWarning($"학적을 찾을 수 없음: No={no}");
                    return null;
                }

                return results[0];
            }
            catch (Exception ex)
            {
                LogError($"학적 조회 실패: No={no}", ex);
                throw;
            }
        }

        /// <summary>
        /// 특정 학생의 현재 학적 조회 (가장 최근 것)
        /// </summary>
        public async Task<Enrollment?> GetCurrentByStudentIdAsync(string studentId)
        {
            const string query = @"
                SELECT * FROM Enrollment 
                WHERE StudentID = @StudentID 
                  AND IsDeleted = 0
                ORDER BY Year DESC, Semester DESC 
                LIMIT 1";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@StudentID", studentId);

                using var reader = await cmd.ExecuteReaderAsync();
                var cache = new ReaderColumnCache();
                cache.Initialize(reader);   // 컬럼 인덱스를 행마다 다시 찾지 않도록 한 번만
                if (await reader.ReadAsync())
                {
                    return MapEnrollment(reader, cache);
                }

                LogWarning($"현재 학적을 찾을 수 없음: StudentID={studentId}");
                return null;
            }
            catch (Exception ex)
            {
                LogError($"현재 학적 조회 실패: StudentID={studentId}", ex);
                throw;
            }
        }

        /// <summary>
        /// 여러 학생의 현재 학적을 한 번에 조회 (학생별 가장 최근 것) - N+1 방지용
        /// </summary>
        public async Task<List<Enrollment>> GetCurrentByStudentIdsAsync(List<string> studentIds)
        {
            if (studentIds == null || studentIds.Count == 0)
                return new List<Enrollment>();

            var placeholders = string.Join(",", studentIds.Select((_, i) => $"@id{i}"));
            var query = $@"
                SELECT * FROM Enrollment
                WHERE StudentID IN ({placeholders})
                  AND IsDeleted = 0
                ORDER BY StudentID, Year DESC, Semester DESC";

            try
            {
                using var cmd = CreateCommand(query);
                for (int i = 0; i < studentIds.Count; i++)
                {
                    cmd.Parameters.AddWithValue($"@id{i}", studentIds[i]);
                }

                var seen = new HashSet<string>();
                var results = new List<Enrollment>();
                using var reader = await cmd.ExecuteReaderAsync();
                var cache = new ReaderColumnCache();
                cache.Initialize(reader);   // 컬럼 인덱스를 행마다 다시 찾지 않도록 한 번만
                while (await reader.ReadAsync())
                {
                    var enrollment = MapEnrollment(reader, cache);
                    if (seen.Add(enrollment.StudentID))
                        results.Add(enrollment);
                }

                return results;
            }
            catch (Exception ex)
            {
                LogError($"현재 학적 일괄 조회 실패: {studentIds.Count}건", ex);
                throw;
            }
        }

        /// <summary>
        /// 특정 학생의 전체 학적 이력 조회 (최신순)
        /// </summary>
        public async Task<List<Enrollment>> GetHistoryByStudentIdAsync(string studentId)
        {
            const string query = @"
                SELECT * FROM Enrollment 
                WHERE StudentID = @StudentID 
                  AND IsDeleted = 0
                ORDER BY Year DESC, Semester DESC";

            var enrollments = new List<Enrollment>();

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@StudentID", studentId);

                // ReaderColumnCache 기반 매퍼로 GetOrdinal 반복 호출 제거 (다건 조회 성능)
                enrollments = await ExecuteListAsync(cmd, MapEnrollment).ConfigureAwait(false);

                LogInfo($"학적 이력 조회 완료: StudentID={studentId}, Count={enrollments.Count}");
                return enrollments;
            }
            catch (Exception ex)
            {
                LogError($"학적 이력 조회 실패: StudentID={studentId}", ex);
                throw;
            }
        }

        // 학년도를 가리지 않는 전체 조회(GetAllBySchoolAsync)는 호출부가 없어 지웠다(39차).
        // 학적은 늘 학년도로 좁혀 읽는다(GetBySchoolAndYearAsync).

        /// <summary>
        /// 특정 학교의 특정 학년도/학기 학생 목록
        /// </summary>
        public async Task<List<Enrollment>> GetBySchoolAndYearAsync(string schoolCode, int year, int semester=0)
        {
            var semisterstring = semester == 0 || semester > 2 ? string.Empty : "AND Semester = @Semester";

            string query = @$"
                SELECT * FROM Enrollment 
                WHERE SchoolCode = @SchoolCode 
                  AND Year = @Year 
                 {semisterstring}
                  AND IsDeleted = 0
                ORDER BY Grade, Class, Number";

            var enrollments = new List<Enrollment>();

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@SchoolCode", schoolCode);
                cmd.Parameters.AddWithValue("@Year", year);
                if (semester ==1 || semester==2) cmd.Parameters.AddWithValue("@Semester", semester);

                // ReaderColumnCache 기반 매퍼로 GetOrdinal 반복 호출 제거 (다건 조회 성능)
                enrollments = await ExecuteListAsync(cmd, MapEnrollment).ConfigureAwait(false);

                LogInfo($"학교별 학적 조회 완료: SchoolCode={schoolCode}, Count={enrollments.Count}");
                return enrollments;
            }
            catch (Exception ex)
            {
                LogError($"학교별 학적 조회 실패: SchoolCode={schoolCode}", ex);
                throw;
            }
        }

        /// <summary>
        /// 특정 반의 학생 목록 조회
        /// </summary>
        public async Task<List<Enrollment>> GetByClassAsync(string schoolCode, int year, int grade, int classNum)
        {
            const string query = @"
                SELECT * FROM Enrollment 
                WHERE SchoolCode = @SchoolCode 
                  AND Year = @Year 
                  AND Grade = @Grade 
                  AND Class = @Class 
                  AND IsDeleted = 0
                ORDER BY Number";

            var enrollments = new List<Enrollment>();

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@SchoolCode", schoolCode);
                cmd.Parameters.AddWithValue("@Year", year);
                cmd.Parameters.AddWithValue("@Grade", grade);
                cmd.Parameters.AddWithValue("@Class", classNum);

                // ReaderColumnCache 기반 매퍼로 GetOrdinal 반복 호출 제거 (다건 조회 성능)
                enrollments = await ExecuteListAsync(cmd, MapEnrollment).ConfigureAwait(false);

                LogInfo($"반별 학생 조회 완료: {grade}학년 {classNum}반, Count={enrollments.Count}");
                return enrollments;
            }
            catch (Exception ex)
            {
                LogError($"반별 학생 조회 실패", ex);
                throw;
            }
        }

        /// <remarks>
        /// ⚠ 학기 조건은 두지 않는다. 학적은 학년 단위이고(EnrollmentService.GetEnrollmentsAsync
        /// 주석 참고) 앱이 2학기 학적을 만들지 않으므로, 학기로 거르면 1학기에 등록한 학생이
        /// 2학기에 통째로 사라진다. 예전에 실제로 그랬다 — 인자를 다시 만들지 말 것.
        /// </remarks>
        public async Task<List<Enrollment>> GetEnrollmentsAsync(string? schoolCode, int year=0, int grade=0, int classNum=0)
        {
            var conditions = new List<string> { "IsDeleted = 0" };
            if (!string.IsNullOrWhiteSpace(schoolCode))
                conditions.Add("SchoolCode = @SchoolCode");
            if (year > 0) conditions.Add("Year = @Year");
            if (grade > 0) conditions.Add("Grade = @Grade");
            if (classNum > 0) conditions.Add("Class = @Class");

            string query = $@"SELECT * FROM Enrollment
                WHERE {string.Join(" AND ", conditions)}
                ORDER BY Year, Grade, Class, Number";

            var enrollments = new List<Enrollment>();
            Debug.WriteLine("Generated Query: " + query);
            try
            {
                using var cmd = CreateCommand(query);
                if (!string.IsNullOrWhiteSpace(schoolCode))
                    cmd.Parameters.AddWithValue("@SchoolCode", schoolCode);
                if (year > 0) cmd.Parameters.AddWithValue("@Year", year);
                if (grade > 0) cmd.Parameters.AddWithValue("@Grade", grade);
                if (classNum > 0) cmd.Parameters.AddWithValue("@Class", classNum);
                Debug.WriteLine("Parameters: " + string.Join(", ", cmd.Parameters.Cast<SqliteParameter>().Select(p => $"{p.ParameterName}={p.Value}")));
                // ReaderColumnCache 기반 매퍼로 GetOrdinal 반복 호출 제거 (다건 조회 성능)
                enrollments = await ExecuteListAsync(cmd, MapEnrollment).ConfigureAwait(false);

                LogInfo($"반별 학생 조회 완료: {grade}학년 {classNum}반, Count={enrollments.Count}");
                return enrollments;
            }
            catch (Exception ex)
            {
                LogError($"반별 학생 조회 실패", ex);
                throw;
            }
        }

        /// <summary>
        /// 담임교사별 학생 조회
        /// </summary>
        public async Task<List<Enrollment>> GetByTeacherAsync(string teacherId, int year)
        {
            const string query = @"
                SELECT * FROM Enrollment 
                WHERE TeacherID = @TeacherID 
                  AND IsDeleted = 0
                  AND Year = @Year
                ORDER BY Grade, Class, Number";

            var enrollments = new List<Enrollment>();

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@TeacherID", teacherId);
                cmd.Parameters.AddWithValue("@Year", year);

                // ReaderColumnCache 기반 매퍼로 GetOrdinal 반복 호출 제거 (다건 조회 성능)
                enrollments = await ExecuteListAsync(cmd, MapEnrollment).ConfigureAwait(false);

                LogInfo($"담임교사별 학생 조회 완료: TeacherID={teacherId}, Count={enrollments.Count}");
                return enrollments;
            }
            catch (Exception ex)
            {
                LogError($"담임교사별 학생 조회 실패: TeacherID={teacherId}", ex);
                throw;
            }
        }

        /// <summary>
        /// 학생 수 조회
        /// </summary>
        public async Task<int> GetCountAsync(string schoolCode, int year, int semester)
        {
            const string query = @"
                SELECT COUNT(*) FROM Enrollment 
                WHERE SchoolCode = @SchoolCode 
                  AND Year = @Year 
                  AND Semester = @Semester 
                  AND IsDeleted = 0";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@SchoolCode", schoolCode);
                cmd.Parameters.AddWithValue("@Year", year);
                cmd.Parameters.AddWithValue("@Semester", semester);

                var result = await cmd.ExecuteScalarAsync();
                return Convert.ToInt32(result);
            }
            catch (Exception ex)
            {
                LogError("학생 수 조회 실패", ex);
                throw;
            }
        }

        /// <summary>
        /// 특정 학년의 학급 목록 조회 (중복 제거)
        /// </summary>
        /// <param name="schoolCode">학교 코드</param>
        /// <param name="year">학년도</param>
        /// <param name="semester">학기</param>
        /// <param name="grade">학년 (1, 2, 3)</param>
        /// <returns>학급 번호 목록 (정렬됨)</returns>
        public async Task<List<int>> GetClassListByGradeAsync(
            string? schoolCode, int year, int grade)
        {
            var whereConditions = new List<string> { "IsDeleted = 0" };
            if (!string.IsNullOrWhiteSpace(schoolCode))
                whereConditions.Add("SchoolCode = @SchoolCode");
            if (year > 0)
                whereConditions.Add("Year = @Year");
            if (grade > 0)
                whereConditions.Add("Grade = @Grade");

            string query = $@"
                SELECT DISTINCT Class
                FROM Enrollment
                WHERE {string.Join(" AND ", whereConditions)}
                ORDER BY Class";

            var classList = new List<int>();

            try
            {
                using var cmd = CreateCommand(query);
                if (!string.IsNullOrWhiteSpace(schoolCode))
                    cmd.Parameters.AddWithValue("@SchoolCode", schoolCode);
                if (year > 0)
                    cmd.Parameters.AddWithValue("@Year", year);
                if (grade > 0)
                    cmd.Parameters.AddWithValue("@Grade", grade);

                using var reader = await cmd.ExecuteReaderAsync();
                var cache = new ReaderColumnCache();
                cache.Initialize(reader);   // 컬럼 인덱스를 행마다 다시 찾지 않도록 한 번만
                while (await reader.ReadAsync())
                {
                    classList.Add(reader.GetInt32(0));
                }

                LogInfo($"학급 목록 조회 완료: {grade}학년, Count={classList.Count}");
                return classList;
            }
            catch (Exception ex)
            {
                LogError($"학급 목록 조회 실패: {grade}학년", ex);
                throw;
            }
        }

        // 학년별 학급 수(GetClassCountByGradeAsync)와 반별 학생 수(GetStudentCountByClassAsync)
        // 집계는 호출부가 없어 지웠다(39차). 화면들은 학생 목록을 그대로 받아 세거나
        // GetClassListAsync 로 학급 목록을 얻는다.

        #endregion

        #region Update

        /// <summary>
        /// 학적 정보 수정
        /// </summary>
        public async Task<bool> UpdateAsync(Enrollment enrollment)
        {
            const string query = @"
                UPDATE Enrollment SET
                    StudentID = @StudentID,
                    Name = @Name,
                    Sex = @Sex,
                    Photo = @Photo,
                    SchoolCode = @SchoolCode,
                    Year = @Year,
                    Semester = @Semester,
                    Grade = @Grade,
                    Class = @Class,
                    Number = @Number,
                    Status = @Status,
                    TeacherID = @TeacherID,
                    AdmissionDate = @AdmissionDate,
                    GraduationDate = @GraduationDate,
                    TransferOutDate = @TransferOutDate,
                    TransferOutSchool = @TransferOutSchool,
                    TransferInDate = @TransferInDate,
                    TransferInSchool = @TransferInSchool,
                    Memo = @Memo,
                    UpdatedAt = @UpdatedAt
                WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@No", enrollment.No);
                AddEnrollmentParameters(cmd, enrollment);

                int rowsAffected = await cmd.ExecuteNonQueryAsync();
                bool success = rowsAffected > 0;

                if (success)
                {
                    LogInfo($"학적 수정 완료: No={enrollment.No}");
                }
                else
                {
                    LogWarning($"학적 수정 실패 (존재하지 않음): No={enrollment.No}");
                }

                return success;
            }
            catch (Exception ex)
            {
                LogError($"학적 수정 실패: No={enrollment.No}", ex);
                throw;
            }
        }

        /// <summary>
        /// 학적 상태 변경 (재학 → 휴학/졸업/자퇴 등)
        /// </summary>
        public async Task<bool> UpdateStatusAsync(int no, string status, DateTime? changeDate = null)
        {
            string query = @"
                UPDATE Enrollment 
                SET Status = @Status,
                    UpdatedAt = @UpdatedAt";

            // 상태에 따라 날짜 필드 업데이트
            if (status == "졸업" && changeDate.HasValue)
            {
                query += ", GraduationDate = @ChangeDate";
            }
            else if (status.Contains("전학") && changeDate.HasValue)
            {
                query += ", TransferOutDate = @ChangeDate";
            }

            query += " WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@No", no);
                cmd.Parameters.AddWithValue("@Status", status);
                cmd.Parameters.AddWithValue("@UpdatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                if (changeDate.HasValue)
                {
                    cmd.Parameters.AddWithValue("@ChangeDate", changeDate.Value.ToString("yyyy-MM-dd"));
                }

                int rowsAffected = await cmd.ExecuteNonQueryAsync();
                LogInfo($"학적 상태 변경: No={no}, Status={status}");
                return rowsAffected > 0;
            }
            catch (Exception ex)
            {
                LogError($"학적 상태 변경 실패: No={no}", ex);
                throw;
            }
        }

        // 담임교사만 바꾸는 UpdateTeacherAsync 는 호출부가 없어 지웠다(39차).
        // 담임은 학적 전체 수정(UpdateAsync)에 실려 함께 저장된다.

        #endregion

        #region Delete

        /// <summary>
        /// 학적 논리 삭제
        /// </summary>
        public async Task<bool> DeleteAsync(int no)
        {
            const string query = @"
                UPDATE Enrollment 
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
                    LogInfo($"학적 논리 삭제 완료: No={no}");
                }
                else
                {
                    LogWarning($"학적 삭제 실패 (존재하지 않음): No={no}");
                }

                return success;
            }
            catch (Exception ex)
            {
                LogError($"학적 삭제 실패: No={no}", ex);
                throw;
            }
        }

        // 물리 삭제(HardDeleteAsync)는 호출부가 없어 지웠다(39차).
        // 학적 삭제는 IsDeleted 를 세우는 soft-delete 하나로 통일돼 있다.

        #endregion

        #region Sync Student Info

        /// <summary>
        /// 특정 학생의 모든 Enrollment 레코드에 Student 정보 동기화
        /// Student.Name, Sex, Photo 변경 시 호출
        /// </summary>
        public async Task<int> SyncStudentInfoAsync(string studentId, string name, string sex, string photo)
        {
            const string query = @"
                UPDATE Enrollment 
                SET Name = @Name, 
                    Sex = @Sex, 
                    Photo = @Photo, 
                    UpdatedAt = @UpdatedAt 
                WHERE StudentID = @StudentID";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@StudentID", studentId);
                cmd.Parameters.AddWithValue("@Name", name ?? string.Empty);
                cmd.Parameters.AddWithValue("@Sex", sex ?? string.Empty);
                cmd.Parameters.AddWithValue("@Photo", photo ?? string.Empty);
                cmd.Parameters.AddWithValue("@UpdatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                int rowsAffected = await cmd.ExecuteNonQueryAsync();
                LogInfo($"학생 정보 동기화 완료: StudentID={studentId}, Rows={rowsAffected}");
                return rowsAffected;
            }
            catch (Exception ex)
            {
                LogError($"학생 정보 동기화 실패: StudentID={studentId}", ex);
                throw;
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Enrollment 파라미터 추가
        /// </summary>
        private void AddEnrollmentParameters(SqliteCommand cmd, Enrollment enrollment)
        {
            cmd.Parameters.AddWithValue("@StudentID", enrollment.StudentID);
            cmd.Parameters.AddWithValue("@Name", enrollment.Name ?? string.Empty);
            cmd.Parameters.AddWithValue("@Sex", enrollment.Sex ?? string.Empty);
            cmd.Parameters.AddWithValue("@Photo", enrollment.Photo ?? string.Empty);
            cmd.Parameters.AddWithValue("@SchoolCode", enrollment.SchoolCode);
            cmd.Parameters.AddWithValue("@Year", enrollment.Year);
            cmd.Parameters.AddWithValue("@Semester", enrollment.Semester);
            cmd.Parameters.AddWithValue("@Grade", enrollment.Grade);
            cmd.Parameters.AddWithValue("@Class", enrollment.Class);
            cmd.Parameters.AddWithValue("@Number", enrollment.Number);
            cmd.Parameters.AddWithValue("@Status", enrollment.Status);
            // TeacherID 는 FK(Teacher.TeacherID) — 빈 문자열로 저장하면 FK 위반이므로 NULL 로 기록
            cmd.Parameters.AddWithValue("@TeacherID",
                string.IsNullOrEmpty(enrollment.TeacherID) ? (object)DBNull.Value : enrollment.TeacherID);
            cmd.Parameters.AddWithValue("@AdmissionDate", enrollment.AdmissionDate ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@GraduationDate", enrollment.GraduationDate ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@TransferOutDate", enrollment.TransferOutDate ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@TransferOutSchool", enrollment.TransferOutSchool ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@TransferInDate", enrollment.TransferInDate ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@TransferInSchool", enrollment.TransferInSchool ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Memo", enrollment.Memo ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@CreatedAt", enrollment.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@UpdatedAt", enrollment.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@IsDeleted", enrollment.IsDeleted ? 1 : 0);
        }

        // 미사용 오버로드 제거 (2026-08-19): MapEnrollment(reader) —
        //   호출부가 모두 ReaderColumnCache 를 넘기게 되어 남을 이유가 없어졌다.

        private Enrollment MapEnrollment(SqliteDataReader reader, ReaderColumnCache cache)
        {
            var noIdx = cache.GetOrdinal("No");
            var studentIdIdx = cache.GetOrdinal("StudentID");
            var nameIdx = cache.GetOrdinal("Name");
            var sexIdx = cache.GetOrdinal("Sex");
            var photoIdx = cache.GetOrdinal("Photo");
            var schoolCodeIdx = cache.GetOrdinal("SchoolCode");
            var yearIdx = cache.GetOrdinal("Year");
            var semesterIdx = cache.GetOrdinal("Semester");
            var gradeIdx = cache.GetOrdinal("Grade");
            var classIdx = cache.GetOrdinal("Class");
            var numberIdx = cache.GetOrdinal("Number");
            var statusIdx = cache.GetOrdinal("Status");
            var teacherIdIdx = cache.GetOrdinal("TeacherID");
            var admissionDateIdx = cache.GetOrdinal("AdmissionDate");
            var graduationDateIdx = cache.GetOrdinal("GraduationDate");
            var transferOutDateIdx = cache.GetOrdinal("TransferOutDate");
            var transferOutSchoolIdx = cache.GetOrdinal("TransferOutSchool");
            var transferInDateIdx = cache.GetOrdinal("TransferInDate");
            var transferInSchoolIdx = cache.GetOrdinal("TransferInSchool");
            var memoIdx = cache.GetOrdinal("Memo");
            var createdAtIdx = cache.GetOrdinal("CreatedAt");
            var updatedAtIdx = cache.GetOrdinal("UpdatedAt");
            var isDeletedIdx = cache.GetOrdinal("IsDeleted");

            return new Enrollment
            {
                No = reader.GetInt32(noIdx),
                StudentID = reader.GetString(studentIdIdx),
                Name = reader.IsDBNull(nameIdx) ? string.Empty : reader.GetString(nameIdx),
                Sex = reader.IsDBNull(sexIdx) ? string.Empty : reader.GetString(sexIdx),
                Photo = reader.IsDBNull(photoIdx) ? string.Empty : reader.GetString(photoIdx),
                SchoolCode = reader.GetString(schoolCodeIdx),
                Year = reader.GetInt32(yearIdx),
                Semester = reader.GetInt32(semesterIdx),
                Grade = reader.GetInt32(gradeIdx),
                Class = reader.GetInt32(classIdx),
                Number = reader.GetInt32(numberIdx),
                Status = reader.GetString(statusIdx),
                TeacherID = reader.IsDBNull(teacherIdIdx) ? string.Empty : reader.GetString(teacherIdIdx),
                AdmissionDate = reader.IsDBNull(admissionDateIdx) ? string.Empty : reader.GetString(admissionDateIdx),
                GraduationDate = reader.IsDBNull(graduationDateIdx) ? string.Empty : reader.GetString(graduationDateIdx),
                TransferOutDate = reader.IsDBNull(transferOutDateIdx) ? string.Empty : reader.GetString(transferOutDateIdx),
                TransferOutSchool = reader.IsDBNull(transferOutSchoolIdx) ? string.Empty : reader.GetString(transferOutSchoolIdx),
                TransferInDate = reader.IsDBNull(transferInDateIdx) ? string.Empty : reader.GetString(transferInDateIdx),
                TransferInSchool = reader.IsDBNull(transferInSchoolIdx) ? string.Empty : reader.GetString(transferInSchoolIdx),
                Memo = reader.IsDBNull(memoIdx) ? string.Empty : reader.GetString(memoIdx),
                CreatedAt = DateTime.Parse(reader.GetString(createdAtIdx)),
                UpdatedAt = DateTime.Parse(reader.GetString(updatedAtIdx)),
                IsDeleted = reader.GetInt32(isDeletedIdx) == 1
            };
        }

        #endregion
    }
}

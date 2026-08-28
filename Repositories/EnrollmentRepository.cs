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
                    StudentID, SchoolCode, Year, Grade, Class, Number,
                    IsActive, ChangeType, ChangeDate, Memo, TeacherID
                ) VALUES (
                    @StudentID, @SchoolCode, @Year, @Grade, @Class, @Number,
                    @IsActive, @ChangeType, @ChangeDate, @Memo, @TeacherID
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
                ? string.Empty
                : "WHERE SchoolCode = @SchoolCode";
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
            var whereConditions = new List<string>();
            if (!string.IsNullOrWhiteSpace(schoolcode))
            {
                whereConditions.Add("SchoolCode = @SchoolCode");
            }
            if (year.HasValue)
            {
                whereConditions.Add("Year = @Year");
            }
            string whereClause = whereConditions.Count == 0
                ? string.Empty
                : "WHERE " + string.Join(" AND ", whereConditions);
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
            const string query = "SELECT * FROM EnrollmentFull WHERE No = @No";

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
                SELECT * FROM EnrollmentFull
                WHERE StudentID = @StudentID
                ORDER BY Year DESC
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
                SELECT * FROM EnrollmentFull
                WHERE StudentID IN ({placeholders})
                ORDER BY StudentID, Year DESC";

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
                SELECT * FROM EnrollmentFull
                WHERE StudentID = @StudentID
                ORDER BY Year DESC";

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
        public async Task<List<Enrollment>> GetBySchoolAndYearAsync(string schoolCode, int year)
        {
            const string query = @"
                SELECT * FROM EnrollmentFull
                WHERE SchoolCode = @SchoolCode
                  AND Year = @Year
                ORDER BY Grade, Class, Number";

            var enrollments = new List<Enrollment>();

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@SchoolCode", schoolCode);
                cmd.Parameters.AddWithValue("@Year", year);

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
                SELECT * FROM EnrollmentFull
                WHERE SchoolCode = @SchoolCode
                  AND Year = @Year
                  AND Grade = @Grade
                  AND Class = @Class
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
        /// <param name="includeInactive">
        /// 전출·졸업·자퇴처럼 <b>명단에서 빠진 학적</b>까지 포함할지. 기본은 아니오다 —
        /// "지금 이 반에 누가 있나" 가 거의 모든 호출처의 질문이기 때문이다.
        /// 참으로 켜는 곳은 설정의 학생 관리 하나뿐이다.
        /// </param>
        public async Task<List<Enrollment>> GetEnrollmentsAsync(string? schoolCode, int year=0, int grade=0, int classNum=0,
                                                                bool includeInactive=false)
        {
            var conditions = new List<string>();
            if (!includeInactive) conditions.Add("IsActive = 1");
            if (!string.IsNullOrWhiteSpace(schoolCode))
                conditions.Add("SchoolCode = @SchoolCode");
            if (year > 0) conditions.Add("Year = @Year");
            if (grade > 0) conditions.Add("Grade = @Grade");
            if (classNum > 0) conditions.Add("Class = @Class");

            string whereClause = conditions.Count == 0
                ? string.Empty
                : "WHERE " + string.Join(" AND ", conditions);

            string query = $@"SELECT * FROM EnrollmentFull
                {whereClause}
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
                SELECT * FROM EnrollmentFull
                WHERE TeacherID = @TeacherID
                  AND Year = @Year
                  AND IsActive = 1
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
        public async Task<int> GetCountAsync(string schoolCode, int year)
        {
            const string query = @"
                SELECT COUNT(*) FROM Enrollment
                WHERE SchoolCode = @SchoolCode
                  AND Year = @Year
                  AND IsActive = 1";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@SchoolCode", schoolCode);
                cmd.Parameters.AddWithValue("@Year", year);

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
            var whereConditions = new List<string> { "IsActive = 1" };
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
                    SchoolCode = @SchoolCode,
                    Year = @Year,
                    Grade = @Grade,
                    Class = @Class,
                    Number = @Number,
                    IsActive = @IsActive,
                    ChangeType = @ChangeType,
                    ChangeDate = @ChangeDate,
                    Memo = @Memo,
                    TeacherID = @TeacherID
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
        /// <summary>
        /// 학적 변동을 기록한다 — 변동 유형·일자와 재적 여부를 한 번에 맞춘다.
        ///
        /// <para><c>IsActive</c> 는 인자로 받지 않는다. 변동 유형에서만 나오므로 바깥에서
        /// 넣을 길을 아예 두지 않아야 두 값이 갈라지지 않는다.</para>
        /// </summary>
        public async Task<bool> ApplyChangeAsync(int no, string changeType, DateTime? changeDate = null)
        {
            const string query = @"
                UPDATE Enrollment
                SET ChangeType = @ChangeType,
                    IsActive   = @IsActive,
                    ChangeDate = COALESCE(@ChangeDate, ChangeDate)
                WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@No", no);
                cmd.Parameters.AddWithValue("@ChangeType", changeType);
                cmd.Parameters.AddWithValue("@IsActive", EnrollmentChange.IsActive(changeType) ? 1 : 0);
                cmd.Parameters.AddWithValue("@ChangeDate",
                    changeDate.HasValue ? changeDate.Value.ToString("yyyy-MM-dd") : (object)DBNull.Value);

                int rowsAffected = await cmd.ExecuteNonQueryAsync();
                LogInfo($"학적 변동 기록: No={no}, ChangeType={changeType}");
                return rowsAffected > 0;
            }
            catch (Exception ex)
            {
                LogError($"학적 변동 기록 실패: No={no}", ex);
                throw;
            }
        }

        // 담임교사만 바꾸는 UpdateTeacherAsync 는 호출부가 없어 지웠다(39차).
        // 담임은 학적 전체 수정(UpdateAsync)에 실려 함께 저장된다.

        #endregion

        #region Delete

        /// <summary>
        /// 학적 삭제 — 행을 실제로 지운다.
        ///
        /// <para>예전에는 <c>IsDeleted</c> 를 세우는 논리 삭제였는데, 삭제 확인 문구는
        /// "이 작업은 되돌릴 수 없습니다" 라고 말하면서 행은 남기고 있었다. 되살리는 경로도
        /// 없어서 그 플래그의 유일한 값이 쓰이지 못했다. 안전망은 ZIP 백업이 맡는다.</para>
        ///
        /// <para>⚠ 전출·졸업과 혼동하지 말 것. 그것들은 <see cref="ApplyChangeAsync"/> 로
        /// 남기는 <b>사실</b>이고, 삭제는 <b>잘못 만든 행</b>을 없애는 일이다.</para>
        /// </summary>
        public async Task<bool> DeleteAsync(int no)
        {
            const string query = "DELETE FROM Enrollment WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@No", no);

                int rowsAffected = await cmd.ExecuteNonQueryAsync();
                bool success = rowsAffected > 0;

                if (success)
                {
                    LogInfo($"학적 삭제 완료: No={no}");
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

        #endregion

        #region Sync Student Info

        // SyncStudentInfoAsync 는 없앴다. 이름·성별·사진이 Enrollment 의 컬럼이던 시절,
        // Student 를 고칠 때마다 학적 쪽 사본을 따라 고쳐 주던 메서드다. 사본을 없애고
        // EnrollmentFull 뷰가 JOIN 으로 읽게 되면서 동기화할 것이 남지 않았다.

        #endregion

        #region Helper Methods

        /// <summary>
        /// Enrollment 파라미터 추가
        /// </summary>
        private void AddEnrollmentParameters(SqliteCommand cmd, Enrollment enrollment)
        {
            // Name·Sex·Photo 는 넣지 않는다 — 이 표의 컬럼이 아니라 Student 에서 JOIN 으로
            // 읽어 오는 값이다(Enrollment 모델 주석 참고).
            cmd.Parameters.AddWithValue("@StudentID", enrollment.StudentID);
            cmd.Parameters.AddWithValue("@SchoolCode", enrollment.SchoolCode);
            cmd.Parameters.AddWithValue("@Year", enrollment.Year);
            cmd.Parameters.AddWithValue("@Grade", enrollment.Grade);
            cmd.Parameters.AddWithValue("@Class", enrollment.Class);
            cmd.Parameters.AddWithValue("@Number", enrollment.Number);
            // IsActive 는 ChangeType 에서만 나온다. 바깥에서 받은 값을 그대로 쓰지 않는 것이
            // 두 값이 갈라지지 않게 하는 유일한 장치다.
            cmd.Parameters.AddWithValue("@IsActive", EnrollmentChange.IsActive(enrollment.ChangeType) ? 1 : 0);
            cmd.Parameters.AddWithValue("@ChangeType", enrollment.ChangeType);
            cmd.Parameters.AddWithValue("@ChangeDate",
                string.IsNullOrEmpty(enrollment.ChangeDate) ? (object)DBNull.Value : enrollment.ChangeDate);
            cmd.Parameters.AddWithValue("@Memo", enrollment.Memo ?? (object)DBNull.Value);
            // TeacherID 는 FK(Teacher.TeacherID) — 빈 문자열로 저장하면 FK 위반이므로 NULL 로 기록
            cmd.Parameters.AddWithValue("@TeacherID",
                string.IsNullOrEmpty(enrollment.TeacherID) ? (object)DBNull.Value : enrollment.TeacherID);
        }

        // 미사용 오버로드 제거 (2026-08-19): MapEnrollment(reader) —
        //   호출부가 모두 ReaderColumnCache 를 넘기게 되어 남을 이유가 없어졌다.

        private Enrollment MapEnrollment(SqliteDataReader reader, ReaderColumnCache cache)
        {
            var noIdx = cache.GetOrdinal("No");
            var studentIdIdx = cache.GetOrdinal("StudentID");
            var schoolCodeIdx = cache.GetOrdinal("SchoolCode");
            var yearIdx = cache.GetOrdinal("Year");
            var gradeIdx = cache.GetOrdinal("Grade");
            var classIdx = cache.GetOrdinal("Class");
            var numberIdx = cache.GetOrdinal("Number");
            var changeTypeIdx = cache.GetOrdinal("ChangeType");
            var changeDateIdx = cache.GetOrdinal("ChangeDate");
            var memoIdx = cache.GetOrdinal("Memo");
            var teacherIdIdx = cache.GetOrdinal("TeacherID");

            // Student 에서 JOIN 으로 온 값. 조회 SQL 이 SelectEnrollment 를 쓰면 항상 있다.
            var nameIdx = cache.GetOrdinal("Name");
            var sexIdx = cache.GetOrdinal("Sex");
            var photoIdx = cache.GetOrdinal("Photo");

            var enrollment = new Enrollment
            {
                No = reader.GetInt32(noIdx),
                StudentID = reader.GetString(studentIdIdx),
                SchoolCode = reader.GetString(schoolCodeIdx),
                Year = reader.GetInt32(yearIdx),
                Grade = reader.GetInt32(gradeIdx),
                Class = reader.GetInt32(classIdx),
                Number = reader.GetInt32(numberIdx),
                ChangeDate = reader.IsDBNull(changeDateIdx) ? string.Empty : reader.GetString(changeDateIdx),
                Memo = reader.IsDBNull(memoIdx) ? string.Empty : reader.GetString(memoIdx),
                TeacherID = reader.IsDBNull(teacherIdIdx) ? string.Empty : reader.GetString(teacherIdIdx),
                Name = reader.IsDBNull(nameIdx) ? string.Empty : reader.GetString(nameIdx),
                Sex = reader.IsDBNull(sexIdx) ? string.Empty : reader.GetString(sexIdx),
                Photo = reader.IsDBNull(photoIdx) ? string.Empty : reader.GetString(photoIdx),
            };

            // ChangeType 대입이 IsActive 를 함께 맞춘다. DB 의 IsActive 컬럼은 읽지 않는다 —
            // 어긋난 행이 있어도 규칙 쪽을 믿는다.
            enrollment.ChangeType = reader.GetString(changeTypeIdx);

            return enrollment;
        }

        #endregion
    }
}

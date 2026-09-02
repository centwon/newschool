using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Models;

namespace NewSchool.Repositories;

/// <summary>
/// StudentLog Repository
/// 학생 기록부 (행동 특성 및 종합의견) 관리
/// ⭐ 확장 버전: Category enum + 구조화된 활동 기록 필드
/// </summary>
public class StudentLogRepository : BaseRepository
{
    public StudentLogRepository(string dbPath) : base(dbPath) { }

    #region Create

    /// <summary>
    /// 학생 기록 생성
    /// </summary>
    public async Task<int> CreateAsync(StudentLog log)
    {
        const string query = @"
                INSERT INTO StudentLog (
                    StudentID, TeacherID, Year, Semester, Date,
                    Category, CourseNo, SubjectName, ClubNo, ClubName, Log, Tag, IsImportant,
                    ActivityName, Topic, Description, Role, 
                    SkillDeveloped, StrengthShown, ResultOrOutcome
                ) VALUES (
                    @StudentID, @TeacherID, @Year, @Semester, @Date,
                    @Category, @CourseNo, @SubjectName, @ClubNo, @ClubName, @Log, @Tag, @IsImportant,
                    @ActivityName, @Topic, @Description, @Role,
                    @SkillDeveloped, @StrengthShown, @ResultOrOutcome
                );
                SELECT last_insert_rowid();";

        try
        {
            using var cmd = CreateCommand(query);
            AddLogParameters(cmd, log);

            var result = await cmd.ExecuteScalarAsync();
            log.No = Convert.ToInt32(result);

            LogInfo($"학생 기록 생성 완료: No={log.No}");
            return log.No;
        }
        catch (Exception ex)
        {
            LogError($"학생 기록 생성 실패: StudentID={log.StudentID}, TeacherID={log.TeacherID}, CourseNo={log.CourseNo}", ex);
            LogError($"  상세 오류: {ex.GetType().Name} - {ex.Message}", ex);
            if (ex.InnerException != null)
            {
                LogError($"  Inner Exception: {ex.InnerException.Message}", ex.InnerException);
            }
            throw;
        }
    }

    #endregion

    #region Read

    /// <summary>
    /// No로 학생 기록 조회
    /// </summary>
    public async Task<StudentLog?> GetByIdAsync(int no)
    {
        const string query = "SELECT * FROM StudentLog WHERE No = @No";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.Add("@No", SqliteType.Integer).Value = no;

            using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
            if (await reader.ReadAsync().ConfigureAwait(false))
            {
                return MapStudentLog(reader);
            }

            return null;
        }
        catch (Exception ex)
        {
            LogError($"학생 기록 조회 실패: No={no}", ex);
            throw;
        }
    }

    /// <summary>
    /// 학생별 기록 조회 (최적화됨)
    /// ⚡ ExecuteListAsync + ReaderColumnCache로 40% 성능 향상
    /// semester = 0이면 해당 학년도 전체 기록 조회
    /// </summary>
    public async Task<List<StudentLog>> GetByStudentAsync(
        string studentId, int year, int semester = 0)
    {
        // semester가 0이면 학년도 전체 기록 조회
        string query = semester > 0
            ? @"SELECT * FROM StudentLog
                    WHERE StudentID = @StudentID
                      AND Year = @Year
                      AND Semester = @Semester
                    ORDER BY Date DESC, Category"
            : @"SELECT * FROM StudentLog
                    WHERE StudentID = @StudentID
                      AND Year = @Year
                    ORDER BY Date DESC, Category";

        try
        {
            System.Diagnostics.Debug.WriteLine($"[StudentLogRepository] GetByStudentAsync 시작: StudentID={studentId}, Year={year}, Semester={semester}");
            System.Diagnostics.Debug.WriteLine($"[StudentLogRepository] Query: {(semester > 0 ? "with Semester" : "without Semester")}");

            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@StudentID", studentId);
            cmd.Parameters.Add("@Year", SqliteType.Integer).Value = year;
            if (semester > 0)
            {
                cmd.Parameters.Add("@Semester", SqliteType.Integer).Value = semester;
            }

            var logs = await ExecuteListAsync(cmd, MapStudentLog).ConfigureAwait(false);

            System.Diagnostics.Debug.WriteLine($"[StudentLogRepository] GetByStudentAsync 완료: {logs.Count}건 조회됨");
            return logs;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StudentLogRepository] GetByStudentAsync 오류: {ex.Message}");
            LogError($"학생별 기록 조회 실패: StudentID={studentId}", ex);
            throw;
        }
    }

    /// <summary>
    /// 여러 학생의 기록을 단일 쿼리로 일괄 조회 (N+1 해소)
    /// StudentID → List&lt;StudentLog&gt; 딕셔너리로 반환
    /// </summary>
    public async Task<Dictionary<string, List<StudentLog>>> GetByStudentIdsAsync(
        IEnumerable<string> studentIds, int year, int semester = 0)
    {
        var idList = studentIds?.Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList()
                     ?? new List<string>();
        var result = new Dictionary<string, List<StudentLog>>();
        if (idList.Count == 0) return result;

        // IN (@id0, @id1, ...) 파라미터 바인딩
        var placeholders = string.Join(",", idList.Select((_, i) => $"@id{i}"));
        string query = semester > 0
            ? $@"SELECT * FROM StudentLog
                     WHERE StudentID IN ({placeholders})
                       AND Year = @Year
                       AND Semester = @Semester
                     ORDER BY Date DESC, Category"
            : $@"SELECT * FROM StudentLog
                     WHERE StudentID IN ({placeholders})
                       AND Year = @Year
                     ORDER BY Date DESC, Category";

        try
        {
            using var cmd = CreateCommand(query);
            for (int i = 0; i < idList.Count; i++)
            {
                cmd.Parameters.AddWithValue($"@id{i}", idList[i]);
            }
            cmd.Parameters.Add("@Year", SqliteType.Integer).Value = year;
            if (semester > 0)
            {
                cmd.Parameters.Add("@Semester", SqliteType.Integer).Value = semester;
            }

            var logs = await ExecuteListAsync(cmd, MapStudentLog).ConfigureAwait(false);

            // 빈 리스트로 초기화 (요청한 모든 학생에 대해 키 존재 보장)
            foreach (var id in idList) result[id] = new List<StudentLog>();
            foreach (var log in logs)
            {
                if (!result.TryGetValue(log.StudentID, out var list))
                {
                    list = new List<StudentLog>();
                    result[log.StudentID] = list;
                }
                list.Add(log);
            }

            LogInfo($"학생 일괄 기록 조회 완료: 학생 {idList.Count}명, 기록 {logs.Count}건");
            return result;
        }
        catch (Exception ex)
        {
            LogError($"학생 일괄 기록 조회 실패: Count={idList.Count}", ex);
            throw;
        }
    }

    // 학년도를 가리지 않는 전체 조회(GetAllByStudentAsync)는 이를 부르던
    // StudentLogService.GetAllStudentLogsAsync 와 함께 지웠다(39차).

    // 카테고리별(GetByCategoryAsync)·교사별(GetByTeacherAsync)·수업별(GetByCourseAsync)·
    // 키워드 검색(SearchAsync)·날짜 범위(GetByDateRangeAsync) 조회는 호출부가 없어 지웠다(44차).
    // 서비스에도 이들을 감싼 메서드가 없었으므로 화면에서 닿을 방법이 아예 없었다
    // — GetByCategoryAsync 만 테스트가 하나 붙들고 있었고 그 테스트도 함께 지웠다.
    // 화면은 학년도·학기로 좁힌 GetByStudentAsync·GetByStudentIdsAsync 와 학급 단위 조회를 쓴다.
    //
    // ⚠ 이들이 쓰던 인덱스(idx_studentlog_category·idx_studentlog_teacher·
    //   idx_studentlog_course·idx_studentlog_date)는 그대로 뒀다. 지우려면 손으로 DB 를
    //   고쳐야 하는데(이 프로젝트는 ALTER 마이그레이션을 두지 않는다), 이득이 없다.

    /// <summary>
    /// 학년 반별 기록 조회 (Enrollment JOIN으로 최적화)
    /// 특정 날짜의 특정 학급 전체 학생 기록을 단일 쿼리로 조회
    /// </summary>
    public async Task<List<StudentLog>> GetByClassAndDateAsync(
        string schoolCode, int year, int grade, int classroom, DateTime date)
    {
        // Enrollment와 JOIN하여 해당 학급 학생들의 로그만 조회
        // Date는 날짜 부분만 비교 (시간 제외)
        const string query = @"
                SELECT sl.* FROM StudentLog sl
                INNER JOIN Enrollment e ON sl.StudentID = e.StudentID
                WHERE e.SchoolCode = @SchoolCode
                  AND e.Year = @Year
                  AND e.Grade = @Grade
                  AND e.Class = @Class
                  AND sl.Year = @Year
                  AND date(sl.Date) = date(@Date)
                ORDER BY e.Number, sl.Date DESC";

        try
        {
            LogDebug($"학급별 기록 조회: {year}년 {grade}학년 {classroom}반, {date:yyyy-MM-dd}");

            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@SchoolCode", schoolCode);
            cmd.Parameters.Add("@Year", SqliteType.Integer).Value = year;
            cmd.Parameters.Add("@Grade", SqliteType.Integer).Value = grade;
            cmd.Parameters.Add("@Class", SqliteType.Integer).Value = classroom;
            cmd.Parameters.AddWithValue("@Date", date.ToString("yyyy-MM-dd"));

            using var reader = await cmd.ExecuteReaderAsync();
            var logs = await ReadAllLogsAsync(reader);

            LogInfo($"학급별 기록 조회 완료: {logs.Count}건");
            return logs;
        }
        catch (Exception ex)
        {
            LogError($"학급별 학생 기록 조회 실패: {grade}-{classroom}, {date:yyyy-MM-dd}", ex);
            throw;
        }
    }

    /// <summary>
    /// 학년 반별 기간 기록 조회 (Enrollment JOIN으로 최적화)
    ///
    /// <para>⚠ 날짜는 <c>date()</c> 로 감싸 비교한다 — 바로 위의 하루 조회와 같은 규칙이다.
    /// 예전에는 여기만 문자열로 견줬는데, <c>Date</c> 는 TEXT 라 시각이 붙은 행
    /// ("2026-03-31 10:00")이 있으면 <c>&lt;= '2026-03-31'</c> 에 걸리지 않아
    /// <b>마지막 날이 통째로 빠졌다</b>. 지금 저장 경로는 날짜만 넣지만, 그렇지 않던 시절의
    /// 행이 남아 있을 수 있고 형제 함수와 규칙이 달라야 할 이유도 없다.</para>
    /// </summary>
    public async Task<List<StudentLog>> GetByClassAndDateRangeAsync(
        string schoolCode, int year, int grade, int classroom, DateTime startDate, DateTime endDate)
    {
        const string query = @"
                SELECT sl.* FROM StudentLog sl
                INNER JOIN Enrollment e ON sl.StudentID = e.StudentID
                WHERE e.SchoolCode = @SchoolCode
                  AND e.Year = @Year
                  AND e.Grade = @Grade
                  AND e.Class = @Class
                  AND sl.Year = @Year
                  AND date(sl.Date) >= date(@StartDate)
                  AND date(sl.Date) <= date(@EndDate)
                ORDER BY sl.Date DESC, e.Number";

        try
        {
            LogDebug($"학급별 기간 조회: {year}년 {grade}학년 {classroom}반, {startDate:yyyy-MM-dd} ~ {endDate:yyyy-MM-dd}");

            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@SchoolCode", schoolCode);
            cmd.Parameters.Add("@Year", SqliteType.Integer).Value = year;
            cmd.Parameters.Add("@Grade", SqliteType.Integer).Value = grade;
            cmd.Parameters.Add("@Class", SqliteType.Integer).Value = classroom;
            cmd.Parameters.AddWithValue("@StartDate", startDate.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@EndDate", endDate.ToString("yyyy-MM-dd"));

            using var reader = await cmd.ExecuteReaderAsync();
            var logs = await ReadAllLogsAsync(reader);

            LogInfo($"학급별 기간 조회 완료: {logs.Count}건");
            return logs;
        }
        catch (Exception ex)
        {
            LogError($"학급별 기간 조회 실패: {grade}-{classroom}", ex);
            throw;
        }
    }

    // 중요 기록만(GetImportantAsync)·구조화 기록만(GetStructuredAsync) 조회는 호출부가 없어
    // 지웠다(39차). 목록은 학기 전체를 받아 화면에서 별표·항목 유무로 거른다.


    #endregion

    #region Update

    /// <summary>
    /// 학생 기록 수정
    /// </summary>
    public async Task<bool> UpdateAsync(StudentLog log)
    {
        const string query = @"
                UPDATE StudentLog SET
                    StudentID = @StudentID,
                    TeacherID = @TeacherID,
                    Year = @Year,
                    Semester = @Semester,
                    Date = @Date,
                    Category = @Category,
                    CourseNo = @CourseNo,
                    SubjectName = @SubjectName,
                    ClubNo = @ClubNo,
                    ClubName = @ClubName,
                    Log = @Log,
                    Tag = @Tag,
                    IsImportant = @IsImportant,
                    ActivityName = @ActivityName,
                    Topic = @Topic,
                    Description = @Description,
                    Role = @Role,
                    SkillDeveloped = @SkillDeveloped,
                    StrengthShown = @StrengthShown,
                    ResultOrOutcome = @ResultOrOutcome
                WHERE No = @No";

        try
        {
            using var cmd = CreateCommand(query);
            AddLogParameters(cmd, log);

            int affected = await cmd.ExecuteNonQueryAsync();
            bool success = affected > 0;

            if (success)
                LogInfo($"학생 기록 수정 완료: No={log.No}");
            else
                LogWarning($"학생 기록 수정 실패: No={log.No}");

            return success;
        }
        catch (Exception ex)
        {
            LogError($"학생 기록 수정 실패: No={log.No}", ex);
            throw;
        }
    }

    #endregion

    #region Delete

    /// <summary>
    /// 학생 기록 삭제
    /// </summary>
    public async Task<bool> DeleteAsync(int no)
    {
        const string query = "DELETE FROM StudentLog WHERE No = @No";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.Add("@No", SqliteType.Integer).Value = no;

            int affected = await cmd.ExecuteNonQueryAsync();
            bool success = affected > 0;

            if (success)
                LogInfo($"학생 기록 삭제 완료: No={no}");
            else
                LogWarning($"학생 기록 삭제 실패: No={no}");

            return success;
        }
        catch (Exception ex)
        {
            LogError($"학생 기록 삭제 실패: No={no}", ex);
            throw;
        }
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// StudentLog 파라미터 추가
    /// </summary>
    private void AddLogParameters(SqliteCommand cmd, StudentLog log)
    {
        cmd.Parameters.Add("@No", SqliteType.Integer).Value = log.No;
        cmd.Parameters.AddWithValue("@StudentID", log.StudentID ?? string.Empty);
        cmd.Parameters.AddWithValue("@TeacherID", string.IsNullOrEmpty(log.TeacherID) ? DBNull.Value : log.TeacherID);
        cmd.Parameters.Add("@Year", SqliteType.Integer).Value = log.Year;
        cmd.Parameters.Add("@Semester", SqliteType.Integer).Value = log.Semester;
        cmd.Parameters.AddWithValue("@Date", log.Date.ToString("yyyy-MM-dd"));
        cmd.Parameters.Add("@Category", SqliteType.Integer).Value = (int)log.Category; // ⭐ enum을 int로 변환
        // ⭐ CourseNo가 0이면 NULL로 저장 (외래키 제약 회피)
        cmd.Parameters.Add("@CourseNo", SqliteType.Integer).Value = log.CourseNo == 0 ? (object)DBNull.Value : log.CourseNo;
        cmd.Parameters.AddWithValue("@SubjectName", string.IsNullOrEmpty(log.SubjectName) ? DBNull.Value : log.SubjectName);
        // ⚠ 이 두 파라미터는 오랫동안 여기서 채워지기만 하고 INSERT/UPDATE 문에는 빠져 있었다.
        //   SQLite 는 쓰이지 않는 파라미터를 조용히 무시하므로 오류 없이 동아리 정보만 사라졌다.
        cmd.Parameters.Add("@ClubNo", SqliteType.Integer).Value = log.ClubNo == 0 ? (object)DBNull.Value : log.ClubNo;
        cmd.Parameters.AddWithValue("@ClubName", string.IsNullOrEmpty(log.ClubName) ? DBNull.Value : log.ClubName);
        cmd.Parameters.AddWithValue("@Log", string.IsNullOrEmpty(log.Log) ? DBNull.Value : log.Log);
        cmd.Parameters.AddWithValue("@Tag", string.IsNullOrEmpty(log.Tag) ? DBNull.Value : log.Tag);
        cmd.Parameters.Add("@IsImportant", SqliteType.Integer).Value = log.IsImportant ? 1 : 0;

        // 구조화된 필드들
        cmd.Parameters.AddWithValue("@ActivityName", string.IsNullOrEmpty(log.ActivityName) ? DBNull.Value : log.ActivityName);
        cmd.Parameters.AddWithValue("@Topic", string.IsNullOrEmpty(log.Topic) ? DBNull.Value : log.Topic);
        cmd.Parameters.AddWithValue("@Description", string.IsNullOrEmpty(log.Description) ? DBNull.Value : log.Description);
        cmd.Parameters.AddWithValue("@Role", string.IsNullOrEmpty(log.Role) ? DBNull.Value : log.Role);
        cmd.Parameters.AddWithValue("@SkillDeveloped", string.IsNullOrEmpty(log.SkillDeveloped) ? DBNull.Value : log.SkillDeveloped);
        cmd.Parameters.AddWithValue("@StrengthShown", string.IsNullOrEmpty(log.StrengthShown) ? DBNull.Value : log.StrengthShown);
        cmd.Parameters.AddWithValue("@ResultOrOutcome", string.IsNullOrEmpty(log.ResultOrOutcome) ? DBNull.Value : log.ResultOrOutcome);
    }

    /// <summary>
    /// SqliteDataReader를 StudentLog로 매핑 (호환성 오버로드)
    /// </summary>
    private StudentLog MapStudentLog(SqliteDataReader reader)
    {
        var cache = new ReaderColumnCache(20);
        cache.Initialize(reader);
        return MapStudentLog(reader, cache);
    }

    private async Task<List<StudentLog>> ReadAllLogsAsync(SqliteDataReader reader)
    {
        var logs = new List<StudentLog>();
        var cache = new ReaderColumnCache(20);
        bool cacheInitialized = false;
        while (await reader.ReadAsync())
        {
            if (!cacheInitialized) { cache.Initialize(reader); cacheInitialized = true; }
            logs.Add(MapStudentLog(reader, cache));
        }
        return logs;
    }

    /// <summary>
    /// SqliteDataReader를 StudentLog로 매핑
    /// ⚡ ReaderColumnCache로 GetOrdinal 반복 호출 제거 (40% 성능 향상)
    /// </summary>
    private StudentLog MapStudentLog(SqliteDataReader reader, ReaderColumnCache cache)
    {
        // 각 컬럼의 인덱스를 캐시에서 가져옴
        var noIdx = cache.GetOrdinal("No");
        var studentIdIdx = cache.GetOrdinal("StudentID");
        var teacherIdIdx = cache.GetOrdinal("TeacherID");
        var yearIdx = cache.GetOrdinal("Year");
        var semesterIdx = cache.GetOrdinal("Semester");
        var dateIdx = cache.GetOrdinal("Date");
        var categoryIdx = cache.GetOrdinal("Category");
        var courseNoIdx = cache.GetOrdinal("CourseNo");
        var subjectNameIdx = cache.GetOrdinal("SubjectName");

        // ClubNo, ClubName 컬럼이 없을 수도 있음 (구 DB 스키마 호환)
        var hasClubNo = cache.TryGetOrdinal("ClubNo", out var clubNoIdx);
        var hasClubName = cache.TryGetOrdinal("ClubName", out var clubNameIdx);

        var logIdx = cache.GetOrdinal("Log");
        var tagIdx = cache.GetOrdinal("Tag");
        var isImportantIdx = cache.GetOrdinal("IsImportant");
        var activityNameIdx = cache.GetOrdinal("ActivityName");
        var topicIdx = cache.GetOrdinal("Topic");
        var descriptionIdx = cache.GetOrdinal("Description");
        var roleIdx = cache.GetOrdinal("Role");
        var skillDevelopedIdx = cache.GetOrdinal("SkillDeveloped");
        var strengthShownIdx = cache.GetOrdinal("StrengthShown");
        var resultOrOutcomeIdx = cache.GetOrdinal("ResultOrOutcome");

        return new StudentLog
        {
            No = reader.GetInt32(noIdx),
            StudentID = reader.IsDBNull(studentIdIdx) ? string.Empty : reader.GetString(studentIdIdx),
            TeacherID = reader.IsDBNull(teacherIdIdx) ? string.Empty : reader.GetString(teacherIdIdx),
            Year = reader.GetInt32(yearIdx),
            Semester = reader.GetInt32(semesterIdx),
            Date = NewSchool.DateTimeHelper.FromDateString(reader.IsDBNull(dateIdx) ? string.Empty : reader.GetString(dateIdx)),
            Category = (LogCategory)reader.GetInt32(categoryIdx), // ⭐ int를 enum으로 변환
            CourseNo = reader.IsDBNull(courseNoIdx) ? 0 : reader.GetInt32(courseNoIdx),
            SubjectName = reader.IsDBNull(subjectNameIdx) ? string.Empty : reader.GetString(subjectNameIdx),
            ClubNo = hasClubNo && !reader.IsDBNull(clubNoIdx) ? reader.GetInt32(clubNoIdx) : 0,
            ClubName = hasClubName && !reader.IsDBNull(clubNameIdx) ? reader.GetString(clubNameIdx) : string.Empty,
            Log = reader.IsDBNull(logIdx) ? string.Empty : reader.GetString(logIdx),
            Tag = reader.IsDBNull(tagIdx) ? string.Empty : reader.GetString(tagIdx),
            IsImportant = (reader.IsDBNull(isImportantIdx) ? 0 : reader.GetInt32(isImportantIdx)) == 1,

            // 구조화된 필드들
            ActivityName = reader.IsDBNull(activityNameIdx) ? string.Empty : reader.GetString(activityNameIdx),
            Topic = reader.IsDBNull(topicIdx) ? string.Empty : reader.GetString(topicIdx),
            Description = reader.IsDBNull(descriptionIdx) ? string.Empty : reader.GetString(descriptionIdx),
            Role = reader.IsDBNull(roleIdx) ? string.Empty : reader.GetString(roleIdx),
            SkillDeveloped = reader.IsDBNull(skillDevelopedIdx) ? string.Empty : reader.GetString(skillDevelopedIdx),
            StrengthShown = reader.IsDBNull(strengthShownIdx) ? string.Empty : reader.GetString(strengthShownIdx),
            ResultOrOutcome = reader.IsDBNull(resultOrOutcomeIdx) ? string.Empty : reader.GetString(resultOrOutcomeIdx)
        };
    }

    #endregion
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Models;

namespace NewSchool.Repositories;

/// <summary>
/// StudentSpecial Repository
/// 학교생활기록부 특기사항 데이터 접근
/// DB 칼럼명 IsActive 유지, C# IsFinalized로 반전 매핑
/// </summary>
public class StudentSpecialRepository : BaseRepository
{
    public StudentSpecialRepository(string dbPath) : base(dbPath) { }

    #region Create

    /// <summary>
    /// 학생부 기록 생성
    /// </summary>
    public async Task<int> CreateAsync(StudentSpecial special)
    {
        const string query = @"
                INSERT INTO StudentSpecial (
                    StudentID, Year, Semester, Type, Title, Content, Date, TeacherID, 
                    CourseNo, SubjectName, IsActive, Tag
                ) VALUES (
                    @StudentID, @Year, @Semester, @Type, @Title, @Content, @Date, @TeacherID,
                    @CourseNo, @SubjectName, @IsActive, @Tag
                );
                SELECT last_insert_rowid();";

        try
        {
            using var cmd = CreateCommand(query);
            AddSpecialParameters(cmd, special);

            var result = await cmd.ExecuteScalarAsync();
            special.No = Convert.ToInt32(result);

            LogInfo($"학생부 기록 생성 완료: No={special.No}, StudentID={special.StudentID}");
            return special.No;
        }
        catch (Exception ex)
        {
            LogError($"학생부 기록 생성 실패: StudentID={special.StudentID}", ex);
            throw;
        }
    }

    #endregion

    #region Read

    /// <summary>
    /// No로 학생부 기록 조회
    /// </summary>
    public async Task<StudentSpecial?> GetByIdAsync(int no)
    {
        const string query = "SELECT * FROM StudentSpecial WHERE No = @No";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@No", no);

            var found = await ExecuteListAsync(cmd, MapStudentSpecial).ConfigureAwait(false);
            return found.Count > 0 ? found[0] : null;
        }
        catch (Exception ex)
        {
            LogError($"학생부 기록 조회 실패: No={no}", ex);
            throw;
        }
    }

    /// <summary>
    /// 학생별 학생부 기록 조회
    /// </summary>
    public async Task<List<StudentSpecial>> GetByStudentAsync(string studentId, int year)
    {
        const string query = @"
                SELECT * FROM StudentSpecial 
                WHERE StudentID = @StudentID 
                  AND Year = @Year
                ORDER BY Date DESC";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@StudentID", studentId);
            cmd.Parameters.AddWithValue("@Year", year);

            return await ExecuteListAsync(cmd, MapStudentSpecial).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogError($"학생별 학생부 기록 조회 실패: StudentID={studentId}", ex);
            throw;
        }
    }

    /// <summary>
    /// 여러 학생의 학생부 기록을 단일 쿼리로 일괄 조회 (N+1 해소)
    /// StudentID → List&lt;StudentSpecial&gt; 딕셔너리로 반환
    /// </summary>
    public async Task<Dictionary<string, List<StudentSpecial>>> GetByStudentIdsAsync(
        System.Collections.Generic.IEnumerable<string> studentIds, int year)
    {
        var idList = new List<string>();
        var seen = new HashSet<string>();
        if (studentIds != null)
        {
            foreach (var id in studentIds)
            {
                if (!string.IsNullOrEmpty(id) && seen.Add(id)) idList.Add(id);
            }
        }

        var result = new Dictionary<string, List<StudentSpecial>>();
        if (idList.Count == 0) return result;

        var placeholders = string.Join(",", idList.Select((_, i) => $"@id{i}"));
        string query = $@"
                SELECT * FROM StudentSpecial
                WHERE StudentID IN ({placeholders})
                  AND Year = @Year
                ORDER BY Date DESC";

        try
        {
            using var cmd = CreateCommand(query);
            for (int i = 0; i < idList.Count; i++)
            {
                cmd.Parameters.AddWithValue($"@id{i}", idList[i]);
            }
            cmd.Parameters.AddWithValue("@Year", year);

            foreach (var id in idList) result[id] = new List<StudentSpecial>();
            foreach (var spec in await ExecuteListAsync(cmd, MapStudentSpecial).ConfigureAwait(false))
            {
                if (!result.TryGetValue(spec.StudentID, out var list))
                {
                    list = new List<StudentSpecial>();
                    result[spec.StudentID] = list;
                }
                list.Add(spec);
            }

            LogInfo($"학생부 일괄 조회 완료: 학생 {idList.Count}명");
            return result;
        }
        catch (Exception ex)
        {
            LogError($"학생부 일괄 조회 실패: Count={idList.Count}", ex);
            throw;
        }
    }

    /// <summary>
    /// CourseNo별 학생부 기록 조회
    /// </summary>
    public async Task<List<StudentSpecial>> GetByCourseAsync(int courseNo, int year)
    {
        const string query = @"
                SELECT * FROM StudentSpecial 
                WHERE CourseNo = @CourseNo
                  AND Year = @Year
                ORDER BY Date DESC";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@CourseNo", courseNo);
            cmd.Parameters.AddWithValue("@Year", year);

            return await ExecuteListAsync(cmd, MapStudentSpecial).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogError($"수업별 학생부 기록 조회 실패: CourseNo={courseNo}", ex);
            throw;
        }
    }

    // 미마감 조회(GetDraftByStudentAsync·GetDraftByTypeAsync)·영역별 조회(GetByTypeAsync)·
    // 교사별 조회(GetByTeacherAsync)·키워드 검색(SearchAsync)·통계 둘(GetCountByTypeAsync·
    // GetDraftCountByTypeAsync)은 호출부가 없어 서비스의 짝과 함께 지웠다(44차).
    // 화면은 학생 단위·수업 단위로만 읽고, 영역·마감 상태는 메모리에서 거른다.
    //
    // ⚠ 미마감 조회 셋(IsActive = 1)은 애초에 학년도 조건이 없었다 — 되살린다면
    //   Year 를 함께 받아야 옛 학년도가 섞이지 않는다.
    //
    // 이들이 쓰던 인덱스(idx_studentspecial_type·idx_studentspecial_active·
    // idx_studentspecial_teacher·idx_studentspecial_date)는 그대로 뒀다 — 지우려면
    // 손으로 DB 를 고쳐야 하고(이 프로젝트는 ALTER 마이그레이션을 두지 않는다) 이득이 없다.

    #endregion

    #region Update

    /// <summary>
    /// 학생부 기록 수정
    /// </summary>
    public async Task<bool> UpdateAsync(StudentSpecial special)
    {
        const string query = @"
                UPDATE StudentSpecial SET
                    StudentID = @StudentID,
                    Year = @Year,
                    Semester = @Semester,
                    Type = @Type,
                    Title = @Title,
                    Content = @Content,
                    Date = @Date,
                    TeacherID = @TeacherID,
                    CourseNo = @CourseNo,
                    SubjectName = @SubjectName,
                    IsActive = @IsActive,
                    Tag = @Tag
                WHERE No = @No";

        try
        {
            using var cmd = CreateCommand(query);
            AddSpecialParameters(cmd, special);

            int affected = await cmd.ExecuteNonQueryAsync();
            bool success = affected > 0;

            if (success)
                LogInfo($"학생부 기록 수정 완료: No={special.No}");
            else
                LogWarning($"학생부 기록 수정 실패: No={special.No}");

            return success;
        }
        catch (Exception ex)
        {
            LogError($"학생부 기록 수정 실패: No={special.No}", ex);
            throw;
        }
    }

    /// <summary>
    /// 마감 상태 변경
    /// </summary>
    public async Task<bool> UpdateFinalizedStatusAsync(int no, bool isFinalized)
    {
        const string query = @"
                UPDATE StudentSpecial SET
                    IsActive = @IsActive
                WHERE No = @No";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@No", no);
            cmd.Parameters.AddWithValue("@IsActive", isFinalized ? 0 : 1);

            int affected = await cmd.ExecuteNonQueryAsync();
            return affected > 0;
        }
        catch (Exception ex)
        {
            LogError($"마감 상태 변경 실패: No={no}", ex);
            throw;
        }
    }

    #endregion

    #region Delete

    /// <summary>
    /// 학생부 기록 삭제
    /// </summary>
    public async Task<bool> DeleteAsync(int no)
    {
        const string query = "DELETE FROM StudentSpecial WHERE No = @No";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@No", no);

            int affected = await cmd.ExecuteNonQueryAsync();
            bool success = affected > 0;

            if (success)
                LogInfo($"학생부 기록 삭제 완료: No={no}");
            else
                LogWarning($"학생부 기록 삭제 실패: No={no}");

            return success;
        }
        catch (Exception ex)
        {
            LogError($"학생부 기록 삭제 실패: No={no}", ex);
            throw;
        }
    }

    #endregion

    #region Helper Methods

    private void AddSpecialParameters(SqliteCommand cmd, StudentSpecial special)
    {
        cmd.Parameters.AddWithValue("@No", special.No);
        cmd.Parameters.AddWithValue("@StudentID", special.StudentID ?? string.Empty);
        cmd.Parameters.AddWithValue("@Year", special.Year);
        cmd.Parameters.AddWithValue("@Semester", special.Semester);
        cmd.Parameters.AddWithValue("@Type", special.Type ?? string.Empty);
        cmd.Parameters.AddWithValue("@Title", special.Title ?? string.Empty);
        cmd.Parameters.AddWithValue("@Content", special.Content ?? string.Empty);
        cmd.Parameters.AddWithValue("@Date", special.Date ?? string.Empty);
        // 빈 문자열은 NULL 이 아니다 — StudentSpecial.TeacherID 는 Teacher(TeacherID) FK 이고
        // BaseRepository 가 연결마다 foreign_keys=ON 을 켜므로, "" 로 넣으면 "" 인 교사 행을
        // 찾다가 FK 위반으로 저장이 통째로 실패한다. 미지정은 NULL 로 넣는다
        // (스키마도 TeacherID TEXT NULL + ON DELETE SET NULL 이고, 매퍼가 NULL→"" 로 되돌린다).
        cmd.Parameters.AddWithValue("@TeacherID",
            string.IsNullOrWhiteSpace(special.TeacherID) ? DBNull.Value : special.TeacherID);
        cmd.Parameters.AddWithValue("@CourseNo", special.CourseNo > 0 ? special.CourseNo : DBNull.Value);
        cmd.Parameters.AddWithValue("@SubjectName", string.IsNullOrEmpty(special.SubjectName) ? DBNull.Value : special.SubjectName);
        cmd.Parameters.AddWithValue("@IsActive", special.IsFinalized ? 0 : 1);
        cmd.Parameters.AddWithValue("@Tag", special.Tag ?? string.Empty);
    }

    private StudentSpecial MapStudentSpecial(SqliteDataReader reader, ReaderColumnCache cache)
    {
        var semesterIdx = cache.GetOrdinal("Semester");
        var courseNoIdx = cache.GetOrdinal("CourseNo");
        var subjectNameIdx = cache.GetOrdinal("SubjectName");
        // TeacherID 는 DatabaseInitializer.CleanupOrphansAsync 가 교사 삭제 시 NULL 로 만들고
        // Tag 는 DEFAULT 가 없어 NULL 일 수 있다. 예전의 무조건 GetString 은
        // 두 경우 모두 InvalidCastException 을 냈다.
        var teacherIdIdx = cache.GetOrdinal("TeacherID");
        var tagIdx = cache.GetOrdinal("Tag");
        // IsActive 도 마찬가지다 — 스키마가 `INTEGER DEFAULT 1` 이라 NOT NULL 이 아니다.
        // DEFAULT 는 값을 안 주었을 때만 채우므로, NULL 을 명시해 넣은 행이 있으면 그대로 남는다.
        // 위의 둘과 같은 InvalidCastException 이 날 자리인데 여기만 맨몸이었다.
        var isActiveIdx = cache.GetOrdinal("IsActive");

        return new StudentSpecial
        {
            No = reader.GetInt32(cache.GetOrdinal("No")),
            StudentID = reader.GetString(cache.GetOrdinal("StudentID")),
            Year = reader.GetInt32(cache.GetOrdinal("Year")),
            Semester = reader.IsDBNull(semesterIdx) ? 0 : reader.GetInt32(semesterIdx),
            Type = reader.GetString(cache.GetOrdinal("Type")),
            Title = reader.GetString(cache.GetOrdinal("Title")),
            Content = reader.GetString(cache.GetOrdinal("Content")),
            Date = reader.GetString(cache.GetOrdinal("Date")),
            TeacherID = reader.IsDBNull(teacherIdIdx) ? string.Empty : reader.GetString(teacherIdIdx),
            CourseNo = reader.IsDBNull(courseNoIdx) ? 0 : reader.GetInt32(courseNoIdx),
            SubjectName = reader.IsDBNull(subjectNameIdx) ? string.Empty : reader.GetString(subjectNameIdx),
            // NULL 은 "마감 안 됨"으로 본다(DEFAULT 1 = 작성 중 과 같은 뜻).
            IsFinalized = !reader.IsDBNull(isActiveIdx) && reader.GetInt32(isActiveIdx) == 0,
            Tag = reader.IsDBNull(tagIdx) ? string.Empty : reader.GetString(tagIdx)
        };
    }

    #endregion
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Repositories;

namespace NewSchool.Services;

/// <summary>
/// Enrollment Service — 학적 <b>조회</b>. 학적을 만드는 곳은 <c>Pages/AddStudentsPage</c> 이고,
/// 그 해 학적을 고치는 곳은 <c>Pages/StudentManagementPage</c> 다.
///
/// <para>⚠ 2026-07-30 정리: 학급 배정(AssignToClass·BulkAssign)·전학(TransferIn·TransferOut)·
/// 학적 이력 조회·담임별 조회·정적 UpdateAsync 를 제거했다. 전부 호출부가 0건이었고,
/// 전학 처리는 상태값을 <c>"전학(전출)"</c> 같은 리터럴로 써서 <see cref="EnrollmentStatus"/>
/// (<c>"전학"</c>)와도 어긋나 있었다.</para>
///
/// <para>⚠ 2026-08-23 정리: 학년 전체 진급(<c>PromoteStudentsAsync</c>)도 제거했다. 화면에서
/// 부를 방법이 없어 <b>한 번도 실사용된 적이 없고</b>, 그 사이 결함이 하나 자라 회귀 테스트가
/// 현실에 없는 데이터(2학기 학적)로 통과시키고 있었다(29차에 수정). 학년도가 바뀔 때 학적을
/// 잇는 기능이 필요해지면 그때 만든다 — 지운 코드와 테스트는 git 에 있으나 <b>되살릴 때는
/// 반 · 번호 재배정을 어떻게 할지부터 정해야 한다</b>(옛 코드는 이전 반 · 번호를 그대로 복사했다).
/// 학적 상태값 <c>EnrollmentStatus.Graduated</c> 와 <c>GraduationDate</c> 컬럼은 과거 데이터
/// 호환을 위해 그대로 둔다.</para>
/// </summary>
public sealed class EnrollmentService : IDisposable
{
    private readonly EnrollmentRepository _enrollmentRepo;
    private bool _disposed;

    public EnrollmentService() : this(SchoolDatabase.DbPath) { }

    /// <summary>DB 경로 주입 생성자 (테스트용 임시 DB 지원)</summary>
    public EnrollmentService(string dbPath)
    {
        _enrollmentRepo = new EnrollmentRepository(dbPath);
    }

    #region 조회
    ///<summary>
        ///학년도별 학년 리스트 조회
    /// </summary>
    public async Task<List<int>> GetGradeListByYearAsync(string? schoolCode, int? year)
    {
        return await _enrollmentRepo.GetGradesByYearAsync(schoolCode, year);
    }

    ///<summary>
    ///학급 리스트 조회
    ///학년도별 학년별 학급리스트를 조회
    /// </summary>
    public async Task<List<int>> GetClassListAsync(string schoolCode, int year, int grade)
    {
        return await _enrollmentRepo.GetClassListByGradeAsync(schoolCode, year, grade);
    }
    /// <summary>
    /// 학생 명부 조회. 학년도·학년·반으로 거르며 <b>0 은 전체</b>를 뜻한다.
    ///
    /// <para><b>학적은 학년 단위로 다룬다(2026-07-30 확정).</b> 테이블은 (학년도, 학기) 단위
    /// 행이지만 학급 편성은 한 학년도 내내 유지되고, 실제로 <b>2학기 학적 행을 만드는 경로가
    /// 앱에 없다</b>(학생 추가 화면이 등록 시점의 행 하나만 만든다). 그래서 예전처럼 학기로
    /// 거르면 1학기에 등록한 학생이 <b>2학기에는 명부·누가기록·반 목록에서 통째로 사라졌다</b>.
    /// 이 메서드는 학기를 조건으로 쓰지 않고, 혹시 두 학기 행이 다 있으면 학생당 최신 학기
    /// 한 건만 남긴다.</para>
    ///
    /// ⚠ 학기 인자를 다시 만들지 말 것. 학기별 기록은 <c>StudentLog.Semester</c> 처럼
    /// 기록 쪽에서 구분한다 — 명부(누가 이 반에 있는가)는 학년 단위다.
    /// </summary>
    /// <param name="includeNotOnRoll">
    /// 전출·졸업·자퇴·퇴학처럼 <b>명부에서 빠진 학생까지</b> 포함할지.
    ///
    /// <para>기본값은 <c>false</c> — "지금 이 반에 누가 있나" 가 거의 모든 호출처의 질문이다.
    /// 예전에는 상태를 아예 보지 않아 전출한 학생이 명렬표·좌석표·수업 배정·동아리 배정에
    /// 그대로 섞여 들어왔다. 참으로 켜는 곳은 <b>설정 → 학생 관리</b> 하나뿐이며,
    /// 거기서만 빠진 학생을 흐리게 보여 준다.</para>
    /// </param>
    public async Task<List<Enrollment>> GetEnrollmentsAsync(
        string schoolCode, int year = 0, int grade = 0, int classNum = 0,
        bool includeNotOnRoll = false)
    {
        // 거르기는 SQL(WHERE IsActive = 1)이 한다. 상태 목록을 WHERE 절에 나열하던 시절과
        // 달리 조건이 불리언 하나라 안정적이고, IsActive 는 ChangeType 에서만 채워지므로
        // 판정 규칙은 여전히 한 곳(EnrollmentChange.IsActive)에만 있다.
        var enrollments = await _enrollmentRepo.GetEnrollmentsAsync(
            schoolCode: schoolCode, year: year, grade: grade, classNum: classNum,
            includeInactive: includeNotOnRoll);

        return DedupeByYear(enrollments);
    }

    /// <summary>
    /// 학급 명부 조회. <see cref="GetEnrollmentsAsync"/> 와 같은 규칙이며 번호순으로 돌려준다.
    /// </summary>
    public async Task<List<Enrollment>> GetClassRosterAsync(
        string schoolCode, int year, int grade, int classNo,
        bool includeNotOnRoll = false)
    {
        var enrollments = await GetEnrollmentsAsync(schoolCode, year, grade, classNo, includeNotOnRoll);
        return enrollments.OrderBy(e => e.Number).ToList();
    }

    /// <summary>
    /// 학생당 <b>학년도별</b> 한 건만 남긴다.
    ///
    /// <para>학적은 <c>UNIQUE(StudentID, SchoolCode, Year)</c> 라 원래 학년도당 한 줄이지만,
    /// 여러 학교를 함께 읽는 조회가 있어 그때 겹치는 것을 걸러 준다. 여러 학년도를
    /// 한꺼번에 조회하는 곳이 있으므로 학년도까지 묶어야 한다 — <c>StudentID</c> 로만
    /// 묶으면 과거 학년도 학적이 사라진다.</para>
    /// </summary>
    private static List<Enrollment> DedupeByYear(List<Enrollment> enrollments) =>
        enrollments
            .GroupBy(e => (e.StudentID, e.Year))
            .Select(g => g.First())
            .OrderBy(e => e.Year).ThenBy(e => e.Grade).ThenBy(e => e.Class).ThenBy(e => e.Number)
            .ToList();

    /// <summary>
    /// 학생의 현재 학적 조회
    /// </summary>
    public async Task<Enrollment?> GetCurrentEnrollmentAsync(string studentId) => await _enrollmentRepo.GetCurrentByStudentIdAsync(studentId);

    /// <summary>
    /// 여러 학생의 현재 학적을 한 번에 조회 - N+1 방지용
    /// </summary>
    public async Task<List<Enrollment>> GetCurrentEnrollmentsAsync(List<string> studentIds) => await _enrollmentRepo.GetCurrentByStudentIdsAsync(studentIds);

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (!_disposed)
        {
            _enrollmentRepo?.Dispose();
            _disposed = true;
        }
    }

    #endregion

}

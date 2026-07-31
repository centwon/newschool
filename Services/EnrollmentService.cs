using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Repositories;

namespace NewSchool.Services;

/// <summary>
/// Enrollment Service — 학적 조회와 진급 처리.
///
/// <para>⚠ 2026-07-30 정리: 학급 배정(AssignToClass·BulkAssign)·전학(TransferIn·TransferOut)·
/// 학적 이력 조회·담임별 조회·정적 UpdateAsync 를 제거했다. 전부 호출부가 0건이었고,
/// 전학 처리는 상태값을 <c>"전학(전출)"</c> 같은 리터럴로 써서 <see cref="EnrollmentStatus"/>
/// (<c>"전학"</c>)와도 어긋나 있었다. 실제 학적 생성은 <c>Pages/AddStudentsPage</c> 가 한다.</para>
///
/// <para><see cref="PromoteStudentsAsync"/> 는 호출부가 없지만 <b>의도적으로 남긴다</b> —
/// 학년을 넘겨 누가기록·특기사항을 잇는 유일한 경로이고 회귀 테스트가 붙어 있다.
/// 실사용 전에 반·번호 재배정 미리보기 화면이 필요할 뿐이다.</para>
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

    #region 진급 처리

    /// <summary>
    /// 학년 전체 진급 처리 — 같은 StudentID 로 다음 학년도 1학기 학적을 생성한다.
    /// StudentID 가 등록연도를 포함하므로(매년 재등록 시 이력 단절), 다년간 누가기록·특기사항을
    /// 이어가려면 이 경로가 유일하다. 최고 학년(maxGrade)은 진급 대상이 아니며 아무 처리도 하지
    /// 않는다 — 별도 졸업 마감은 두지 않는 설계(학년도 기준 조회라 옛 학적이 화면에 섞이지 않음).
    /// ⚠ UI 미노출: 실사용 전에 반/번호 재배정 미리보기 화면이 필요하다(현재는 이전 반·번호 복사).
    /// </summary>
    /// <param name="maxGrade">학교급 최고 학년 (초등 6, 중·고 3)</param>
    public async Task<int> PromoteStudentsAsync(string schoolCode, int fromYear, int fromGrade, int maxGrade)
    {
        if (fromGrade >= maxGrade)
            return 0; // 최고 학년은 진급 대상 아님 (졸업 마감 처리도 하지 않음)

        // 진급 대상 = 그 학년도 해당 학년의 재학생.
        //
        // ⚠ 예전에는 "2학기 학적"만 찾았다(GetByGradeAsync(..., semester: 2, ...)).
        //    그런데 앱은 2학기 학적을 만들지 않으므로(학적은 학년 단위 — GetEnrollmentsAsync
        //    주석 참고) 실제 데이터에서는 늘 0명이었다. 회귀 테스트가 2학기 행을 직접 심어
        //    통과시키고 있어 드러나지 않았다.
        var students = await GetEnrollmentsAsync(schoolCode, fromYear, fromGrade);
        var activeStudents = students.Where(e => e.Status == EnrollmentStatus.Enrolled).ToList();

        if (activeStudents.Count == 0)
            return 0;

        try
        {
            _enrollmentRepo.BeginTransaction();

            int count = 0;
            int nextYear = fromYear + 1;
            int nextGrade = fromGrade + 1;

            foreach (var oldEnrollment in activeStudents)
            {
                // 새 학년도 1학기 학적 생성
                var newEnrollment = new Enrollment
                {
                    StudentID = oldEnrollment.StudentID,
                    SchoolCode = oldEnrollment.SchoolCode,
                    Year = nextYear,
                    Semester = 1,
                    Grade = nextGrade,
                    Class = oldEnrollment.Class, // 같은 반 유지 (필요시 수정)
                    Number = oldEnrollment.Number, // 같은 번호 유지 (필요시 수정)
                    Status = EnrollmentStatus.Enrolled,
                    //TeacherID = oldEnrollment.TeacherID, // 담임 변경 필요시 나중에 수정
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };

                await _enrollmentRepo.CreateAsync(newEnrollment);
                count++;
            }

            _enrollmentRepo.Commit();
            return count;
        }
        catch
        {
            _enrollmentRepo.Rollback();
            throw;
        }
    }

    #endregion

    // 졸업 처리(GraduateAsync)는 제거됨 — 조회가 전부 학년도 기준이라 별도 졸업 마감이
    // 불필요하고, UI 미노출 상태에서 초등(1~6학년) 진급을 졸업으로 오처리하는 버그의
    // 온상이었다. 학적 상태값(EnrollmentStatus.Graduated)과 GraduationDate 컬럼은
    // 과거 데이터 호환을 위해 유지한다. (2026-07-15)

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
    public async Task<List<Enrollment>> GetEnrollmentsAsync(
        string schoolCode, int year = 0, int grade = 0, int classNum = 0)
    {
        var enrollments = await _enrollmentRepo.GetEnrollmentsAsync(
            schoolCode: schoolCode, year: year, grade: grade, classNum: classNum);

        return DedupeByYear(enrollments);
    }

    /// <summary>
    /// 학급 명부 조회. <see cref="GetEnrollmentsAsync"/> 와 같은 규칙이며 번호순으로 돌려준다.
    /// </summary>
    public async Task<List<Enrollment>> GetClassRosterAsync(
        string schoolCode, int year, int grade, int classNo)
    {
        var enrollments = await GetEnrollmentsAsync(schoolCode, year, grade, classNo);
        return enrollments.OrderBy(e => e.Number).ToList();
    }

    /// <summary>
    /// 학생당 <b>학년도별</b> 한 건만 남긴다(같은 학년도에 1·2학기 행이 모두 있으면 최신 학기).
    /// 여러 학년도를 한꺼번에 조회하는 곳이 있으므로 학년도까지 묶어야 한다 —
    /// StudentID 로만 묶으면 과거 학년도 학적이 사라진다.
    /// </summary>
    private static List<Enrollment> DedupeByYear(List<Enrollment> enrollments) =>
        enrollments
            .GroupBy(e => (e.StudentID, e.Year))
            .Select(g => g.OrderByDescending(e => e.Semester).First())
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

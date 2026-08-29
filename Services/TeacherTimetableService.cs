using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.ViewModels;

namespace NewSchool.Services;

/// <summary>
/// <b>교사</b> 시간표 — "내 수업이 언제 어디" 를 답한다.
///
/// <para>학급 시간표는 <see cref="TimetableService"/> 가 맡는다. 둘은 중복이 아니라
/// <b>관점</b>으로 갈린다 — 이쪽은 <c>Lesson</c>(교사 배치), 저쪽은 <c>ClassTimetable</c>
/// (학급 시간표)을 읽는다. 예전 이름 <c>LessonService</c> 는 그 갈림을 전혀 드러내지
/// 못했고, 이름이 범용으로 보이니 CRUD 래퍼가 붙어 정체가 둘이 됐다.</para>
///
/// <para>이 타입이 있는 이유는 <b>리포지토리 하나로는 못 하는 일</b> 뿐이다 —
/// <c>Lesson</c> 과 <c>Course</c> 를 엮어 격자를 채우고(N+1 을 피하려 과목을 일괄 로드한다),
/// <c>Settings</c> 의 "나·올해·이번 학기·오늘" 을 묶는다. 단순 읽기·쓰기는
/// <c>LessonRepository</c> 를 직접 쓴다.</para>
/// </summary>
public class TeacherTimetableService : IDisposable
{
    private readonly LessonRepository _lessonRepository;
    private readonly CourseRepository _courseRepository;
    private bool _disposed;

    public TeacherTimetableService()
    {
        _lessonRepository = new LessonRepository(SchoolDatabase.DbPath);
        _courseRepository = new CourseRepository(SchoolDatabase.DbPath);
    }

    public TeacherTimetableService(string dbPath)
    {
        _lessonRepository = new LessonRepository(dbPath);
        _courseRepository = new CourseRepository(dbPath);
    }

    // CRUD 통과 래퍼 여섯(Create·Update·Delete·GetById·GetTeacherSchedule·GetByCourse)은
    // 지웠다. 리포지토리를 그대로 부르기만 해서 얹는 것이 없었고, 부르는 곳도 한 곳도 없었다.
    //
    // 더 나빴던 것은 그것들이 이 타입을 **범용 CRUD 파사드로 보이게** 했다는 점이다. 그렇게
    // 읽으면 굳이 거칠 이유가 없어 리포지토리로 직행하게 되고, 실제로 Lesson 을 만지는 화면
    // 열 곳 중 일곱이 그렇게 갔다. 남은 셋만 이 서비스를 쓴다.
    //
    // 그러니 여기 CRUD 를 다시 만들지 말 것. 쓰기와 단순 조회는 LessonRepository 를 직접
    // 쓰는 것이 이 저장소의 배치다. 이 서비스는 아래 하나를 위해 있다 —
    // 리포지토리 하나로는 못 하는 일.

    #region 교사 시간표 조회

    /// <summary>
    /// 오늘 내 수업. <c>Settings</c> 의 "나·오늘·올해·이번 학기" 를 묶는 자리다.
    ///
    /// <para>학년도·학기를 함께 넘기는 것이 중요하다 — 빼면 작년 같은 요일 수업이 섞인다.</para>
    /// </summary>
    public async Task<List<Lesson>> GetTodayLessonsAsync()
    {
        return await _lessonRepository.GetByDateAsync(
            Settings.User.Value,
            DateTime.Today,
            Settings.WorkYear.Value,
            Settings.WorkSemester.Value);
    }

    #endregion

    #region 시간표 ViewModel 생성

    /// <summary>
    /// 교사 시간표 ViewModel 생성 (Lesson 기반)
    /// </summary>
    public async Task<TimetableViewModel> GetTeacherTimetableViewModelAsync(
        string teacherId, int year, int semester)
    {
        var viewModel = new TimetableViewModel
        {
            Title = $"{teacherId} 시간표",
            Year = year,
            Semester = semester
        };

        viewModel.InitializeEmptyTimetable();

        // Lesson 테이블에서 시간표 조회
        var lessons = await _lessonRepository.GetTeacherScheduleAsync(teacherId, year, semester);

        // Course 정보 일괄 로드 (N+1 쿼리 방지)
        var courseIds = lessons.Select(l => l.Course).Distinct().ToList();
        var courseList = await _courseRepository.GetByIdsAsync(courseIds);
        var courses = courseList.ToDictionary(c => c.No, c => c);

        // ViewModel에 수업 정보 채우기
        foreach (var lesson in lessons)
        {
            var item = viewModel.GetItem(lesson.DayOfWeek, lesson.Period);
            if (item != null)
            {
                item.LessonNo = lesson.No;
                item.CourseNo = lesson.Course;
                item.SubjectName = courses.TryGetValue(lesson.Course, out var course)
                    ? course.Subject
                    : "Unknown";
                item.Room = lesson.Room;
                item.TeacherName = teacherId;
                item.IsEmpty = false;
            }
        }

        return viewModel;
    }

    /// <summary>
    /// 현재 사용자(교사)의 시간표 ViewModel 생성
    /// </summary>
    public async Task<TimetableViewModel> GetMyTimetableViewModelAsync()
    {
        return await GetTeacherTimetableViewModelAsync(
            Settings.User.Value,
            Settings.WorkYear.Value,
            Settings.WorkSemester.Value);
    }

    // 학급 시간표 ViewModel(GetClassTimetableViewModelAsync)은 이를 부르던
    // TimetableControl.LoadClassScheduleAsync 와 함께 지웠다(39차). 학급 시간표는 Lesson 이
    // 아니라 ClassTimetable 테이블을 쓰는 CourseTimetableBoard·WeeklyTimetableView 가 보여 준다.

    #endregion

    // ── 수업 상태 관리는 통째로 없앴다 ──────────────────────────
    //
    // 완료·취소 처리(MarkCompletedAsync·MarkCancelledAsync)를 리포지토리 쪽과 함께 지웠다.
    // 부르는 곳이 한 곳도 없어 Lesson.IsCompleted·IsCancelled 는 만들어진 뒤로 늘 0 이었고,
    // 그래서 그 두 칸으로 거르는 조회들도 사실상 조건이 없는 것과 같았다.
    //
    // ⚠ 되살리기 전에 읽을 것 — 휴강은 이 플래그로 하면 안 된다. Lesson 은 정기 시간표라
    // 행 하나가 "매주 그 교시"를 뜻하므로, 여기에 취소를 세우면 그 수업이 **매주** 사라진다.
    // 특정 날짜 한 교시만 바꾸는 일은 LessonChange 가 맡는다(그 모델 주석에 근거가 있다).
    //
    // 취소 해제(UnmarkCancelledAsync), 시간표 생성 둘(CreateScheduleFromCourseAsync·
    // CreateSpecialLessonAsync)은 호출부가 없어 지웠다(39차). 시간표는 CourseTimetableBoard 가
    // ClassTimetable 로 직접 짜고, 결·보강은 LessonChange 로 따로 관리한다.

    // 시간대 충돌 확인(HasConflictAsync)은 이를 부르던 HasMyConflictAsync 와 함께 지웠다(39차).
    // 시간표 편집 화면은 자기 격자 안에서 겹침을 직접 본다.

    #region IDisposable

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _lessonRepository?.Dispose();
                _courseRepository?.Dispose();
            }
            _disposed = true;
        }
    }

    #endregion
}

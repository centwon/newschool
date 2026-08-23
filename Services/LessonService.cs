using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.ViewModels;

namespace NewSchool.Services;

/// <summary>
/// Lesson 비즈니스 로직 서비스
/// 시간표 관리 및 수업 진행 관리
/// </summary>
public class LessonService : IDisposable
{
    private readonly LessonRepository _lessonRepository;
    private readonly CourseRepository _courseRepository;
    private bool _disposed;

    public LessonService()
    {
        _lessonRepository = new LessonRepository(SchoolDatabase.DbPath);
        _courseRepository = new CourseRepository(SchoolDatabase.DbPath);
    }

    public LessonService(string dbPath)
    {
        _lessonRepository = new LessonRepository(dbPath);
        _courseRepository = new CourseRepository(dbPath);
    }

    #region CRUD

    /// <summary>
    /// 수업 생성
    /// </summary>
    public async Task<int> CreateAsync(Lesson lesson)
    {
        return await _lessonRepository.CreateAsync(lesson);
    }

    /// <summary>
    /// 수업 수정
    /// </summary>
    public async Task<bool> UpdateAsync(Lesson lesson)
    {
        return await _lessonRepository.UpdateAsync(lesson);
    }

    /// <summary>
    /// 수업 삭제
    /// </summary>
    public async Task<bool> DeleteAsync(int no)
    {
        return await _lessonRepository.DeleteAsync(no);
    }

    /// <summary>
    /// 수업 조회
    /// </summary>
    public async Task<Lesson?> GetByIdAsync(int no)
    {
        return await _lessonRepository.GetByIdAsync(no);
    }

    #endregion

    #region 시간표 조회

    /// <summary>
    /// 교사 시간표 조회
    /// </summary>
    public async Task<List<Lesson>> GetTeacherScheduleAsync(string teacherId, int year, int semester)
    {
        return await _lessonRepository.GetTeacherScheduleAsync(teacherId, year, semester);
    }

    /// <summary>
    /// 학급 시간표 조회
    /// </summary>
    public async Task<List<Lesson>> GetClassScheduleAsync(int year, int semester, int grade, int classNum)
    {
        return await _lessonRepository.GetClassScheduleAsync(year, semester, grade, classNum);
    }

    /// <summary>
    /// 오늘 수업 목록 조회
    /// </summary>
    public async Task<List<Lesson>> GetTodayLessonsAsync()
    {
        return await _lessonRepository.GetByDateAsync(Settings.User.Value, DateTime.Today);
    }

    /// <summary>
    /// Course별 수업 조회
    /// </summary>
    public async Task<List<Lesson>> GetByCourseAsync(int courseNo)
    {
        return await _lessonRepository.GetByCourseAsync(courseNo);
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

    #region 수업 상태 관리

    /// <summary>
    /// 수업 완료 처리
    /// </summary>
    public async Task<bool> MarkCompletedAsync(int lessonNo)
    {
        return await _lessonRepository.MarkCompletedAsync(lessonNo, true);
    }

    /// <summary>
    /// 수업 취소 처리
    /// </summary>
    public async Task<bool> MarkCancelledAsync(int lessonNo)
    {
        return await _lessonRepository.MarkCancelledAsync(lessonNo, true);
    }

    // 취소 해제(UnmarkCancelledAsync), 시간표 생성 둘(CreateScheduleFromCourseAsync·
    // CreateSpecialLessonAsync)은 호출부가 없어 지웠다(39차). 시간표는 CourseTimetableBoard 가
    // ClassTimetable 로 직접 짜고, 결·보강은 LessonChange 로 따로 관리한다.

    #endregion

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

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
    /// 현재 사용자(교사)의 시간표 조회
    /// </summary>
    public async Task<List<Lesson>> GetMyScheduleAsync()
    {
        return await _lessonRepository.GetTeacherScheduleAsync(
            Settings.User.Value,
            Settings.WorkYear.Value,
            Settings.WorkSemester.Value);
    }

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
    /// 특정 날짜 수업 목록 조회
    /// </summary>
    public async Task<List<Lesson>> GetLessonsByDateAsync(DateTime date)
    {
        return await _lessonRepository.GetByDateAsync(Settings.User.Value, date);
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

    /// <summary>
    /// 학급 시간표 ViewModel 생성
    /// </summary>
    public async Task<TimetableViewModel> GetClassTimetableViewModelAsync(
        int year, int semester, int grade, int classNum)
    {
        var viewModel = new TimetableViewModel
        {
            Title = $"{grade}학년 {classNum}반 시간표",
            Year = year,
            Semester = semester
        };

        viewModel.InitializeEmptyTimetable();

        // Lesson 테이블에서 수업 조회
        var lessons = await _lessonRepository.GetClassScheduleAsync(year, semester, grade, classNum);

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
                item.TeacherName = lesson.Teacher;
                item.IsEmpty = false;
            }
        }

        return viewModel;
    }

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

    /// <summary>
    /// 수업 취소 해제
    /// </summary>
    public async Task<bool> UnmarkCancelledAsync(int lessonNo)
    {
        return await _lessonRepository.MarkCancelledAsync(lessonNo, false);
    }

    #endregion

    #region 시간표 생성

    /// <summary>
    /// Course에서 정기 시간표 생성
    /// CourseSchedule 데이터를 Lesson으로 마이그레이션
    /// </summary>
    public async Task<int> CreateScheduleFromCourseAsync(Course course, List<(int DayOfWeek, int Period, string Room)> schedules)
    {
        // 기존 정기 수업 삭제
        await _lessonRepository.DeleteByCourseAsync(course.No);

        // 새 정기 수업 생성
        int count = 0;
        foreach (var (dayOfWeek, period, room) in schedules)
        {
            var lesson = new Lesson
            {
                Course = course.No,
                Teacher = course.TeacherID,
                Year = course.Year,
                Semester = course.Semester,
                DayOfWeek = dayOfWeek,
                Period = period,
                Grade = course.Grade,
                Class = 0, // 나중에 설정
                Room = room,
                IsRecurring = true
            };

            await _lessonRepository.CreateAsync(lesson);
            count++;
        }

        return count;
    }

    /// <summary>
    /// 보충/특별 수업 생성 (비정기)
    /// </summary>
    public async Task<int> CreateSpecialLessonAsync(
        int courseNo, string teacherId, DateTime date, int period, 
        int grade, int classNum, string room, string topic = "")
    {
        var lesson = new Lesson
        {
            Course = courseNo,
            Teacher = teacherId,
            Year = Settings.WorkYear.Value,
            Semester = Settings.WorkSemester.Value,
            Date = date.ToString("yyyy-MM-dd"),
            DayOfWeek = 0, // 비정기는 요일 무시
            Period = period,
            Grade = grade,
            Class = classNum,
            Room = room,
            Topic = topic,
            IsRecurring = false
        };

        return await _lessonRepository.CreateAsync(lesson);
    }

    #endregion

    #region 충돌 확인

    /// <summary>
    /// 시간대 충돌 확인
    /// </summary>
    public async Task<bool> HasConflictAsync(string teacherId, int year, int semester, int dayOfWeek, int period)
    {
        var existing = await _lessonRepository.GetBySlotAsync(teacherId, year, semester, dayOfWeek, period);
        return existing != null;
    }

    /// <summary>
    /// 현재 사용자 시간대 충돌 확인
    /// </summary>
    public async Task<bool> HasMyConflictAsync(int dayOfWeek, int period)
    {
        return await HasConflictAsync(
            Settings.User.Value,
            Settings.WorkYear.Value,
            Settings.WorkSemester.Value,
            dayOfWeek,
            period);
    }

    #endregion

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

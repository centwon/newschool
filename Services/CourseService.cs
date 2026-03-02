using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Repositories;

namespace NewSchool.Services;

internal partial class CourseService : IDisposable
{
    private CourseRepository _courseRepository;
    private CourseEnrollmentRepository _courseEnrollmentRepository;
    private bool _disposed;
    public CourseService()
    {
        _courseRepository = new CourseRepository(SchoolDatabase.DbPath);
        _courseEnrollmentRepository = new CourseEnrollmentRepository(SchoolDatabase.DbPath);
    }
    public async Task<List<int>> GetDistinctCourseYearsAsync(string teacher)
    {
        return await _courseRepository.GetDistinctCourseYearsAsync(teacher);
    }
    public async Task<List<Course>> GetAllCourseAsync(string schoolCode, int year, int semester)
    {
        return await _courseRepository.GetByTeacherAsync(schoolCode, year, semester);

    }
    public async Task<List<CourseEnrollment>> GetCourseEnrollmentsAsync(string schoolCode, int year, int semester, int courseCode)
    {
        return await _courseEnrollmentRepository.GetByCourseAsync(courseCode);
    }

    public async Task<IEnumerable<Course>> GetByTeacherAsync(string teacherId, int year, int semester)
    {
        return await _courseRepository.GetByTeacherAsync(teacherId, year, semester);
    }

    /// <summary>
    /// 현재 로그인한 교사의 과목 목록 조회
    /// </summary>
    public async Task<List<Course>> GetMyCoursesAsync()
    {
        return await _courseRepository.GetByTeacherAsync(
            Settings.User.Value,
            Settings.WorkYear.Value,
            Settings.WorkSemester.Value
        );
    }

    // 1. IDisposable 인터페이스 구현
    public void Dispose()
    {
        Dispose(true);
        // 가비지 컬렉터가 파이널라이저를 호출하지 않도록 설정
        GC.SuppressFinalize(this);
    }

    // 2. 실제 리소스 해제 로직 (상속 가능성을 열어둠)
    protected virtual void Dispose(bool disposing)
    {
        // 이미 해제되었다면 반환
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            // 관리되는 리소스(Managed Resources) 해제
            // ?. 연산자로 null 체크를 하여 안전하게 해제
            _courseRepository?.Dispose();
            _courseEnrollmentRepository?.Dispose();
        }

        // 비관리 리소스 해제 (현재는 없음)

        _disposed = true;
    }
}

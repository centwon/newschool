using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Repositories;

namespace NewSchool.Services;

/// <summary>
/// Student Service — 학생 기본정보(Student 테이블) 조회·수정.
///
/// <para>⚠ 2026-07-30 정리: 등록·상세정보·연락처·삭제·통계 메서드 11개를 제거했다. 전부
/// 호출부가 0건이었고, 그중 <c>RegisterNewStudentAsync</c> 는 <b>트랜잭션이 가짜</b>여서
/// (studentRepo 에만 BeginTransaction 을 걸고 enrollment·detail 은 다른 연결로 즉시 커밋)
/// 되살려 쓰면 바로 깨지는 코드였다. 실제 학생 등록은
/// <c>Pages/AddStudentsPage</c> 가 단일 연결·단일 트랜잭션으로 처리한다.</para>
///
/// 학생 상세정보(StudentDetail)는 <see cref="StudentDetailService"/> 가 담당한다.
/// </summary>
public sealed class StudentService : IDisposable
{
    private readonly StudentRepository _studentRepo;
    private bool _disposed;

    public StudentService(string dbPath)
    {
        _studentRepo = new StudentRepository(dbPath);
    }

    #region 학생 정보 조회

    /// <summary>
    /// 학생 기본정보 조회
    /// </summary>
    public async Task<Student?> GetBasicInfoAsync(string studentId)
    {
        return await _studentRepo.GetByIdAsync(studentId);
    }

    /// <summary>
    /// 학생 목록 조회 (여러 ID) — N+1 방지용
    /// </summary>
    public async Task<List<Student>> GetStudentsByIdsAsync(List<string> studentIds)
    {
        if (studentIds == null || studentIds.Count == 0)
            return new List<Student>();

        return await _studentRepo.GetByIdsAsync(studentIds);
    }

    #endregion

    #region 학생 정보 수정

    /// <summary>
    /// 학생 기본정보 수정.
    /// Name, Sex, Photo 변경 시 <see cref="StudentRepository.UpdateAsync"/> 가
    /// denormalized 된 Enrollment 쪽까지 동기화한다.
    /// </summary>
    public async Task<bool> UpdateBasicInfoAsync(Student student)
    {
        ValidateStudent(student);

        student.UpdatedAt = DateTime.Now;
        return await _studentRepo.UpdateAsync(student);
    }

    #endregion

    #region 유효성 검증

    private static void ValidateStudent(Student student)
    {
        if (string.IsNullOrEmpty(student.StudentID))
            throw new ArgumentException("학생 ID는 필수입니다.");

        if (student.StudentID.Length != 15)
            throw new ArgumentException("학생 ID는 15자리여야 합니다.");

        if (string.IsNullOrEmpty(student.Name))
            throw new ArgumentException("학생 이름은 필수입니다.");

        if (string.IsNullOrEmpty(student.Sex))
            throw new ArgumentException("성별은 필수입니다.");

        if (student.Sex != "남" && student.Sex != "여")
            throw new ArgumentException("성별은 '남' 또는 '여'만 가능합니다.");
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (!_disposed)
        {
            _studentRepo?.Dispose();
            _disposed = true;
        }
    }

    #endregion
}

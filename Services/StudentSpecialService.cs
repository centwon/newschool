using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Repositories;

namespace NewSchool.Services;

/// <summary>
/// StudentSpecial Service
/// 학교생활기록부 특기사항 비즈니스 로직
/// 마감(IsFinalized)된 기록은 수정/삭제가 거부됨
/// </summary>
public class StudentSpecialService : IDisposable
{
    private readonly StudentSpecialRepository _repository;
    private bool _disposed;

    public StudentSpecialService()
    {
        _repository = new StudentSpecialRepository(SchoolDatabase.DbPath);
    }

    #region CRUD Operations

    /// <summary>
    /// 학생부 기록 생성
    /// </summary>
    public async Task<int> CreateAsync(StudentSpecial special)
    {
        ValidateSpecial(special);
        return await _repository.CreateAsync(special);
    }

    /// <summary>
    /// No로 학생부 기록 조회
    /// </summary>
    public async Task<StudentSpecial?> GetByIdAsync(int no)
    {
        return await _repository.GetByIdAsync(no);
    }

    /// <summary>
    /// 학생별 학생부 기록 조회
    /// </summary>
    public async Task<List<StudentSpecial>> GetByStudentAsync(string studentId, int year)
    {
        if (string.IsNullOrWhiteSpace(studentId))
            throw new ArgumentException("학생 ID가 필요합니다.", nameof(studentId));

        return await _repository.GetByStudentAsync(studentId, year);
    }

    /// <summary>
    /// 학생의 미마감(작성 중) 기록 조회
    /// </summary>
    public async Task<List<StudentSpecial>> GetDraftByStudentAsync(string studentId)
    {
        if (string.IsNullOrWhiteSpace(studentId))
            throw new ArgumentException("학생 ID가 필요합니다.", nameof(studentId));

        return await _repository.GetDraftByStudentAsync(studentId);
    }

    /// <summary>
    /// 수업별 학생부 기록 조회
    /// </summary>
    public async Task<List<StudentSpecial>> GetByCourseAsync(int courseNo, int year)
    {
        return await _repository.GetByCourseAsync(courseNo, year);
    }

    /// <summary>
    /// 영역별 학생부 기록 조회
    /// </summary>
    public async Task<List<StudentSpecial>> GetByTypeAsync(string type, int year)
    {
        if (string.IsNullOrWhiteSpace(type))
            throw new ArgumentException("영역이 필요합니다.", nameof(type));

        return await _repository.GetByTypeAsync(type, year);
    }

    /// <summary>
    /// 학생의 영역별 학생부 기록 조회
    /// </summary>
    public async Task<List<StudentSpecial>> GetByTypeAsync(string studentId, int year, string type)
    {
        if (string.IsNullOrWhiteSpace(studentId))
            throw new ArgumentException("학생 ID가 필요합니다.", nameof(studentId));
        if (string.IsNullOrWhiteSpace(type))
            throw new ArgumentException("영역이 필요합니다.", nameof(type));

        var allSpecs = await _repository.GetByStudentAsync(studentId, year);
        return allSpecs.Where(s => s.Type == type).ToList();
    }

    /// <summary>
    /// 영역별 미마감(작성 중) 기록 조회
    /// </summary>
    public async Task<List<StudentSpecial>> GetDraftByTypeAsync(string type)
    {
        if (string.IsNullOrWhiteSpace(type))
            throw new ArgumentException("영역이 필요합니다.", nameof(type));

        return await _repository.GetDraftByTypeAsync(type);
    }

    /// <summary>
    /// 교사가 작성한 학생부 기록 조회
    /// </summary>
    public async Task<List<StudentSpecial>> GetByTeacherAsync(string teacherId, int year)
    {
        if (string.IsNullOrWhiteSpace(teacherId))
            throw new ArgumentException("교사 ID가 필요합니다.", nameof(teacherId));

        return await _repository.GetByTeacherAsync(teacherId, year);
    }

    /// <summary>
    /// 키워드로 학생부 기록 검색
    /// </summary>
    public async Task<List<StudentSpecial>> SearchAsync(string keyword, int year)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return new List<StudentSpecial>();

        return await _repository.SearchAsync(keyword, year);
    }

    /// <summary>
    /// 학생부 기록 수정 (마감된 기록은 거부)
    /// </summary>
    public async Task<bool> UpdateAsync(StudentSpecial special)
    {
        ValidateSpecial(special);
        await EnsureNotFinalizedAsync(special.No);
        return await _repository.UpdateAsync(special);
    }

    /// <summary>
    /// 마감 상태 변경 (마감/마감해제)
    /// </summary>
    public async Task<bool> UpdateFinalizedStatusAsync(int no, bool isFinalized)
    {
        return await _repository.UpdateFinalizedStatusAsync(no, isFinalized);
    }

    /// <summary>
    /// 학생부 기록 삭제 (마감된 기록은 거부)
    /// </summary>
    public async Task<bool> DeleteAsync(int no)
    {
        await EnsureNotFinalizedAsync(no);
        return await _repository.DeleteAsync(no);
    }

    #endregion

    #region Finalization Guard

    /// <summary>
    /// 마감 여부 확인 — 마감된 기록이면 예외 발생
    /// </summary>
    private async Task EnsureNotFinalizedAsync(int no)
    {
        if (no <= 0) return; // 신규 레코드는 체크 불필요

        var existing = await _repository.GetByIdAsync(no);
        if (existing != null && existing.IsFinalized)
        {
            throw new InvalidOperationException(
                "마감된 학생부 기록은 수정할 수 없습니다. 수정하려면 먼저 마감을 해제해주세요.");
        }
    }

    #endregion

    #region Statistics

    /// <summary>
    /// 영역별 통계 조회
    /// </summary>
    public async Task<Dictionary<string, int>> GetCountByTypeAsync(int year)
    {
        return await _repository.GetCountByTypeAsync(year);
    }

    /// <summary>
    /// 미마감 기록 통계
    /// </summary>
    public async Task<Dictionary<string, int>> GetDraftCountByTypeAsync()
    {
        return await _repository.GetDraftCountByTypeAsync();
    }

    #endregion

    #region Validation

    /// <summary>
    /// StudentSpecial 유효성 검사
    /// </summary>
    private void ValidateSpecial(StudentSpecial special)
    {
        if (special == null)
            throw new ArgumentNullException(nameof(special));

        if (string.IsNullOrWhiteSpace(special.StudentID))
            throw new ArgumentException("학생 ID가 필요합니다.");

        if (string.IsNullOrWhiteSpace(special.Type))
            throw new ArgumentException("학생부 영역이 필요합니다.");

        if (special.Year < 2000 || special.Year > 2100)
            throw new ArgumentException("올바른 학년도를 입력하세요.");
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (_disposed)
            return;

        _repository?.Dispose();
        _disposed = true;
    }

    #endregion
}

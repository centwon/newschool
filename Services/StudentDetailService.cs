using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Repositories;

namespace NewSchool.Services;

/// <summary>
/// StudentDetail Service (Repository 패턴 버전)
/// 학생 상세 정보 비즈니스 로직 관리
/// </summary>
public sealed class StudentDetailService : IDisposable
{
    private readonly StudentDetailRepository _detailRepo;
    private readonly StudentRepository _studentRepo;
    private bool _disposed;

    public StudentDetailService(string dbPath)
    {
        _detailRepo = new StudentDetailRepository(dbPath);
        _studentRepo = new StudentRepository(dbPath);
    }

    #region Create

    /// <summary>
    /// 학생 상세 정보 생성
    /// </summary>
    public async Task<int> CreateAsync(StudentDetail detail)
    {
        // 유효성 검증
        ValidateStudentDetail(detail);

        // 학생 존재 확인
        var student = await _studentRepo.GetByIdAsync(detail.StudentID);
        if (student == null)
        {
            throw new InvalidOperationException($"존재하지 않는 학생입니다: {detail.StudentID}");
        }

        // 이미 상세정보가 있는지 확인 (1:1 관계)
        var existing = await _detailRepo.GetByStudentIdAsync(detail.StudentID);
        if (existing != null)
        {
            throw new InvalidOperationException($"이미 상세정보가 존재합니다: {detail.StudentID}");
        }

        detail.CreatedAt = DateTime.Now;
        detail.UpdatedAt = DateTime.Now;

        return await _detailRepo.CreateAsync(detail);
    }

    /// <summary>
    /// 학생 상세 정보 생성 또는 업데이트 (Upsert)
    /// </summary>
    /// <returns>반영된 행의 No. 반영되지 않았으면 0.</returns>
    public async Task<int> CreateOrUpdateAsync(StudentDetail detail)
    {
        ValidateStudentDetail(detail);

        var existing = await _detailRepo.GetByStudentIdAsync(detail.StudentID);

        if (existing == null)
        {
            // 생성
            detail.CreatedAt = DateTime.Now;
            detail.UpdatedAt = DateTime.Now;
            return await _detailRepo.CreateAsync(detail);
        }
        else
        {
            // 업데이트
            detail.No = existing.No;
            detail.CreatedAt = existing.CreatedAt;
            detail.UpdatedAt = DateTime.Now;

            // 갱신 결과를 확인한다 — 예전에는 결과를 버리고 무조건 No 를 돌려줘서
            // 호출부가 반환값을 검사해도 실패를 알 수 없었다. 0 = 반영 안 됨.
            return await _detailRepo.UpdateAsync(detail) ? existing.No : 0;
        }
    }

    #endregion

    #region Read

    /// <summary>
    /// StudentID로 상세 정보 조회
    /// </summary>
    public async Task<StudentDetail?> GetByStudentIdAsync(string studentId)
    {
        if (string.IsNullOrWhiteSpace(studentId))
        {
            throw new ArgumentException("학생 ID는 필수입니다.", nameof(studentId));
        }

        return await _detailRepo.GetByStudentIdAsync(studentId);
    }

    /// <summary>
    /// 여러 StudentID로 상세 정보 일괄 조회
    /// </summary>
    public async Task<List<StudentDetail>> GetByStudentIdsAsync(List<string> studentIds)
    {
        if (studentIds == null || studentIds.Count == 0)
            return [];

        return await _detailRepo.GetByStudentIdsAsync(studentIds);
    }

    /// <summary>
    /// No로 상세 정보 조회
    /// </summary>
    public async Task<StudentDetail?> GetByNoAsync(int no)
    {
        if (no <= 0)
        {
            throw new ArgumentException("유효하지 않은 No입니다.", nameof(no));
        }

        return await _detailRepo.GetByNoAsync(no);
    }

    #endregion

    #region Update

    /// <summary>
    /// 학생 상세 정보 전체 업데이트
    /// </summary>
    public async Task<bool> UpdateAsync(StudentDetail detail)
    {
        ValidateStudentDetail(detail);

        // 기존 데이터 존재 확인
        var existing = await _detailRepo.GetByStudentIdAsync(detail.StudentID);
        if (existing == null)
        {
            throw new InvalidOperationException($"상세정보가 존재하지 않습니다: {detail.StudentID}");
        }

        detail.No = existing.No;
        detail.CreatedAt = existing.CreatedAt;
        detail.UpdatedAt = DateTime.Now;

        return await _detailRepo.UpdateAsync(detail);
    }

    // 항목별 부분 업데이트(부모·보호자·가족·교우·진로·건강)는 호출부가 없어 지웠다(39차).
    // 화면은 학생카드에서 한 번에 담아 UpdateAsync 로 통째로 저장한다.

    /// <summary>
    /// 메모 업데이트
    /// </summary>
    public async Task<bool> UpdateMemoAsync(string studentId, string memo)
    {
        var detail = await GetOrCreateDetailAsync(studentId);
        detail.Memo = memo;
        detail.UpdatedAt = DateTime.Now;

        return await _detailRepo.UpdateAsync(detail);
    }

    #endregion

    #region Delete

    /// <summary>
    /// 학생 상세 정보 삭제
    /// </summary>
    public async Task<bool> DeleteByStudentIdAsync(string studentId)
    {
        if (string.IsNullOrWhiteSpace(studentId))
        {
            throw new ArgumentException("학생 ID는 필수입니다.", nameof(studentId));
        }

        var detail = await _detailRepo.GetByStudentIdAsync(studentId);
        if (detail == null)
        {
            return false; // 이미 없음
        }

        return await _detailRepo.DeleteByStudentIdAsync(studentId);
    }

    // No 로 삭제(DeleteByNoAsync)와 완성도 체크(CheckCompletenessAsync + DetailCompleteness)는
    // 호출부가 없어 지웠다(39차). 삭제는 학생 기준(DeleteByStudentIdAsync) 하나로 충분하다.

    #endregion

    #region Helper Methods

    /// <summary>
    /// 상세정보 가져오기 또는 생성
    /// </summary>
    private async Task<StudentDetail> GetOrCreateDetailAsync(string studentId)
    {
        var detail = await _detailRepo.GetByStudentIdAsync(studentId);

        if (detail == null)
        {
            // 학생 존재 확인
            var student = await _studentRepo.GetByIdAsync(studentId);
            if (student == null)
            {
                throw new InvalidOperationException($"존재하지 않는 학생입니다: {studentId}");
            }

            // 새 상세정보 생성
            detail = new StudentDetail
            {
                StudentID = studentId,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };

            detail.No = await _detailRepo.CreateAsync(detail);
        }

        return detail;
    }

    /// <summary>
    /// 유효성 검증
    /// </summary>
    private void ValidateStudentDetail(StudentDetail detail)
    {
        if (detail == null)
        {
            throw new ArgumentNullException(nameof(detail));
        }

        if (string.IsNullOrWhiteSpace(detail.StudentID))
        {
            throw new ArgumentException("학생 ID는 필수입니다.");
        }

        if (detail.StudentID.Length != 15)
        {
            throw new ArgumentException("학생 ID는 15자리여야 합니다.");
        }
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (!_disposed)
        {
            _detailRepo?.Dispose();
            _studentRepo?.Dispose();
            _disposed = true;
        }
    }

    #endregion
}

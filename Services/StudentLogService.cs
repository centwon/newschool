using Microsoft.Data.Sqlite;
using NewSchool.Controls;
using NewSchool.Models;
using NewSchool.Repositories;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NewSchool.Services;

/// <summary>
/// StudentLog (누가기록) 서비스
/// Repository 패턴 적용으로 성능 최적화 (DB 연결 재사용)
/// </summary>
public class StudentLogService : IDisposable
{
    private readonly StudentLogRepository _repository;
    private bool _disposed;

    public StudentLogService()
    {
        _repository = new StudentLogRepository(SchoolDatabase.DbPath);
    }

    #region Create/Insert

    /// <summary>
    /// 누가기록 생성 (최적화됨 - Repository 사용)
    /// </summary>
    public async Task<int> InsertAsync(StudentLog log)
    {
        return await _repository.CreateAsync(log).ConfigureAwait(false);
    }

    #endregion

    #region Read/Select

    /// <summary>
    /// No로 조회 (최적화됨)
    /// </summary>
    public async Task<StudentLog?> GetByNoAsync(int no)
    {
        return await _repository.GetByIdAsync(no).ConfigureAwait(false);
    }

    /// <summary>
    /// 학생의 누가기록 조회 (최적화됨)
    /// semester = 0이면 해당 학년도 전체 기록 조회
    /// </summary>
    public async Task<List<StudentLog>> GetStudentLogsAsync(string studentId, int year, int semester = 0)
    {
        return await _repository.GetByStudentAsync(studentId, year, semester).ConfigureAwait(false);
    }

    /// <summary>
    /// 학생의 전체 누가기록 조회 (학년도/학기 무관)
    /// </summary>
    public async Task<List<StudentLog>> GetAllStudentLogsAsync(string studentId)
    {
        return await _repository.GetAllByStudentAsync(studentId).ConfigureAwait(false);
    }

    /// <summary>
    /// 카테고리별 조회 (최적화됨)
    /// </summary>
    public async Task<List<StudentLog>> GetLogsByCategoryAsync(
        string studentId,
        int year,
        LogCategory category)
    {
        return await _repository.GetByCategoryAsync(studentId, year, 0, category).ConfigureAwait(false);
    }

    /// <summary>
    /// 기간별 조회 (최적화됨)
    /// </summary>
    public async Task<List<StudentLog>> GetLogsByDateRangeAsync(
        string studentId,
        DateTime startDate,
        DateTime endDate)
    {
        return await _repository.GetByDateRangeAsync(
            studentId,
            startDate.ToString("yyyy-MM-dd"),
            endDate.ToString("yyyy-MM-dd")
        ).ConfigureAwait(false);
    }

    /// <summary>
    /// 학년 반별 기록 조회 (최적화됨 - 단일 JOIN 쿼리)
    /// </summary>
    public static async Task<List<StudentLog>> GetByClassAsync(
        string schoolCode, int year, int grade, int classroom, DateTime date)
    {
        System.Diagnostics.Debug.WriteLine($"[StudentLogService] GetByClassAsync: SchoolCode={schoolCode}, Year={year}, Grade={grade}, Class={classroom}, Date={date:yyyy-MM-dd}");

        using var repo = new StudentLogRepository(SchoolDatabase.DbPath);

        // Enrollment와 JOIN하는 단일 쿼리로 효율적 조회
        var logs = await repo.GetByClassAndDateAsync(
            schoolCode, year, grade, classroom, date
        ).ConfigureAwait(false);

        System.Diagnostics.Debug.WriteLine($"[StudentLogService] 조회 완료: {logs.Count}건");

        return logs;
    }

    /// <summary>
    /// 학년 반별 기간 기록 조회 (최적화됨 - 단일 JOIN 쿼리)
    /// </summary>
    public static async Task<List<StudentLog>> GetByClassAndDateRangeAsync(
        string schoolCode, int year, int grade, int classroom, DateTime startDate, DateTime endDate)
    {
        System.Diagnostics.Debug.WriteLine($"[StudentLogService] GetByClassAndDateRangeAsync: {year}년 {grade}학년 {classroom}반, {startDate:yyyy-MM-dd} ~ {endDate:yyyy-MM-dd}");

        using var repo = new StudentLogRepository(SchoolDatabase.DbPath);

        var logs = await repo.GetByClassAndDateRangeAsync(
            schoolCode, year, grade, classroom, startDate, endDate
        ).ConfigureAwait(false);

        System.Diagnostics.Debug.WriteLine($"[StudentLogService] 기간 조회 완료: {logs.Count}건");

        return logs;
    }

    #endregion

    #region Update

    /// <summary>
    /// 누가기록 업데이트 (최적화됨)
    /// </summary>
    public async Task<bool> UpdateAsync(StudentLog log)
    {
        return await _repository.UpdateAsync(log).ConfigureAwait(false);
    }

    #endregion

    #region Delete

    /// <summary>
    /// 누가기록 삭제 (최적화됨)
    /// </summary>
    public async Task<bool> DeleteAsync(int no)
    {
        return await _repository.DeleteAsync(no).ConfigureAwait(false);
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
                _repository?.Dispose();
            }
            _disposed = true;
        }
    }

    #endregion
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Repositories;

namespace NewSchool.Services;

/// <summary>
/// 업무 일지 서비스
/// </summary>
public class WorkLogService : IDisposable
{
    private readonly WorkLogRepository _repo;
    private bool _disposed;

    public WorkLogService()
    {
        _repo = new WorkLogRepository(SchoolDatabase.DbPath);
    }

    public async Task<int> InsertAsync(WorkLog log) => await _repo.InsertAsync(log);
    public async Task<WorkLog?> GetByNoAsync(int no) => await _repo.GetByNoAsync(no);
    public async Task<List<WorkLog>> GetByDateAsync(string teacherId, DateTime date) => await _repo.GetByDateAsync(teacherId, date);
    public async Task<List<WorkLog>> GetByRangeAsync(string teacherId, DateTime start, DateTime end) => await _repo.GetByTeacherAsync(teacherId, start, end);
    public async Task<List<WorkLog>> SearchAsync(string teacherId, string keyword) => await _repo.SearchAsync(teacherId, keyword);
    public async Task UpdateAsync(WorkLog log) => await _repo.UpdateAsync(log);
    public async Task DeleteAsync(int no) => await _repo.DeleteAsync(no);

    public void Dispose()
    {
        if (!_disposed) { _repo.Dispose(); _disposed = true; }
    }
}

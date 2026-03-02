using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Repositories;

namespace NewSchool.Services;

/// <summary>
/// 학교 일정 서비스
/// </summary>
public class SchoolEventService : IDisposable
{
    private readonly SchoolEventRepository _repo;
    private bool _disposed;

    public SchoolEventService()
    {
        _repo = new SchoolEventRepository(SchoolDatabase.DbPath);
    }

    public async Task<int> InsertAsync(SchoolEvent evt) => await _repo.InsertAsync(evt);
    public async Task<SchoolEvent?> GetByNoAsync(int no) => await _repo.GetByNoAsync(no);
    public async Task<List<SchoolEvent>> GetByMonthAsync(string teacherId, int year, int month) => await _repo.GetByMonthAsync(teacherId, year, month);
    public async Task<List<SchoolEvent>> GetByDateAsync(string teacherId, DateTime date) => await _repo.GetByDateAsync(teacherId, date);
    public async Task<List<SchoolEvent>> GetUpcomingAsync(string teacherId, int limit = 10) => await _repo.GetUpcomingAsync(teacherId, limit);
    public async Task UpdateAsync(SchoolEvent evt) => await _repo.UpdateAsync(evt);
    public async Task DeleteAsync(int no) => await _repo.DeleteAsync(no);

    public void Dispose()
    {
        if (!_disposed) { _repo.Dispose(); _disposed = true; }
    }
}

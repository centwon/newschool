using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Repositories;

namespace NewSchool.Services;

/// <summary>
/// 할 일 서비스
/// </summary>
public class TodoService : IDisposable
{
    private readonly TodoItemRepository _repo;
    private bool _disposed;

    public TodoService()
    {
        _repo = new TodoItemRepository(SchoolDatabase.DbPath);
    }

    public async Task<int> InsertAsync(TodoItem item) => await _repo.InsertAsync(item);
    public async Task<TodoItem?> GetByNoAsync(int no) => await _repo.GetByNoAsync(no);
    public async Task<List<TodoItem>> GetActiveAsync(string teacherId) => await _repo.GetActiveAsync(teacherId);
    public async Task<List<TodoItem>> GetCompletedAsync(string teacherId, int limit = 50) => await _repo.GetCompletedAsync(teacherId, limit);
    public async Task<List<TodoItem>> GetAllAsync(string teacherId) => await _repo.GetAllAsync(teacherId);
    public async Task<List<TodoItem>> GetOverdueAsync(string teacherId) => await _repo.GetOverdueAsync(teacherId);
    public async Task UpdateAsync(TodoItem item) => await _repo.UpdateAsync(item);
    public async Task DeleteAsync(int no) => await _repo.DeleteAsync(no);
    public async Task<int> DeleteCompletedAsync(string teacherId) => await _repo.DeleteCompletedAsync(teacherId);

    public void Dispose()
    {
        if (!_disposed) { _repo.Dispose(); _disposed = true; }
    }
}

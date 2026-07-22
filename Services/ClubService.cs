using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Repositories;

namespace NewSchool.Services;

internal class ClubService:IDisposable
{
    private ClubRepository _clubRepository;
    private ClubEnrollmentRepository _clubEnrollmentRepository;
    private bool _disposed;

    public ClubService()
    {
        _clubRepository = new ClubRepository(SchoolDatabase.DbPath);
        _clubEnrollmentRepository = new ClubEnrollmentRepository(SchoolDatabase.DbPath);

    }
    public async Task<List<Club>> GetAllClubsAsync(string schoolCode, int year)
    {
        return await _clubRepository.GetBySchoolAsync(schoolCode, year);

    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _clubRepository.Dispose();
            _clubEnrollmentRepository.Dispose();
            _disposed = true;
        }
    }
}

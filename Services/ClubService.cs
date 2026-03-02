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

    // // TODO: 비관리형 리소스를 해제하는 코드가 'Dispose(bool disposing)'에 포함된 경우에만 종료자를 재정의합니다.
    // ~ClubService()
    // {
    //     // 이 코드를 변경하지 마세요. 'Dispose(bool disposing)' 메서드에 정리 코드를 입력합니다.
    //     Dispose(disposing: false);
    // }

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

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Repositories;

namespace NewSchool.Services
{
    /// <summary>
    /// School Service — 학교 한 곳의 정보를 저장하고 코드로 되읽는다.
    /// </summary>
    public sealed class SchoolService : IDisposable
    {
        private readonly string _dbPath;
        private bool _disposed;

        public SchoolService(string dbPath)
        {
            _dbPath = dbPath;
        }

        #region 학교 기본 관리

        /// <summary>
        /// 학교 저장 (Upsert: 있으면 업데이트, 없으면 생성)
        /// </summary>
        public async Task<School> SaveSchoolAsync(School school)
        {
            using var repo = new SchoolRepository(_dbPath);
            var existing = await repo.GetBySchoolCodeAsync(school.SchoolCode);

            if (existing != null)
            {
                existing.SchoolName = school.SchoolName;
                existing.ATPT_OFCDC_SC_CODE = school.ATPT_OFCDC_SC_CODE;
                existing.ATPT_OFCDC_SC_NAME = school.ATPT_OFCDC_SC_NAME;
                existing.SchoolType = school.SchoolType;
                existing.Address = school.Address;
                existing.Phone = school.Phone;
                existing.Fax = school.Fax;
                existing.Website = school.Website;
                existing.FoundationDate = school.FoundationDate;
                existing.IsActive = true;
                existing.UpdatedAt = DateTime.Now;

                // 반영 여부를 확인한다 — 예전에는 결과를 버려서, 저장이 실패해도
                // 호출부(초기 설정·설정 화면)는 성공으로 넘어갔고
                // 설정>학교정보는 계속 옛 정보를 보여줬다.
                if (!await repo.UpdateAsync(existing))
                    throw new InvalidOperationException(
                        $"학교 정보({school.SchoolCode})가 갱신되지 않았습니다.");

                return existing;
            }
            else
            {
                school.IsActive = true;
                school.IsDeleted = false;
                school.CreatedAt = DateTime.Now;
                school.UpdatedAt = DateTime.Now;

                school.No = await repo.CreateAsync(school);
                if (school.No <= 0)
                    throw new InvalidOperationException(
                        $"학교 정보({school.SchoolCode})가 저장되지 않았습니다.");

                return school;
            }
        }

        #endregion

        #region 학교 조회

        /// <summary>
        /// SchoolCode로 학교 조회
        /// </summary>
        public async Task<School?> GetSchoolByCodeAsync(string schoolCode)
        {
            using var schoolRepo = new SchoolRepository(_dbPath);

            try
            {
                return await schoolRepo.GetBySchoolCodeAsync(schoolCode);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SchoolService] 학교 조회 실패: {ex.Message}");
                return null;
            }
        }

        // 학교 등록·수정·폐교·삭제, No 조회, 목록/검색(활성·시도교육청·종류·키워드), 학생 수 통계
        // 세 가지는 호출부가 한 곳도 없어 지웠다(39차). 이 앱은 학교 한 곳만 다루므로
        // 남은 것은 초기 설정이 쓰는 SaveSchoolAsync·GetSchoolByCodeAsync 뿐이다.

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
            }
        }

        #endregion
    }
}

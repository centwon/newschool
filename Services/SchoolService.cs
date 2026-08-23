using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Repositories;

namespace NewSchool.Services
{
    /// <summary>
    /// School Service
    /// 학교 정보 관리 및 통계
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

        /// <summary>
        /// 학교 등록
        /// </summary>
        public async Task<(bool Success, string Message, int SchoolNo)> CreateSchoolAsync(School school)
        {
            using var schoolRepo = new SchoolRepository(_dbPath);

            try
            {
                // SchoolCode 중복 확인
                var existing = await schoolRepo.GetBySchoolCodeAsync(school.SchoolCode);
                if (existing != null)
                {
                    return (false, "이미 등록된 학교 코드입니다.", -1);
                }

                // 학교 생성
                int no = await schoolRepo.CreateAsync(school);

                return (true, "학교가 등록되었습니다.", no);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SchoolService] 학교 등록 실패: {ex.Message}");
                return (false, $"학교 등록 중 오류가 발생했습니다: {ex.Message}", -1);
            }
        }

        /// <summary>
        /// 학교 정보 수정
        /// </summary>
        public async Task<(bool Success, string Message)> UpdateSchoolAsync(School school)
        {
            using var schoolRepo = new SchoolRepository(_dbPath);

            try
            {
                var success = await schoolRepo.UpdateAsync(school);
                return success
                    ? (true, "학교 정보가 수정되었습니다.")
                    : (false, "학교 정보 수정에 실패했습니다.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SchoolService] 학교 정보 수정 실패: {ex.Message}");
                return (false, $"학교 정보 수정 중 오류가 발생했습니다: {ex.Message}");
            }
        }

        /// <summary>
        /// 학교 폐교 처리
        /// </summary>
        public async Task<(bool Success, string Message)> CloseSchoolAsync(int schoolNo)
        {
            using var schoolRepo = new SchoolRepository(_dbPath);

            try
            {
                var success = await schoolRepo.UpdateIsActiveAsync(schoolNo, false);
                return success
                    ? (true, "학교가 폐교 처리되었습니다.")
                    : (false, "학교 폐교 처리에 실패했습니다.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SchoolService] 학교 폐교 처리 실패: {ex.Message}");
                return (false, $"학교 폐교 처리 중 오류가 발생했습니다: {ex.Message}");
            }
        }

        /// <summary>
        /// 학교 삭제
        /// </summary>
        public async Task<(bool Success, string Message)> DeleteSchoolAsync(int schoolNo)
        {
            using var schoolRepo = new SchoolRepository(_dbPath);

            try
            {
                var success = await schoolRepo.DeleteAsync(schoolNo);
                return success
                    ? (true, "학교가 삭제되었습니다.")
                    : (false, "학교 삭제에 실패했습니다.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SchoolService] 학교 삭제 실패: {ex.Message}");
                return (false, $"학교 삭제 중 오류가 발생했습니다: {ex.Message}");
            }
        }

        #endregion

        #region 학교 조회

        /// <summary>
        /// No로 학교 조회
        /// </summary>
        public async Task<School?> GetSchoolByNoAsync(int no)
        {
            using var schoolRepo = new SchoolRepository(_dbPath);

            try
            {
                return await schoolRepo.GetByNoAsync(no);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SchoolService] 학교 조회 실패: {ex.Message}");
                return null;
            }
        }

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

        /// <summary>
        /// 모든 활성 학교 목록 조회
        /// </summary>
        public async Task<List<School>> GetAllActiveSchoolsAsync()
        {
            using var schoolRepo = new SchoolRepository(_dbPath);

            try
            {
                return await schoolRepo.GetAllActiveAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SchoolService] 학교 목록 조회 실패: {ex.Message}");
                return new List<School>();
            }
        }

        /// <summary>
        /// 시도교육청별 학교 목록 조회
        /// </summary>
        public async Task<List<School>> GetSchoolsByAtptCodeAsync(string atptCode)
        {
            using var schoolRepo = new SchoolRepository(_dbPath);

            try
            {
                return await schoolRepo.GetByAtptCodeAsync(atptCode);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SchoolService] 시도교육청별 학교 조회 실패: {ex.Message}");
                return new List<School>();
            }
        }

        /// <summary>
        /// 학교 종류별 조회
        /// </summary>
        public async Task<List<School>> GetSchoolsByTypeAsync(string schoolType)
        {
            using var schoolRepo = new SchoolRepository(_dbPath);

            try
            {
                return await schoolRepo.GetBySchoolTypeAsync(schoolType);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SchoolService] 학교 종류별 조회 실패: {ex.Message}");
                return new List<School>();
            }
        }

        /// <summary>
        /// 학교 검색
        /// </summary>
        public async Task<List<School>> SearchSchoolsAsync(string keyword)
        {
            using var schoolRepo = new SchoolRepository(_dbPath);

            try
            {
                return await schoolRepo.SearchAsync(keyword);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SchoolService] 학교 검색 실패: {ex.Message}");
                return new List<School>();
            }
        }

        #endregion

        #region 학교 통계

        /// <summary>
        /// 학교의 학생 수 조회
        /// </summary>
        public async Task<int> GetStudentCountAsync(string schoolCode, int year, int semester)
        {
            using var enrollmentRepo = new EnrollmentRepository(_dbPath);

            try
            {
                var enrollments = await enrollmentRepo.GetBySchoolAndYearAsync(schoolCode, year, semester);
                return enrollments.Count;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SchoolService] 학생 수 조회 실패: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// 학교의 학년별 학생 수 통계
        /// </summary>
        public async Task<Dictionary<int, int>> GetStudentCountByGradeAsync(
            string schoolCode, int year, int semester)
        {
            using var enrollmentRepo = new EnrollmentRepository(_dbPath);

            try
            {
                var enrollments = await enrollmentRepo.GetBySchoolAndYearAsync(schoolCode, year, semester);

                return enrollments
                    .GroupBy(e => e.Grade)
                    .ToDictionary(g => g.Key, g => g.Count());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SchoolService] 학년별 학생 수 조회 실패: {ex.Message}");
                return new Dictionary<int, int>();
            }
        }

        /// <summary>
        /// 학교의 반별 학생 수 통계
        /// </summary>
        public async Task<Dictionary<(int Grade, int Class), int>> GetStudentCountByClassAsync(
            string schoolCode, int year, int semester)
        {
            using var enrollmentRepo = new EnrollmentRepository(_dbPath);

            try
            {
                var enrollments = await enrollmentRepo.GetBySchoolAndYearAsync(schoolCode, year, semester);

                return enrollments
                    .GroupBy(e => (e.Grade, e.Class))
                    .ToDictionary(g => g.Key, g => g.Count());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SchoolService] 반별 학생 수 조회 실패: {ex.Message}");
                return new Dictionary<(int, int), int>();
            }
        }

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

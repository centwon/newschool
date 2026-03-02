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
    public class SchoolService : IDisposable
    {
        private readonly string _dbPath;
        private bool _disposed;

        public SchoolService(string dbPath)
        {
            _dbPath = dbPath;
        }

        #region 학교 기본 관리

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

        /// <summary>
        /// 학교의 교사 수 조회
        /// </summary>
        public async Task<int> GetTeacherCountAsync(string schoolCode)
        {
            using var historyRepo = new TeacherSchoolHistoryRepository(_dbPath);

            try
            {
                var histories = await historyRepo.GetCurrentBySchoolCodeAsync(schoolCode);
                return histories.Count;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SchoolService] 교사 수 조회 실패: {ex.Message}");
                return 0;
            }
        }

        #endregion

        #region NEIS 연동 (향후 확장)

        /// <summary>
        /// NEIS에서 학교 정보 가져오기 (향후 구현)
        /// </summary>
        public async Task<(bool Success, string Message)> ImportFromNEISAsync(string schoolCode)
        {
            // TODO: NEIS Open API 연동
            await Task.Delay(0);
            return (false, "NEIS 연동 기능은 아직 구현되지 않았습니다.");
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

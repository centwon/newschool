using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Repositories;

namespace NewSchool.Services
{
    /// <summary>
    /// StudentDetail Service (Repository 패턴 버전)
    /// 학생 상세 정보 비즈니스 로직 관리
    /// </summary>
    public class StudentDetailService : IDisposable
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
                await _detailRepo.UpdateAsync(detail);
                return existing.No;
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

        /// <summary>
        /// 주 보호자 연락처 가져오기
        /// 우선순위: 어머니 → 아버지 → 보호자
        /// </summary>
        public async Task<string?> GetPrimaryGuardianPhoneAsync(string studentId)
        {
            var detail = await _detailRepo.GetByStudentIdAsync(studentId);
            return detail?.GetPrimaryContact();
        }

        /// <summary>
        /// 주 보호자명 가져오기
        /// </summary>
        public async Task<string?> GetPrimaryGuardianNameAsync(string studentId)
        {
            var detail = await _detailRepo.GetByStudentIdAsync(studentId);
            return detail?.GetPrimaryGuardianName();
        }

        /// <summary>
        /// 특이사항 여부 확인
        /// </summary>
        public async Task<bool> HasSpecialConsiderationsAsync(string studentId)
        {
            var detail = await _detailRepo.GetByStudentIdAsync(studentId);
            return detail?.HasSpecialConsiderations() ?? false;
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

        /// <summary>
        /// 부모 정보 업데이트
        /// </summary>
        public async Task<bool> UpdateParentInfoAsync(
            string studentId,
            string? fatherName = null,
            string? fatherPhone = null,
            string? fatherJob = null,
            string? motherName = null,
            string? motherPhone = null,
            string? motherJob = null)
        {
            var detail = await GetOrCreateDetailAsync(studentId);

            // 변경된 항목만 업데이트
            if (fatherName != null) detail.FatherName = fatherName;
            if (fatherPhone != null) detail.FatherPhone = fatherPhone;
            if (fatherJob != null) detail.FatherJob = fatherJob;
            if (motherName != null) detail.MotherName = motherName;
            if (motherPhone != null) detail.MotherPhone = motherPhone;
            if (motherJob != null) detail.MotherJob = motherJob;

            detail.UpdatedAt = DateTime.Now;
            return await _detailRepo.UpdateAsync(detail);
        }

        /// <summary>
        /// 보호자 정보 업데이트
        /// </summary>
        public async Task<bool> UpdateGuardianInfoAsync(
            string studentId,
            string? guardianName = null,
            string? guardianPhone = null,
            string? guardianRelation = null)
        {
            var detail = await GetOrCreateDetailAsync(studentId);

            if (guardianName != null) detail.GuardianName = guardianName;
            if (guardianPhone != null) detail.GuardianPhone = guardianPhone;
            if (guardianRelation != null) detail.GuardianRelation = guardianRelation;

            detail.UpdatedAt = DateTime.Now;
            return await _detailRepo.UpdateAsync(detail);
        }

        /// <summary>
        /// 가족 정보 업데이트
        /// </summary>
        public async Task<bool> UpdateFamilyInfoAsync(string studentId, string familyInfo)
        {
            var detail = await GetOrCreateDetailAsync(studentId);
            detail.FamilyInfo = familyInfo;
            detail.UpdatedAt = DateTime.Now;

            return await _detailRepo.UpdateAsync(detail);
        }

        /// <summary>
        /// 교우관계 업데이트
        /// </summary>
        public async Task<bool> UpdateFriendsAsync(string studentId, string friends)
        {
            var detail = await GetOrCreateDetailAsync(studentId);
            detail.Friends = friends;
            detail.UpdatedAt = DateTime.Now;

            return await _detailRepo.UpdateAsync(detail);
        }

        /// <summary>
        /// 진로 정보 업데이트
        /// </summary>
        public async Task<bool> UpdateCareerInfoAsync(
            string studentId,
            string? interests = null,
            string? talents = null,
            string? careerGoal = null)
        {
            var detail = await GetOrCreateDetailAsync(studentId);

            if (interests != null) detail.Interests = interests;
            if (talents != null) detail.Talents = talents;
            if (careerGoal != null) detail.CareerGoal = careerGoal;

            detail.UpdatedAt = DateTime.Now;
            return await _detailRepo.UpdateAsync(detail);
        }

        /// <summary>
        /// 건강 정보 업데이트
        /// </summary>
        public async Task<bool> UpdateHealthInfoAsync(
            string studentId,
            string? healthInfo = null,
            string? allergies = null,
            string? specialNeeds = null)
        {
            var detail = await GetOrCreateDetailAsync(studentId);

            if (healthInfo != null) detail.HealthInfo = healthInfo;
            if (allergies != null) detail.Allergies = allergies;
            if (specialNeeds != null) detail.SpecialNeeds = specialNeeds;

            detail.UpdatedAt = DateTime.Now;
            return await _detailRepo.UpdateAsync(detail);
        }

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

        /// <summary>
        /// No로 삭제
        /// </summary>
        public async Task<bool> DeleteByNoAsync(int no)
        {
            if (no <= 0)
            {
                throw new ArgumentException("유효하지 않은 No입니다.", nameof(no));
            }

            return await _detailRepo.DeleteAsync(no);
        }

        #endregion

        #region 통계 및 분석

        /// <summary>
        /// 상세정보 완성도 체크
        /// </summary>
        public async Task<DetailCompleteness> CheckCompletenessAsync(string studentId)
        {
            var detail = await _detailRepo.GetByStudentIdAsync(studentId);

            if (detail == null)
            {
                return new DetailCompleteness
                {
                    StudentID = studentId,
                    HasDetail = false
                };
            }

            return new DetailCompleteness
            {
                StudentID = studentId,
                HasDetail = true,
                HasParentInfo = !string.IsNullOrWhiteSpace(detail.FatherName) ||
                               !string.IsNullOrWhiteSpace(detail.MotherName),
                HasGuardianPhone = !string.IsNullOrWhiteSpace(detail.GetPrimaryContact()),
                HasFamilyInfo = !string.IsNullOrWhiteSpace(detail.FamilyInfo),
                HasFriends = !string.IsNullOrWhiteSpace(detail.Friends),
                HasCareerInfo = !string.IsNullOrWhiteSpace(detail.CareerGoal),
                HasHealthInfo = !string.IsNullOrWhiteSpace(detail.HealthInfo) ||
                               !string.IsNullOrWhiteSpace(detail.Allergies),
                HasSpecialNeeds = !string.IsNullOrWhiteSpace(detail.SpecialNeeds)
            };
        }

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

    #region ViewModel

    /// <summary>
    /// 상세정보 완성도 체크 결과
    /// </summary>
    public class DetailCompleteness
    {
        public string StudentID { get; set; } = string.Empty;
        public bool HasDetail { get; set; }
        public bool HasParentInfo { get; set; }
        public bool HasGuardianPhone { get; set; }
        public bool HasFamilyInfo { get; set; }
        public bool HasFriends { get; set; }
        public bool HasCareerInfo { get; set; }
        public bool HasHealthInfo { get; set; }
        public bool HasSpecialNeeds { get; set; }

        /// <summary>
        /// 완성도 퍼센트 (0~100)
        /// </summary>
        public int CompletenessPercentage
        {
            get
            {
                if (!HasDetail) return 0;

                int total = 7;
                int completed = 0;

                if (HasParentInfo) completed++;
                if (HasGuardianPhone) completed++;
                if (HasFamilyInfo) completed++;
                if (HasFriends) completed++;
                if (HasCareerInfo) completed++;
                if (HasHealthInfo) completed++;
                if (HasSpecialNeeds) completed++;

                return (int)((completed / (double)total) * 100);
            }
        }

        public override string ToString()
        {
            return $"상세정보 완성도: {CompletenessPercentage}%";
        }
    }

    #endregion
}

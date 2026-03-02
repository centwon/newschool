using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Repositories;

namespace NewSchool.Services
{
    /// <summary>
    /// Student Service
    /// 학생 정보 통합 관리 (Student + Enrollment + StudentDetail)
    /// </summary>
    public class StudentService : IDisposable
    {
        private readonly StudentRepository _studentRepo;
        private readonly EnrollmentRepository _enrollmentRepo;
        private readonly StudentDetailRepository _detailRepo;
        private bool _disposed;

        public StudentService(string dbPath)
        {
            _studentRepo = new StudentRepository(dbPath);
            _enrollmentRepo = new EnrollmentRepository(dbPath);
            _detailRepo = new StudentDetailRepository(dbPath);
        }

        #region 학생 등록

        /// <summary>
        /// 학생 통합 등록 (Student + Enrollment + Detail을 한 번에)
        /// 신입생 등록 시 사용
        /// </summary>
        public async Task<string> RegisterNewStudentAsync(
            Student student,
            Enrollment enrollment,
            StudentDetail? detail = null)
        {
            // 유효성 검증
            ValidateStudent(student);
            ValidateEnrollment(enrollment);

            // StudentID 일치 확인
            if (enrollment.StudentID != student.StudentID)
            {
                throw new ArgumentException("학생 ID가 일치하지 않습니다.");
            }

            if (detail != null && detail.StudentID != student.StudentID)
            {
                throw new ArgumentException("학생 상세정보의 ID가 일치하지 않습니다.");
            }

            try
            {
                _studentRepo.BeginTransaction();

                // 1. Student 생성
                await _studentRepo.CreateAsync(student);

                // 2. Enrollment 생성 (Student 정보 복사)
                enrollment.Name = student.Name;      // denormalize
                enrollment.Sex = student.Sex;        // denormalize
                enrollment.Photo = student.Photo;    // denormalize
                enrollment.CreatedAt = DateTime.Now;
                enrollment.UpdatedAt = DateTime.Now;
                await _enrollmentRepo.CreateAsync(enrollment);

                // 3. StudentDetail 생성 (선택적)
                if (detail != null)
                {
                    detail.CreatedAt = DateTime.Now;
                    detail.UpdatedAt = DateTime.Now;
                    await _detailRepo.CreateAsync(detail);
                }

                _studentRepo.Commit();
                return student.StudentID;
            }
            catch
            {
                _studentRepo.Rollback();
                throw;
            }
        }

        /// <summary>
        /// StudentID 자동 생성
        /// 형식: 학교코드(7) + 입학년도(4) + 일련번호(4) = 15자리
        /// </summary>
        public string GenerateStudentID(string schoolCode, int admissionYear, int sequenceNumber)
        {
            if (schoolCode.Length != 7)
            {
                throw new ArgumentException("학교 코드는 7자리여야 합니다.");
            }

            if (admissionYear < 2000 || admissionYear > 2100)
            {
                throw new ArgumentException("입학년도가 올바르지 않습니다.");
            }

            if (sequenceNumber < 1 || sequenceNumber > 9999)
            {
                throw new ArgumentException("일련번호는 1~9999 사이여야 합니다.");
            }

            return $"{schoolCode}{admissionYear}{sequenceNumber:D4}";
        }

        #endregion

        #region 학생 정보 조회

        /// <summary>
        /// 학생 전체 정보 조회 (Student + Enrollment + Detail 통합)
        /// Phone, Email, Address, BirthDate 등 상세 정보가 필요한 경우 사용
        /// 일반 명렉표는 GetCurrentEnrollmentAsync() 사용 권장
        /// </summary>
        public async Task<(Enrollment? enrollment, Student? student, StudentDetail? detail)> GetFullInfoAsync(string studentId)
        {
            // 1. Student 기본정보 조회
            var student = await _studentRepo.GetByIdAsync(studentId);
            if (student == null)
                return (null, null, null);

            // 2. 현재 Enrollment 조회
            var enrollment = await _enrollmentRepo.GetCurrentByStudentIdAsync(studentId);
            if (enrollment == null)
                return (null, student, null);

            // 3. StudentDetail 조회 (선택적)
            var detail = await _detailRepo.GetByStudentIdAsync(studentId);

            return (enrollment, student, detail);
        }

        /// <summary>
        /// 학생 기본정보만 조회
        /// </summary>
        public async Task<Student?> GetBasicInfoAsync(string studentId)
        {
            return await _studentRepo.GetByIdAsync(studentId);
        }

        /// <summary>
        /// 학생 상세정보만 조회
        /// </summary>
        public async Task<StudentDetail?> GetDetailInfoAsync(string studentId)
        {
            return await _detailRepo.GetByStudentIdAsync(studentId);
        }

        /// <summary>
        /// 학생 검색 (이름으로)
        /// </summary>
        public async Task<List<Student>> SearchByNameAsync(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("검색어를 입력하세요.");
            }

            return await _studentRepo.SearchByNameAsync(name);
        }

        /// <summary>
        /// 학생 목록 조회 (여러 ID)
        /// </summary>
        public async Task<List<Student>> GetStudentsByIdsAsync(List<string> studentIds)
        {
            if (studentIds == null || studentIds.Count == 0)
                return new List<Student>();

            return await _studentRepo.GetByIdsAsync(studentIds);
        }

        #endregion

        #region 학생 정보 수정

        /// <summary>
        /// 학생 기본정보 수정
        /// Name, Sex, Photo 변경 시 StudentRepository에서 자동으로 Enrollment 동기화 처리
        /// </summary>
        public async Task<bool> UpdateBasicInfoAsync(Student student)
        {
            ValidateStudent(student);

            student.UpdatedAt = DateTime.Now;
            // StudentRepository.UpdateAsync()에서 Enrollment 동기화 자동 처리
            return await _studentRepo.UpdateAsync(student);
        }

        /// <summary>
        /// 학생 상세정보 수정
        /// </summary>
        public async Task<bool> UpdateDetailInfoAsync(StudentDetail detail)
        {
            if (string.IsNullOrEmpty(detail.StudentID))
            {
                throw new ArgumentException("학생 ID는 필수입니다.");
            }

            detail.UpdatedAt = DateTime.Now;
            return await _detailRepo.UpdateAsync(detail);
        }

        /// <summary>
        /// 학생 연락처 정보만 수정
        /// </summary>
        public async Task<bool> UpdateContactInfoAsync(
            string studentId,
            string? phone = null,
            string? email = null,
            string? address = null)
        {
            var student = await _studentRepo.GetByIdAsync(studentId);
            if (student == null)
            {
                throw new InvalidOperationException($"존재하지 않는 학생입니다: {studentId}");
            }

            // 변경된 항목만 업데이트
            if (phone != null) student.Phone = phone;
            if (email != null) student.Email = email;
            if (address != null) student.Address = address;

            student.UpdatedAt = DateTime.Now;
            return await _studentRepo.UpdateAsync(student);
        }

        /// <summary>
        /// 보호자 연락처 수정
        /// </summary>
        public async Task<bool> UpdateGuardianContactAsync(
            string studentId,
            string? fatherPhone = null,
            string? motherPhone = null,
            string? guardianPhone = null)
        {
            var detail = await _detailRepo.GetByStudentIdAsync(studentId);

            // 상세정보가 없으면 생성
            if (detail == null)
            {
                detail = new StudentDetail
                {
                    StudentID = studentId,
                    FatherPhone = fatherPhone ?? "",
                    MotherPhone = motherPhone ?? "",
                    GuardianPhone = guardianPhone ?? "",
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };
                await _detailRepo.CreateAsync(detail);
                return true;
            }

            // 변경된 항목만 업데이트
            if (fatherPhone != null) detail.FatherPhone = fatherPhone;
            if (motherPhone != null) detail.MotherPhone = motherPhone;
            if (guardianPhone != null) detail.GuardianPhone = guardianPhone;

            detail.UpdatedAt = DateTime.Now;
            return await _detailRepo.UpdateAsync(detail);
        }

        #endregion

        #region 학생 삭제

        /// <summary>
        /// 학생 삭제 (논리 삭제)
        /// </summary>
        public async Task<bool> DeleteStudentAsync(string studentId)
        {
            var student = await _studentRepo.GetByIdAsync(studentId);
            if (student == null)
            {
                throw new InvalidOperationException($"존재하지 않는 학생입니다: {studentId}");
            }

            // 재학 중인 학적이 있는지 확인
            var enrollment = await _enrollmentRepo.GetCurrentByStudentIdAsync(studentId);
            if (enrollment != null && (enrollment.Status == "재학" && !enrollment.IsDeleted))
            {
                throw new InvalidOperationException(
                    $"재학 중인 학생은 삭제할 수 없습니다. 먼저 전출 또는 졸업 처리하세요.");
            }

            return await _studentRepo.DeleteAsync(studentId);
        }

        #endregion

        #region 통계

        /// <summary>
        /// 전체 학생 수 조회
        /// </summary>
        public async Task<int> GetTotalStudentCountAsync()
        {
            return await _studentRepo.GetCountAsync();
        }

        /// <summary>
        /// 학생 정보 완성도 확인
        /// </summary>
        public async Task<StudentInfoCompleteness> CheckCompletenessAsync(string studentId)
        {
            var result = new StudentInfoCompleteness
            {
                StudentID = studentId
            };

            // 기본정보 확인
            var student = await _studentRepo.GetByIdAsync(studentId);
            result.HasBasicInfo = student != null;

            if (student != null)
            {
                result.HasPhone = !string.IsNullOrEmpty(student.Phone);
                result.HasEmail = !string.IsNullOrEmpty(student.Email);
                result.HasAddress = !string.IsNullOrEmpty(student.Address);
            }

            // 상세정보 확인
            var detail = await _detailRepo.GetByStudentIdAsync(studentId);
            result.HasDetailInfo = detail != null;

            if (detail != null)
            {
                result.HasGuardianInfo =
                    !string.IsNullOrEmpty(detail.FatherName) ||
                    !string.IsNullOrEmpty(detail.MotherName) ||
                    !string.IsNullOrEmpty(detail.GuardianName);
            }

            // 학적정보 확인
            var enrollments = await _enrollmentRepo.GetHistoryByStudentIdAsync(studentId);
            result.HasEnrollment = enrollments.Count > 0;

            return result;
        }

        #endregion

        #region 유효성 검증

        private void ValidateStudent(Student student)
        {
            if (string.IsNullOrEmpty(student.StudentID))
                throw new ArgumentException("학생 ID는 필수입니다.");

            if (student.StudentID.Length != 15)
                throw new ArgumentException("학생 ID는 15자리여야 합니다.");

            if (string.IsNullOrEmpty(student.Name))
                throw new ArgumentException("학생 이름은 필수입니다.");

            if (string.IsNullOrEmpty(student.Sex))
                throw new ArgumentException("성별은 필수입니다.");

            if (student.Sex != "남" && student.Sex != "여")
                throw new ArgumentException("성별은 '남' 또는 '여'만 가능합니다.");
        }

        private void ValidateEnrollment(Enrollment enrollment)
        {
            if (string.IsNullOrEmpty(enrollment.StudentID))
                throw new ArgumentException("학생 ID는 필수입니다.");

            if (string.IsNullOrEmpty(enrollment.SchoolCode))
                throw new ArgumentException("학교 코드는 필수입니다.");

            if (enrollment.Year < 2000 || enrollment.Year > 2100)
                throw new ArgumentException("학년도가 올바르지 않습니다.");

            if (enrollment.Semester != 1 && enrollment.Semester != 2)
                throw new ArgumentException("학기는 1 또는 2만 가능합니다.");
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (!_disposed)
            {
                _studentRepo?.Dispose();
                _enrollmentRepo?.Dispose();
                _detailRepo?.Dispose();
                _disposed = true;
            }
        }

        #endregion
    }

    #region ViewModel

    /// <summary>
    /// 학생 정보 완성도 체크 결과
    /// </summary>
    public class StudentInfoCompleteness
    {
        public string StudentID { get; set; } = string.Empty;
        public bool HasBasicInfo { get; set; }
        public bool HasDetailInfo { get; set; }
        public bool HasEnrollment { get; set; }
        public bool HasPhone { get; set; }
        public bool HasEmail { get; set; }
        public bool HasAddress { get; set; }
        public bool HasGuardianInfo { get; set; }

        /// <summary>
        /// 전체 완성도 퍼센트 (0~100)
        /// </summary>
        public int CompletenessPercentage
        {
            get
            {
                int total = 7;
                int completed = 0;

                if (HasBasicInfo) completed++;
                if (HasDetailInfo) completed++;
                if (HasEnrollment) completed++;
                if (HasPhone) completed++;
                if (HasEmail) completed++;
                if (HasAddress) completed++;
                if (HasGuardianInfo) completed++;

                return (int)((completed / (double)total) * 100);
            }
        }

        public override string ToString()
        {
            return $"정보 완성도: {CompletenessPercentage}%";
        }
    }

    #endregion
}

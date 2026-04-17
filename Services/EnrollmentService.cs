using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Repositories;

namespace NewSchool.Services;

/// <summary>
/// Enrollment Service - A안의 핵심!
/// 학적 관리 비즈니스 로직
/// </summary>
public class EnrollmentService : IDisposable
{
    private readonly EnrollmentRepository _enrollmentRepo;
    private readonly StudentRepository _studentRepo;
    private bool _disposed;

    public EnrollmentService()
    {
        _enrollmentRepo = new EnrollmentRepository(SchoolDatabase.DbPath);
        _studentRepo = new StudentRepository(SchoolDatabase.DbPath);
    }

    #region 학급 배정

    /// <summary>
    /// 학생을 학급에 배정
    /// </summary>
    public async Task<int> AssignToClassAsync(Enrollment enrollment)
    {
        // 유효성 검증
        ValidateEnrollment(enrollment);

        // 중복 배정 확인
        var existing = await _enrollmentRepo.GetCurrentByStudentIdAsync(enrollment.StudentID);
        if (existing != null && existing.Year == enrollment.Year && existing.Semester == enrollment.Semester)
        {
            throw new InvalidOperationException(
                $"학생이 이미 {enrollment.Year}년 {enrollment.Semester}학기에 배정되어 있습니다.");
        }

        // 학생 존재 확인
        var student = await _studentRepo.GetByIdAsync(enrollment.StudentID);
        if (student == null)
        {
            throw new InvalidOperationException($"존재하지 않는 학생입니다: {enrollment.StudentID}");
        }

        // 배정 실행
        enrollment.Status = EnrollmentStatus.Enrolled;
        enrollment.CreatedAt = DateTime.Now;
        enrollment.UpdatedAt = DateTime.Now;

        return await _enrollmentRepo.CreateAsync(enrollment);
    }

    /// <summary>
    /// 여러 학생을 한 번에 배정 (트랜잭션)
    /// </summary>
    public async Task<int> BulkAssignAsync(List<Enrollment> enrollments)
    {
        if (enrollments == null || enrollments.Count == 0)
            return 0;

        try
        {
            _enrollmentRepo.BeginTransaction();

            int count = 0;
            foreach (var enrollment in enrollments)
            {
                ValidateEnrollment(enrollment);
                enrollment.Status = EnrollmentStatus.Enrolled;
                enrollment.CreatedAt = DateTime.Now;
                enrollment.UpdatedAt = DateTime.Now;

                await _enrollmentRepo.CreateAsync(enrollment);
                count++;
            }

            _enrollmentRepo.Commit();
            return count;
        }
        catch
        {
            _enrollmentRepo.Rollback();
            throw;
        }
    }

    #endregion

    #region 진급 처리

    /// <summary>
    /// 학년 전체 진급 처리
    /// </summary>
    public async Task<int> PromoteStudentsAsync(string schoolCode, int fromYear, int fromGrade)
    {
        // 진급 대상 학생 조회 (2학기 재학생)
        var students = await _enrollmentRepo.GetByGradeAsync(schoolCode, fromYear, 2, fromGrade);
        var activeStudents = students.Where(e => e.Status == EnrollmentStatus.Enrolled).ToList();

        if (activeStudents.Count == 0)
            return 0;

        try
        {
            _enrollmentRepo.BeginTransaction();

            int count = 0;
            int nextYear = fromYear + 1;
            int nextGrade = fromGrade + 1;

            foreach (var oldEnrollment in activeStudents)
            {
                // 졸업 처리 (3학년인 경우)
                if (fromGrade >= 3)
                {
                    await GraduateStudentAsync(oldEnrollment.No);
                    count++;
                    continue;
                }

                // 새 학년도 1학기 학적 생성
                var newEnrollment = new Enrollment
                {
                    StudentID = oldEnrollment.StudentID,
                    SchoolCode = oldEnrollment.SchoolCode,
                    Year = nextYear,
                    Semester = 1,
                    Grade = nextGrade,
                    Class = oldEnrollment.Class, // 같은 반 유지 (필요시 수정)
                    Number = oldEnrollment.Number, // 같은 번호 유지 (필요시 수정)
                    Status = EnrollmentStatus.Enrolled,
                    //TeacherID = oldEnrollment.TeacherID, // 담임 변경 필요시 나중에 수정
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };

                await _enrollmentRepo.CreateAsync(newEnrollment);
                count++;
            }

            _enrollmentRepo.Commit();
            return count;
        }
        catch
        {
            _enrollmentRepo.Rollback();
            throw;
        }
    }

    #endregion

    #region 전학 처리

    /// <summary>
    /// 전학(전출) 처리
    /// </summary>
    public async Task<bool> TransferOutAsync(int enrollmentNo, string transferOutSchool, DateTime transferDate)
    {
        var enrollment = await _enrollmentRepo.GetByIdAsync(enrollmentNo);
        if (enrollment == null)
        {
            throw new InvalidOperationException($"존재하지 않는 학적입니다: {enrollmentNo}");
        }

        if (enrollment.Status != EnrollmentStatus.Enrolled)
        {
            throw new InvalidOperationException($"재학 중인 학생만 전출 가능합니다. 현재 상태: {enrollment.Status}");
        }

        // 전출 정보 업데이트
        enrollment.Status = "전학(전출)";
        enrollment.TransferOutDate = transferDate.ToString("yyyy-MM-dd");
        enrollment.TransferOutSchool = transferOutSchool;
        enrollment.UpdatedAt = DateTime.Now;

        return await _enrollmentRepo.UpdateAsync(enrollment);
    }

    /// <summary>
    /// 전학(전입) 처리
    /// </summary>
    public async Task<int> TransferInAsync(Student student, Enrollment enrollment, string transferInSchool, DateTime transferDate)
    {
        // 학생이 이미 존재하는지 확인
        var existingStudent = await _studentRepo.GetByIdAsync(student.StudentID);
        if (existingStudent == null)
        {
            // 새 학생 등록
            await _studentRepo.CreateAsync(student);
        }

        // 전입 학적 생성
        enrollment.Status = "전학(전입)";
        enrollment.TransferInDate = transferDate.ToString("yyyy-MM-dd");
        enrollment.TransferInSchool = transferInSchool;
        enrollment.CreatedAt = DateTime.Now;
        enrollment.UpdatedAt = DateTime.Now;

        return await _enrollmentRepo.CreateAsync(enrollment);
    }

    #endregion

    #region 졸업 처리

    /// <summary>
    /// 학년 전체 졸업 처리
    /// </summary>
    public async Task<int> GraduateAsync(string schoolCode, int year, int grade)
    {
        // 졸업 대상 학생 조회 (2학기 재학생)
        var students = await _enrollmentRepo.GetByGradeAsync(schoolCode, year, 2, grade);
        var activeStudents = students.Where(e => e.Status == EnrollmentStatus.Enrolled).ToList();

        if (activeStudents.Count == 0)
            return 0;

        try
        {
            _enrollmentRepo.BeginTransaction();

            int count = 0;
            DateTime graduationDate = new DateTime(year, 2, 28); // 2월 말 졸업 가정

            foreach (var enrollment in activeStudents)
            {
                await GraduateStudentAsync(enrollment.No, graduationDate);
                count++;
            }

            _enrollmentRepo.Commit();
            return count;
        }
        catch
        {
            _enrollmentRepo.Rollback();
            throw;
        }
    }

    /// <summary>
    /// 개별 학생 졸업 처리
    /// </summary>
    private async Task<bool> GraduateStudentAsync(int enrollmentNo, DateTime? graduationDate = null)
    {
        var enrollment = await _enrollmentRepo.GetByIdAsync(enrollmentNo);
        if (enrollment == null)
            return false;

        enrollment.Status = EnrollmentStatus.Graduated;
        enrollment.GraduationDate = (graduationDate ?? DateTime.Now).ToString("yyyy-MM-dd");
        enrollment.UpdatedAt = DateTime.Now;

        return await _enrollmentRepo.UpdateAsync(enrollment);
    }

    #endregion

    #region 조회
    ///<summary>
        ///학년도 리스트 조회
    /// </summary>
    public async Task<List<int>> GetYearListAsync(string? schoolcode=null)
    {
        return await _enrollmentRepo.GetEnrollmentYearsAsync(schoolcode);
    }
    ///<summary>
        ///학년도별 학년 리스트 조회
    /// </summary>
    public async Task<List<int>> GetGradeListByYearAsync(string? schoolCode, int? year)
    {
        return await _enrollmentRepo.GetGradesByYearAsync(schoolCode, year);
    }

    ///<summary>
    ///학급 리스트 조회
    ///학년도별 학년별 학급리스트를 조회
    /// </summary>
    public async Task<List<int>> GetClassListAsync(string schoolCode, int year, int grade)
    {
        return await _enrollmentRepo.GetClassListByGradeAsync(schoolCode, year, grade);
    }
    /// <summary>
    ///  학년도별, 학년별,  학급 별    학생 명부 조회
    /// </summary>
    /// 변수 설명: schoolCode: 학교 코드, year: 학년도, semister: 학기, grade: 학년, classnum: 반 번호
    /// 학년도, 학기, 학년, 반 번호는 0으로 지정 시 전체 조회
    /// <returns>List<Enrollment></Enrollment></returns>
    public async Task<List<Enrollment>> GetEnrollmentsAsync(string schoolCode, int year=0, int semester=0, int grade=0, int classnum=0)
    {
        return await _enrollmentRepo.GetEnrollmentsAsync(schoolCode: schoolCode, year: year, grade:grade, classNum:classnum);
    }

    /// <summary>
    /// 학급 명부 조회 (가장 중요!)
    /// Enrollment에 Name, Sex, Photo가 denormalized되어 있어 단일 테이블 조회로 충분
    /// </summary>
    public async Task<List<Enrollment>> GetClassRosterAsync(
        string schoolCode, int year, int grade, int classNo)
    {
        var enrollments = await _enrollmentRepo.GetEnrollmentsAsync(
            schoolCode: schoolCode, 
            year: year, 
            grade: grade, 
            classNum: classNo);

        return enrollments.OrderBy(e => e.Number).ToList();
    }

    /// <summary>
    /// 학생의 학적 이력 조회
    /// </summary>
    public async Task<List<Enrollment>> GetStudentHistoryAsync(string studentId) => await _enrollmentRepo.GetHistoryByStudentIdAsync(studentId);

    /// <summary>
    /// 학생의 현재 학적 조회
    /// </summary>
    public async Task<Enrollment?> GetCurrentEnrollmentAsync(string studentId) => await _enrollmentRepo.GetCurrentByStudentIdAsync(studentId);

    /// <summary>
    /// 담임교사의 학생 목록 조회
    /// Enrollment에 Name, Sex, Photo가 denormalized되어 있어 단일 테이블 조회로 충분
    /// </summary>
    public async Task<List<Enrollment>> GetTeacherStudentsAsync(string teacherId, int year, int semester)
    {
        var enrollments = await _enrollmentRepo.GetByTeacherAsync(teacherId, year);
        
        return enrollments
            .Where(e => e.Semester == semester)
            .OrderBy(e => e.Grade)
            .ThenBy(e => e.Class)
            .ThenBy(e => e.Number)
            .ToList();
    }

    #endregion

    public static async Task<bool> UpdateAsync(Enrollment enrollment)

    {
        using var service = new EnrollmentService();
        // 유효성 검증
        service.ValidateEnrollment(enrollment);
        // 업데이트 실행
        enrollment.UpdatedAt = DateTime.Now;
        using var repo = new EnrollmentRepository(SchoolDatabase.DbPath);
        return await repo.UpdateAsync(enrollment);
    }
    #region 유효성 검증

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

        if (enrollment.Grade < 1 || enrollment.Grade > 6)
            throw new ArgumentException("학년은 1~6 사이여야 합니다.");

        if (enrollment.Class < 1)
            throw new ArgumentException("반은 1 이상이어야 합니다.");

        if (enrollment.Number < 1)
            throw new ArgumentException("번호는 1 이상이어야 합니다.");
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (!_disposed)
        {
            _enrollmentRepo?.Dispose();
            _studentRepo?.Dispose();
            _disposed = true;
        }
    }

    #endregion

}

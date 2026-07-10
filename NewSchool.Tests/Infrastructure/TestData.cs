using System;
using System.Threading;
using NewSchool.Models;

namespace NewSchool.Tests.Infrastructure;

/// <summary>
/// 테스트 시드 빌더 (TEST_PLAN 0단계) — 유효한 기본값을 가진 모델 생성 헬퍼.
/// 학생 ID 는 실제 규칙(학교코드7 + 연도4 + 일련4 = 15자리)을 따르며,
/// 프로세스 내 일련번호 카운터로 충돌 없이 생성된다.
/// </summary>
public static class TestData
{
    public const string SchoolCode = "7530072";
    public const string TeacherId = "T0001";
    public const int Year = 2026;

    private static int _seq;

    /// <summary>실제 규칙(<see cref="Student.GenerateStudentID"/>)을 따르는 고유 학생 ID.</summary>
    public static string NewStudentId() =>
        Student.GenerateStudentID(SchoolCode, Year, Interlocked.Increment(ref _seq));

    public static Student NewStudent(string? id = null, string name = "홍길동", string sex = "남") => new()
    {
        StudentID = id ?? NewStudentId(),
        Name = name,
        Sex = sex,
        Phone = string.Empty,
        Email = string.Empty,
        Address = string.Empty,
        Memo = string.Empty,
        CreatedAt = DateTime.Now,
        UpdatedAt = DateTime.Now,
        IsDeleted = false,
    };

    public static Enrollment NewEnrollment(
        string studentId,
        string name = "홍길동",
        int year = Year,
        int semester = 1,
        int grade = 1,
        int classNum = 1,
        int number = 1,
        string status = "재학") => new()
    {
        StudentID = studentId,
        Name = name,
        Sex = "남",
        Photo = string.Empty,
        SchoolCode = SchoolCode,
        Year = year,
        Semester = semester,
        Grade = grade,
        Class = classNum,
        Number = number,
        Status = status,
        TeacherID = TeacherId,
        AdmissionDate = $"{year}-03-01",
        Memo = string.Empty,
        CreatedAt = DateTime.Now,
        UpdatedAt = DateTime.Now,
        IsDeleted = false,
    };
}

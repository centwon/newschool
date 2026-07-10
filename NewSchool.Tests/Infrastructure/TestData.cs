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

    public static StudentLog NewStudentLog(
        string studentId,
        int year = Year,
        int semester = 1,
        LogCategory category = LogCategory.기타,
        string log = "테스트 기록",
        DateTime? date = null) => new()
    {
        StudentID = studentId,
        TeacherID = TeacherId,
        Year = year,
        Semester = semester,
        Date = date ?? new DateTime(year, 4, 15),
        Category = category,
        Log = log,
        SubjectName = string.Empty,
        ClubName = string.Empty,
        ActivityName = string.Empty,
        Topic = string.Empty,
        Description = string.Empty,
    };

    public static StudentSpecial NewSpecial(
        string studentId,
        int year = Year,
        string type = "자율활동",
        string title = "테스트 특기사항",
        string content = "내용",
        int courseNo = 0,
        bool isFinalized = false) => new()
    {
        StudentID = studentId,
        Year = year,
        Type = type,
        Title = title,
        Content = content,
        Date = $"{year}-05-01",
        TeacherID = TeacherId,
        CourseNo = courseNo,
        SubjectName = string.Empty,
        IsFinalized = isFinalized,
        Tag = string.Empty,
    };

    public static ClassTimetable NewTimetableSlot(
        int grade = 1, int classNum = 1,
        int dayOfWeek = 1, int period = 1,
        string subject = "국어",
        int year = Year, int semester = 1) => new()
    {
        SchoolCode = SchoolCode,
        Year = year,
        Semester = semester,
        Grade = grade,
        Class = classNum,
        DayOfWeek = dayOfWeek,
        Period = period,
        SubjectName = subject,
        TeacherName = "테스트교사",
        Room = string.Empty,
    };

    public static Course NewCourse(
        string subject = "국어",
        int grade = 1,
        int year = Year,
        int semester = 1,
        string rooms = "1-1") => new()
    {
        SchoolCode = SchoolCode,
        Year = year,
        Semester = semester,
        TeacherID = TeacherId,
        Grade = grade,
        Subject = subject,
        Unit = 3,
        Rooms = rooms,
        Remark = string.Empty,
    };

    public static CourseSection NewSection(
        int courseNo,
        int unitNo = 1, string unitName = "1단원",
        int sectionNo = 1, string sectionName = "1절",
        int hours = 2) => new()
    {
        Course = courseNo,
        UnitNo = unitNo,
        UnitName = unitName,
        ChapterNo = 1,
        ChapterName = "1장",
        SectionNo = sectionNo,
        SectionName = sectionName,
        StartPage = 1,
        EndPage = 10,
        EstimatedHours = hours,
        LessonPlan = string.Empty,
        SortOrder = sectionNo,
    };

    public static Board.Post NewPost(
        string category = "수업",
        string subject = "메모",
        string title = "테스트 메모") => new()
    {
        User = "테스트교사",
        DateTime = DateTime.Now,
        Category = category,
        Subject = subject,
        Title = title,
        Content = [],
        PlainText = "본문 평문",
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

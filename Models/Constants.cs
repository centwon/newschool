namespace NewSchool.Models;

/// <summary>
/// 메모/일정 카테고리
/// </summary>
public static class CategoryNames
{
    public const string Lesson = "수업";
    public const string Homeroom = "학급";
    public const string Work = "업무";
    public const string Personal = "개인";

    public static readonly string[] All = [Lesson, Homeroom, Work, Personal];
}

/// <summary>
/// 학적 상태
/// </summary>
public static class EnrollmentStatus
{
    public const string Enrolled = "재학";
    public const string OnLeave = "휴학";
    public const string Graduated = "졸업";
    public const string Transferred = "전학";
    public const string Withdrawn = "자퇴";
    public const string Expelled = "퇴학";
}

/// <summary>
/// 수강 상태
/// </summary>
public static class CourseEnrollmentStatus
{
    public const string Active = "수강중";
    public const string Completed = "수강완료";
    public const string Cancelled = "수강취소";
}

/// <summary>
/// 동아리 등록 상태
/// </summary>
public static class ClubEnrollmentStatus
{
    public const string Active = "활동중";
    public const string Withdrawn = "탈퇴";
}

/// <summary>
/// 수업 유형
/// </summary>
public static class CourseTypes
{
    public const string Class = "Class";
    public const string Selective = "Selective";
    public const string Club = "Club";
}

/// <summary>
/// 출결 상태
/// </summary>
public static class AttendanceStatus
{
    public const string Present = "출석";
    public const string Tardy = "지각";
    public const string EarlyLeave = "조퇴";
    public const string Absent = "결석";
    public const string Excused = "결과";
    public const string Illness = "질병";
    public const string Other = "기타";
}

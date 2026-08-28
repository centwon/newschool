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

    /// <summary>전입 — 다른 학교에서 왔다. <b>지금 이 학교 학생이다.</b></summary>
    public const string TransferredIn = "전입";

    /// <summary>전출 — 다른 학교로 갔다. <b>더는 이 학교 학생이 아니다.</b></summary>
    public const string TransferredOut = "전출";

    public const string Withdrawn = "자퇴";
    public const string Expelled = "퇴학";

    /// <summary>
    /// 1.0 이전 값. 전입과 전출을 가르지 않던 시절의 것으로, 새로 쓰지 않는다.
    /// 남아 있는 행은 <see cref="IsOnRoll"/> 이 재적으로 보며(안 보이게 숨기지 않는다),
    /// 학생 관리에서 열어 전입·전출 중 하나로 고쳐 주면 사라진다.
    /// </summary>
    public const string Transferred = "전학";

    /// <summary>
    /// <b>코드가 상태를 두고 묻는 유일한 질문</b> — 지금 이 학교 명부에 있는가(재적).
    ///
    /// <para>명단·좌석·수업·동아리처럼 "지금 이 반 학생" 을 묻는 곳은 모두 이것만 본다.
    /// 상태 문자열을 직접 비교하지 말 것 — 값이 늘어날 때마다 비교하는 곳이 다 틀어진다.</para>
    ///
    /// <para>빠지는 넷만 거짓이고 나머지는 전부 참이다. 빈 값과 옛 "전학" 도 참으로 본다 —
    /// 판단이 안 서는 행을 거짓으로 보면 멀쩡한 학생이 명부에서 사라지는데,
    /// 그 손해가 반대쪽보다 크다.</para>
    /// </summary>
    public static bool IsOnRoll(string? status) =>
        status is not (TransferredOut or Graduated or Withdrawn or Expelled);
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

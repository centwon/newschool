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
/// 학적 변동. <c>Enrollment.ChangeType</c> 에 들어가는 값이다.
///
/// <para>학적이 <b>왜</b> 지금 모양이 됐는지를 말한다. 그 학생이 명단에 들어가는지는
/// <see cref="IsActive"/> 가 정한다 — 값 목록을 직접 비교하지 말 것.</para>
///
/// <para>설계 근거는 <c>docs/enrollment-redesign.md</c>.</para>
/// </summary>
public static class EnrollmentChange
{
    // ── 활성: 지금 이 학교 학생이다 ──
    /// <summary>입학 — 1학년의 기본값.</summary>
    public const string Admitted = "입학";
    /// <summary>진급 — 2학년 이상의 기본값.</summary>
    public const string Promoted = "진급";
    /// <summary>전입 — 다른 학교에서 왔다.</summary>
    public const string TransferredIn = "전입";

    // ── 비활성: 더는 명단에 넣지 않는다 ──
    /// <summary>전출 — 다른 학교로 갔다. 이 날짜 뒤의 기록은 경고한다.</summary>
    public const string TransferredOut = "전출";
    public const string Graduated = "졸업";
    public const string OnLeave = "휴학";
    /// <summary>유예 — 취학유예. 1학년에만 해당한다.</summary>
    public const string Deferred = "유예";
    /// <summary>정원외 — 정원외 관리. 학년과 무관하다.</summary>
    public const string OutOfQuota = "정원외";
    public const string Withdrawn = "자퇴";
    public const string Expelled = "퇴학";

    /// <summary>고르는 차례대로 — 화면의 목록도 이 순서를 따른다.</summary>
    public static readonly string[] All =
    [
        Admitted, Promoted, TransferredIn,
        TransferredOut, Graduated, OnLeave, Deferred, OutOfQuota, Withdrawn, Expelled
    ];

    /// <summary>
    /// 이 변동을 겪은 학적이 <b>명단에 들어가는가</b>.
    ///
    /// <para><c>Enrollment.IsActive</c> 컬럼은 이 함수로만 채운다. <c>IsActive</c> 를 인자로
    /// 받는 함수를 만들지 말 것 — 그 순간 두 값이 갈라질 길이 생긴다.</para>
    ///
    /// <para>모르는 값과 빈 값은 <b>참</b>으로 본다. 판단이 안 서는 행을 숨겨 멀쩡한 학생이
    /// 명단에서 사라지는 손해가, 남겨서 생기는 손해보다 크다.</para>
    /// </summary>
    public static bool IsActive(string? changeType) =>
        changeType is not (TransferredOut or Graduated or OnLeave
                        or Deferred or OutOfQuota or Withdrawn or Expelled);

    /// <summary>
    /// 학년으로 정하는 기본 변동 — 1학년은 입학, 그 위는 진급.
    /// 전입은 사람이 골라야 알 수 있으므로 기본값이 될 수 없다.
    /// </summary>
    public static string DefaultFor(int grade) => grade <= 1 ? Admitted : Promoted;
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

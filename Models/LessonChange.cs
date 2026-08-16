using System;

namespace NewSchool.Models;

/// <summary>
/// 시간표 변경 — <b>특정 날짜의 한 교시</b>에만 걸리는 예외.
///
/// 정기 시간표(<see cref="Lesson"/>)는 건드리지 않는다. 그래서 변경을 아무리 넣어도
/// 교사 시간표·시수 계산 같은 "평소" 기준은 흔들리지 않는다.
///
/// ⚠ <c>Lesson.IsCancelled</c> 로는 이 일을 할 수 없다 — 그건 행 단위 플래그라
/// 정기 수업에 걸면 그 수업이 <b>매주</b> 사라진다
/// (<c>LessonRepository.GetByDateAsync</c> 의 조건이 날짜를 가리지 않는다).
///
/// 한 장으로 네 가지를 덮는다:
/// <list type="bullet">
///   <item><see cref="CourseNo"/> 도 <see cref="SubjectText"/> 도 없으면 → 그 교시 <b>휴강</b></item>
///   <item><see cref="CourseNo"/> 가 있고 그 교시에 정기 수업이 있으면 → <b>교체</b>(맞바꾸기는 두 줄로 표현된다)</item>
///   <item><see cref="CourseNo"/> 가 있고 정기 수업이 없으면 → <b>보강</b></item>
///   <item><see cref="SubjectText"/> 만 있으면 → <b>대강</b> — 남의 수업에 대신 들어가는 경우다.
///         내가 개설한 수업이 아니라 <c>Course</c> 에 없으므로 과목명을 그대로 적어 둔다</item>
/// </list>
/// 교체냐 보강이냐는 저장하지 않고 볼 때 판단한다 — 정기 시간표가 나중에 바뀔 수 있어서,
/// 저장해 두면 그 순간 거짓이 된다.
/// </summary>
public class LessonChange : NotifyPropertyChangedBase
{
    #region Fields

    private int _no = -1;
    private string _teacherId = string.Empty;
    private int _year;
    private int _semester;
    private DateTime _date;
    private int _period;
    private int? _courseNo;
    private string _subjectText = string.Empty;
    private string _room = string.Empty;
    private string _memo = string.Empty;

    // 표시용 (DB 비저장)
    private string _courseSubject = string.Empty;

    #endregion

    #region Properties

    /// <summary>PK</summary>
    public int No
    {
        get => _no;
        set => SetProperty(ref _no, value);
    }

    /// <summary>담당 교사 ID</summary>
    public string TeacherID
    {
        get => _teacherId;
        set => SetProperty(ref _teacherId, value);
    }

    /// <summary>학년도</summary>
    public int Year
    {
        get => _year;
        set => SetProperty(ref _year, value);
    }

    /// <summary>학기</summary>
    public int Semester
    {
        get => _semester;
        set => SetProperty(ref _semester, value);
    }

    /// <summary>변경이 걸리는 날짜</summary>
    public DateTime Date
    {
        get => _date;
        set
        {
            if (SetProperty(ref _date, value))
                OnPropertyChanged(nameof(DateDisplay));
        }
    }

    /// <summary>교시 (1~)</summary>
    public int Period
    {
        get => _period;
        set
        {
            if (SetProperty(ref _period, value))
                OnPropertyChanged(nameof(PeriodDisplay));
        }
    }

    /// <summary>그 교시에 들어갈 내 수업. null 이고 <see cref="SubjectText"/> 도 비면 휴강.</summary>
    public int? CourseNo
    {
        get => _courseNo;
        set
        {
            if (SetProperty(ref _courseNo, value))
            {
                OnPropertyChanged(nameof(IsCancellation));
                OnPropertyChanged(nameof(IsSubstitute));
                OnPropertyChanged(nameof(Subject));
                OnPropertyChanged(nameof(ContentDisplay));
            }
        }
    }

    /// <summary>
    /// 내 수업이 아닐 때 직접 적는 과목명 (대강).
    /// 남의 수업에 대신 들어가는 경우 <c>Course</c> 에 그 수업이 없어서 FK 로는 가리킬 수 없다.
    /// </summary>
    public string SubjectText
    {
        get => _subjectText;
        set
        {
            if (SetProperty(ref _subjectText, value))
            {
                OnPropertyChanged(nameof(IsCancellation));
                OnPropertyChanged(nameof(IsSubstitute));
                OnPropertyChanged(nameof(Subject));
                OnPropertyChanged(nameof(ContentDisplay));
            }
        }
    }

    /// <summary>강의실 (휴강이면 비어 있다)</summary>
    public string Room
    {
        get => _room;
        set
        {
            if (SetProperty(ref _room, value))
                OnPropertyChanged(nameof(ContentDisplay));
        }
    }

    /// <summary>사유·메모 (예: "출장", "학년 체험학습")</summary>
    public string Memo
    {
        get => _memo;
        set => SetProperty(ref _memo, value);
    }

    /// <summary>
    /// <see cref="CourseNo"/> 가 가리키는 수업의 과목명 — 조회할 때 채워 넣는다(DB 비저장).
    /// </summary>
    public string CourseSubject
    {
        get => _courseSubject;
        set
        {
            if (SetProperty(ref _courseSubject, value))
            {
                OnPropertyChanged(nameof(Subject));
                OnPropertyChanged(nameof(ContentDisplay));
            }
        }
    }

    #endregion

    #region Computed

    /// <summary>내 수업을 가리키는가</summary>
    public bool HasCourse => CourseNo is > 0;

    /// <summary>대강 — 내 수업이 아니라 과목명만 적어 둔 경우</summary>
    public bool IsSubstitute => !HasCourse && !string.IsNullOrWhiteSpace(SubjectText);

    /// <summary>휴강인가 — 들어갈 수업도, 적어 둔 과목명도 없는 경우</summary>
    public bool IsCancellation => !HasCourse && string.IsNullOrWhiteSpace(SubjectText);

    /// <summary>표시할 과목명</summary>
    public string Subject => HasCourse ? CourseSubject : SubjectText;

    /// <summary>날짜 표시 (예: "8/18(월)")</summary>
    public string DateDisplay => Date.ToString("M/d(ddd)");

    /// <summary>교시 표시</summary>
    public string PeriodDisplay => $"{Period}교시";

    /// <summary>
    /// 내용 표시 (예: "휴강", "수학 · 1-3", "(대)과학 · 2-1").
    ///
    /// 내 수업일 때 교체인지 보강인지는 여기서 알 수 없다 — 그 교시에 평소 수업이 있었는지는
    /// 정기 시간표를 봐야 갈리고, 그건 나중에 바뀔 수도 있다. 그래서 과목명만 적는다.
    /// </summary>
    public string ContentDisplay
    {
        get
        {
            if (IsCancellation) return "휴강";

            var subject = string.IsNullOrWhiteSpace(Subject) ? "수업" : Subject;
            if (IsSubstitute) subject = LessonChangeLabels.Prefix(LessonChangeKind.Substitute) + subject;

            return string.IsNullOrWhiteSpace(Room) ? subject : $"{subject} · {Room}";
        }
    }

    #endregion

    public override string ToString() => $"{DateDisplay} {PeriodDisplay} {ContentDisplay}";
}

/// <summary>
/// 오늘 화면에서 한 교시가 평소와 어떻게 다른가.
/// </summary>
public enum LessonChangeKind
{
    /// <summary>평소대로</summary>
    None = 0,

    /// <summary>휴강</summary>
    Cancelled,

    /// <summary>원래 있던 시간이 다른 수업으로 교체</summary>
    Replaced,

    /// <summary>원래 없던 교시에 들어온 보강</summary>
    Added,

    /// <summary>대강 — 남의 수업에 대신 들어감 (내 수업이 아니라 과목명만 적는다)</summary>
    Substitute
}

/// <summary>
/// 변경 표시 문구를 한곳에 모은다 — 오늘 화면 배지와 주별 표 칸이 서로 다른 말을 쓰면
/// 같은 것을 두 이름으로 부르게 된다.
/// </summary>
public static class LessonChangeLabels
{
    /// <summary>칸 안 과목명 앞에 붙이는 표식 (예: "(교)영어")</summary>
    public static string Prefix(LessonChangeKind kind) => kind switch
    {
        LessonChangeKind.Cancelled => "(휴)",
        LessonChangeKind.Replaced => "(교)",
        LessonChangeKind.Added => "(보)",
        LessonChangeKind.Substitute => "(대)",
        _ => ""
    };

    /// <summary>배지·목록에 쓰는 이름</summary>
    public static string Name(LessonChangeKind kind) => kind switch
    {
        LessonChangeKind.Cancelled => "휴강",
        LessonChangeKind.Replaced => "교체",
        LessonChangeKind.Added => "보강",
        LessonChangeKind.Substitute => "대강",
        _ => ""
    };

    /// <summary>과목명 앞에 표식을 붙인다</summary>
    public static string WithPrefix(LessonChangeKind kind, string subject)
        => kind == LessonChangeKind.None ? subject : $"{Prefix(kind)}{subject}";
}

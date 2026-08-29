using System;

namespace NewSchool.Models;

/// <summary>
/// <b>교사 시간표의 한 칸</b> — 이 선생이 무슨 요일 몇 교시에 어느 수업을 어디서 하는가.
///
/// <para>학급 시간표가 아니다. 그건 <c>ClassTimetable</c> 이 맡고, 보는 표부터 다르다
/// (<see cref="Services.TeacherTimetableService"/> 와 <c>TimetableService</c> 의 짝을 볼 것).</para>
///
/// <para><b>여기 칸을 더하기 전에 읽을 것.</b> 원래 이 표는 "수업 한 건" 을 통째로 담으려 해서
/// 주제·완료·휴강·비정기 날짜까지 들고 있었다. 그 일들은 하나씩 다른 곳으로 옮겨 갔고,
/// 칸만 기본값인 채로 남아 있다가 2026-08-29 에 걷혔다 —
/// <list type="bullet">
///   <item>주제·완료 → <b>게시판 수업 일지</b>. 완료 판정은 "그 교시 글이 있는가" 다
///         (일지 일원화, 2026-08-21)</item>
///   <item>휴강·보강(비정기) → <b><see cref="LessonChange"/></b>. 날짜 하나짜리 예외를
///         여기 플래그로 하면 정기 수업이 <b>매주</b> 사라진다</item>
///   <item>학급 → <c>Room</c> 문자열과 <c>ClassTimetable</c></item>
/// </list>
/// 즉 비어 있던 칸들은 "아직 안 쓴 자리" 가 아니라 <b>이미 남이 하기로 정해진 일</b>이었다.
/// 되살리려면 그 결정부터 뒤집어야 한다.</para>
/// </summary>
public class Lesson : NotifyPropertyChangedBase
{
    #region Fields

    private int _no = -1;
    private int _course;
    private string _teacher = string.Empty;
    private int _year;
    private int _semester;
    private int _dayOfWeek;
    private int _period;
    private int _grade;
    private string _room = string.Empty;

    #endregion

    #region Properties

    /// <summary>PK (자동 증가)</summary>
    public int No
    {
        get => _no;
        set => SetProperty(ref _no, value);
    }

    /// <summary>과목 (FK: Course.No)</summary>
    public int Course
    {
        get => _course;
        set => SetProperty(ref _course, value);
    }

    /// <summary>담당 교사 ID</summary>
    public string Teacher
    {
        get => _teacher;
        set => SetProperty(ref _teacher, value);
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

    /// <summary>요일 (1=월, 2=화, ..., 5=금)</summary>
    public int DayOfWeek
    {
        get => _dayOfWeek;
        set
        {
            if (SetProperty(ref _dayOfWeek, value))
                Notify(nameof(DayName), nameof(ScheduleDisplay));
        }
    }

    /// <summary>교시 (1~7)</summary>
    public int Period
    {
        get => _period;
        set { if (SetProperty(ref _period, value)) Notify(nameof(ScheduleDisplay)); }
    }

    /// <summary>
    /// 대상 학년. <b>지금 읽는 곳이 없다</b> — 배치할 때 <c>Course.Grade</c> 를 옮겨 적을 뿐이다.
    ///
    /// <para>같이 있던 <c>Class</c> 는 채우는 코드가 아예 없어(늘 0) 함께 걷었는데, 이 칸은
    /// 값이 들어가므로 남겼다. 학급 축을 제대로 세우는 일(<c>Room</c> 문자열이 사실상
    /// 외래키 노릇을 하는 문제)을 할 때 함께 볼 자리다.</para>
    /// </summary>
    public int Grade
    {
        get => _grade;
        set => SetProperty(ref _grade, value);
    }

    /// <summary>
    /// 강의실 또는 학급 (예: "5-1", "음악실"). <c>Course.Rooms</c> 에 적힌 한 항목이다.
    ///
    /// <para>⚠ 시수 계산·진도가 이 <b>문자열</b>을 키로 삼는다. 수업 개설에서 이름을 고치면
    /// 딸린 기록이 조용히 갈라진다 — 알려진 숙제다.</para>
    /// </summary>
    public string Room
    {
        get => _room;
        set => SetProperty(ref _room, value);
    }

    #endregion

    #region Computed Properties

    /// <summary>요일명</summary>
    public string DayName => DayOfWeek switch
    {
        1 => "월",
        2 => "화",
        3 => "수",
        4 => "목",
        5 => "금",
        6 => "토",
        7 => "일",
        _ => ""
    };

    /// <summary>시간표 표시 (예: "월 3교시")</summary>
    public string ScheduleDisplay => $"{DayName} {Period}교시";

    // 학급 표시(ClassDisplay)는 지웠다 — Class 가 0 이면 Room 을 내놓는 삼항이었는데,
    // Class 를 채우는 코드가 없어 늘 Room 쪽만 탔다. 부르던 곳은 Room 을 직접 읽는다.

    #endregion

    #region Methods

    public override string ToString()
    {
        return $"{Room} - {DayName} {Period}교시";
    }

    #endregion
}

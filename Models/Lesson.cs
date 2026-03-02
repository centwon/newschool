using System;

namespace NewSchool.Models;

/// <summary>
/// 수업 시간표 (정기/비정기 수업)
/// Course에 연결된 개별 수업 일정 관리
/// </summary>
public class Lesson : NotifyPropertyChangedBase, IEntity, IYearSemesterEntity
{
    #region Fields

    private int _no = -1;
    private int _course;
    private string _teacher = string.Empty;
    private int _year;
    private int _semester;
    private string _date = string.Empty;
    private int _dayOfWeek;
    private int _period;
    private int _grade;
    private int _class;
    private string _room = string.Empty;
    private string _topic = string.Empty;
    private bool _isRecurring = true;
    private bool _isCompleted;
    private bool _isCancelled;

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

    /// <summary>날짜 (비정기 수업용, yyyy-MM-dd)</summary>
    public string Date
    {
        get => _date;
        set => SetProperty(ref _date, value);
    }

    /// <summary>요일 (1=월, 2=화, ..., 5=금, 정기 수업용)</summary>
    public int DayOfWeek
    {
        get => _dayOfWeek;
        set => SetProperty(ref _dayOfWeek, value);
    }

    /// <summary>교시 (1~7)</summary>
    public int Period
    {
        get => _period;
        set => SetProperty(ref _period, value);
    }

    /// <summary>대상 학년</summary>
    public int Grade
    {
        get => _grade;
        set => SetProperty(ref _grade, value);
    }

    /// <summary>대상 반</summary>
    public int Class
    {
        get => _class;
        set => SetProperty(ref _class, value);
    }

    /// <summary>강의실/학급 (예: "5-1", "음악실")</summary>
    public string Room
    {
        get => _room;
        set => SetProperty(ref _room, value);
    }

    /// <summary>수업 주제</summary>
    public string Topic
    {
        get => _topic;
        set => SetProperty(ref _topic, value);
    }

    /// <summary>정기 수업 여부 (매주 반복)</summary>
    public bool IsRecurring
    {
        get => _isRecurring;
        set => SetProperty(ref _isRecurring, value);
    }

    /// <summary>완료 여부</summary>
    public bool IsCompleted
    {
        get => _isCompleted;
        set => SetProperty(ref _isCompleted, value);
    }

    /// <summary>취소 여부</summary>
    public bool IsCancelled
    {
        get => _isCancelled;
        set => SetProperty(ref _isCancelled, value);
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

    /// <summary>학급 표시 (예: "5-1")</summary>
    public string ClassDisplay => Class > 0 ? $"{Grade}-{Class}" : Room;

    #endregion

    #region Methods

    public override string ToString()
    {
        return $"{Room} - {DayName} {Period}교시";
    }

    #endregion
}

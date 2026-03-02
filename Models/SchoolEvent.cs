using System;

namespace NewSchool.Models;

/// <summary>
/// 학교 일정 모델
/// 학사일정, 행사, 시험, 연수, 개인 일정 관리
/// </summary>
public class SchoolEvent : NotifyPropertyChangedBase, IEntity
{
    #region Fields

    private int _no = -1;
    private string _teacherId = string.Empty;
    private string _schoolCode = string.Empty;
    private int _year;
    private EventCategory _category = EventCategory.학사;
    private string _title = string.Empty;
    private string _description = string.Empty;
    private DateTime _startDate = DateTime.Today;
    private DateTime _endDate = DateTime.Today;
    private bool _isAllDay = true;
    private TimeSpan _startTime;
    private TimeSpan _endTime;
    private string _location = string.Empty;
    private string _color = "#FF339AF0";
    private bool _isImportant;
    private EventRepeat _repeat = EventRepeat.없음;
    private DateTime _createdAt = DateTime.Now;
    private DateTime _updatedAt = DateTime.Now;

    #endregion

    #region Properties

    /// <summary>PK (자동 증가)</summary>
    public int No
    {
        get => _no;
        set => SetProperty(ref _no, value);
    }

    /// <summary>작성 교사 ID</summary>
    public string TeacherID
    {
        get => _teacherId;
        set => SetProperty(ref _teacherId, value);
    }

    /// <summary>학교 코드</summary>
    public string SchoolCode
    {
        get => _schoolCode;
        set => SetProperty(ref _schoolCode, value);
    }

    /// <summary>학년도</summary>
    public int Year
    {
        get => _year;
        set => SetProperty(ref _year, value);
    }

    /// <summary>카테고리</summary>
    public EventCategory Category
    {
        get => _category;
        set => SetProperty(ref _category, value);
    }

    /// <summary>제목</summary>
    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value ?? string.Empty);
    }

    /// <summary>설명</summary>
    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value ?? string.Empty);
    }

    /// <summary>시작 날짜</summary>
    public DateTime StartDate
    {
        get => _startDate;
        set => SetProperty(ref _startDate, value.Date);
    }

    /// <summary>종료 날짜</summary>
    public DateTime EndDate
    {
        get => _endDate;
        set => SetProperty(ref _endDate, value.Date);
    }

    /// <summary>종일 일정 여부</summary>
    public bool IsAllDay
    {
        get => _isAllDay;
        set => SetProperty(ref _isAllDay, value);
    }

    /// <summary>시작 시간 (종일 아닌 경우)</summary>
    public TimeSpan StartTime
    {
        get => _startTime;
        set => SetProperty(ref _startTime, value);
    }

    /// <summary>종료 시간 (종일 아닌 경우)</summary>
    public TimeSpan EndTime
    {
        get => _endTime;
        set => SetProperty(ref _endTime, value);
    }

    /// <summary>장소</summary>
    public string Location
    {
        get => _location;
        set => SetProperty(ref _location, value ?? string.Empty);
    }

    /// <summary>표시 색상 (Hex)</summary>
    public string Color
    {
        get => _color;
        set => SetProperty(ref _color, value ?? "#FF339AF0");
    }

    /// <summary>중요 표시</summary>
    public bool IsImportant
    {
        get => _isImportant;
        set => SetProperty(ref _isImportant, value);
    }

    /// <summary>반복 설정</summary>
    public EventRepeat Repeat
    {
        get => _repeat;
        set => SetProperty(ref _repeat, value);
    }

    /// <summary>생성일시</summary>
    public DateTime CreatedAt
    {
        get => _createdAt;
        set => SetProperty(ref _createdAt, value);
    }

    /// <summary>수정일시</summary>
    public DateTime UpdatedAt
    {
        get => _updatedAt;
        set => SetProperty(ref _updatedAt, value);
    }

    #endregion

    #region Computed Properties

    /// <summary>기간 (일수)</summary>
    public int DurationDays => (EndDate - StartDate).Days + 1;

    /// <summary>여러 날 일정인지</summary>
    public bool IsMultiDay => DurationDays > 1;

    /// <summary>날짜 범위 표시</summary>
    public string DateRangeDisplay => IsMultiDay
        ? $"{StartDate:M/d(ddd)} ~ {EndDate:M/d(ddd)}"
        : $"{StartDate:M/d(ddd)}";

    /// <summary>시간 범위 표시</summary>
    public string TimeRangeDisplay => IsAllDay
        ? "종일"
        : $"{StartTime:hh\\:mm} ~ {EndTime:hh\\:mm}";

    /// <summary>카테고리 색상</summary>
    public string CategoryColor => Category switch
    {
        EventCategory.학사 => "#FF339AF0",
        EventCategory.시험 => "#FFE74C3C",
        EventCategory.행사 => "#FF27AE60",
        EventCategory.연수 => "#FF9B59B6",
        EventCategory.회의 => "#FFFF9800",
        EventCategory.개인 => "#FF95A5A6",
        _ => "#FFBDC3C7"
    };

    /// <summary>진행 중 여부</summary>
    public bool IsOngoing => DateTime.Today >= StartDate && DateTime.Today <= EndDate;

    /// <summary>지난 일정 여부</summary>
    public bool IsPast => DateTime.Today > EndDate;

    /// <summary>다가오는 일정 여부</summary>
    public bool IsUpcoming => DateTime.Today < StartDate;

    /// <summary>D-day 계산</summary>
    public int DaysUntil => (StartDate - DateTime.Today).Days;

    /// <summary>D-day 표시</summary>
    public string DDayDisplay => DaysUntil switch
    {
        0 => "D-Day",
        > 0 => $"D-{DaysUntil}",
        _ => $"D+{-DaysUntil}"
    };

    /// <summary>특정 날짜가 이 일정에 포함되는지</summary>
    public bool ContainsDate(DateTime date) => date.Date >= StartDate && date.Date <= EndDate;

    #endregion

    #region Methods

    public override string ToString()
        => $"[{Category}] {DateRangeDisplay} {Title}";

    #endregion
}

/// <summary>
/// 일정 카테고리
/// </summary>
public enum EventCategory
{
    학사,
    시험,
    행사,
    연수,
    회의,
    개인
}

/// <summary>
/// 반복 설정
/// </summary>
public enum EventRepeat
{
    없음,
    매일,
    매주,
    매월,
    매년
}

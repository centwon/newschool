using System;
using System.Collections.Generic;
using System.Linq;

namespace NewSchool.Models;

/// <summary>
/// 실제 배치된 수업 슬롯
/// 날짜별 수업 관리 및 진도 추적
/// </summary>
public class Schedule : NotifyPropertyChangedBase, IEntity
{
    #region Fields

    private int _no = -1;
    private int _courseId;
    private string _room = string.Empty;
    private DateTime _date;
    private int _period;
    private bool _isCompleted;
    private DateTime? _completedAt;
    private bool _isCancelled;
    private string _cancelReason = string.Empty;
    private bool _isPinned;
    private string _memo = string.Empty;
    private DateTime _createdAt = DateTime.Now;
    private DateTime _updatedAt = DateTime.Now;

    #endregion

    #region Properties - 기본 정보

    /// <summary>PK (자동 증가)</summary>
    public int No
    {
        get => _no;
        set => SetProperty(ref _no, value);
    }

    /// <summary>과목 번호 (FK: Course.No)</summary>
    public int CourseId
    {
        get => _courseId;
        set => SetProperty(ref _courseId, value);
    }

    /// <summary>학급/강의실 (예: "1-3", "음악실")</summary>
    public string Room
    {
        get => _room;
        set => SetProperty(ref _room, value);
    }

    /// <summary>수업 날짜</summary>
    public DateTime Date
    {
        get => _date;
        set => SetProperty(ref _date, value);
    }

    /// <summary>교시 (1~7)</summary>
    public int Period
    {
        get => _period;
        set => SetProperty(ref _period, value);
    }

    #endregion

    #region Properties - 상태

    /// <summary>수업 완료 여부</summary>
    public bool IsCompleted
    {
        get => _isCompleted;
        set => SetProperty(ref _isCompleted, value);
    }

    /// <summary>완료 일시</summary>
    public DateTime? CompletedAt
    {
        get => _completedAt;
        set => SetProperty(ref _completedAt, value);
    }

    /// <summary>결강 여부</summary>
    public bool IsCancelled
    {
        get => _isCancelled;
        set => SetProperty(ref _isCancelled, value);
    }

    /// <summary>결강 사유</summary>
    public string CancelReason
    {
        get => _cancelReason;
        set => SetProperty(ref _cancelReason, value);
    }

    /// <summary>고정 여부 (밀리기/당기기 제외)</summary>
    public bool IsPinned
    {
        get => _isPinned;
        set => SetProperty(ref _isPinned, value);
    }

    /// <summary>메모</summary>
    public string Memo
    {
        get => _memo;
        set => SetProperty(ref _memo, value);
    }

    #endregion

    #region Properties - 타임스탬프

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

    #region Navigation Properties

    /// <summary>매핑된 단원 목록 (1:N - ScheduleUnitMap)</summary>
    public List<ScheduleUnitMap> UnitMaps { get; set; } = new();

    /// <summary>수업 정보 (Course)</summary>
    public Course? Course { get; set; }

    #endregion

    #region Computed Properties - 표시용

    /// <summary>날짜 표시 (예: "3/15(금)")</summary>
    public string DateDisplay => Date.ToString("M/d(ddd)");

    /// <summary>날짜 전체 표시 (예: "2025-03-15")</summary>
    public string DateFullDisplay => Date.ToString("yyyy-MM-dd");

    /// <summary>교시 표시 (예: "3교시")</summary>
    public string PeriodDisplay => $"{Period}교시";

    /// <summary>슬롯 표시 (예: "3/15(금) 3교시")</summary>
    public string SlotDisplay => $"{DateDisplay} {PeriodDisplay}";

    /// <summary>학급+슬롯 표시 (예: "1-3반 3/15(금) 3교시")</summary>
    public string FullSlotDisplay => $"{Room} {SlotDisplay}";

    /// <summary>요일 (0=일, 1=월, ..., 6=토)</summary>
    public int DayOfWeek => (int)Date.DayOfWeek;

    /// <summary>요일명</summary>
    public string DayName => Date.ToString("ddd");

    #endregion

    #region Computed Properties - 상태

    /// <summary>상태 표시</summary>
    public string StatusDisplay
    {
        get
        {
            if (IsCancelled) return "결강";
            if (IsCompleted) return "완료";
            if (Date.Date < DateTime.Today) return "미완료";
            if (Date.Date == DateTime.Today) return "오늘";
            return "예정";
        }
    }

    /// <summary>상태 색상 (Hex)</summary>
    public string StatusColor
    {
        get
        {
            if (IsCancelled) return "#FF6B6B";      // 빨강
            if (IsCompleted) return "#51CF66";      // 초록
            if (Date.Date < DateTime.Today) return "#FFA94D"; // 주황
            if (Date.Date == DateTime.Today) return "#339AF0"; // 파랑
            return "#868E96"; // 회색
        }
    }

    /// <summary>활성 여부 (결강 아님)</summary>
    public bool IsActive => !IsCancelled;

    /// <summary>이동 가능 여부 (고정되지 않고 완료되지 않음)</summary>
    public bool IsMovable => !IsPinned && !IsCompleted && !IsCancelled;

    /// <summary>과거 수업 여부</summary>
    public bool IsPast => Date.Date < DateTime.Today;

    /// <summary>오늘 수업 여부</summary>
    public bool IsToday => Date.Date == DateTime.Today;

    /// <summary>미래 수업 여부</summary>
    public bool IsFuture => Date.Date > DateTime.Today;

    #endregion

    #region Computed Properties - 단원 관련

    /// <summary>병합 여부 (2개 이상 단원 매핑)</summary>
    public bool IsMerged => UnitMaps.Count > 1;

    /// <summary>단원 매핑 수</summary>
    public int UnitCount => UnitMaps.Count;

    /// <summary>매핑된 단원명 (콤마 구분)</summary>
    public string UnitNames => string.Join(", ", UnitMaps
        .Where(m => m.CourseSection != null)
        .Select(m => m.CourseSection!.SectionName));

    /// <summary>매핑된 단원 전체 경로 (첫 번째)</summary>
    public string FirstUnitFullPath => UnitMaps.FirstOrDefault()?.CourseSection?.FullPath ?? "";

    /// <summary>총 할당 시수</summary>
    public int TotalAllocatedHours => UnitMaps.Sum(m => m.AllocatedHours);

    /// <summary>단원 없음 여부</summary>
    public bool HasNoUnits => UnitMaps.Count == 0;

    #endregion

    #region Methods

    /// <summary>
    /// 완료 처리
    /// </summary>
    public void MarkAsCompleted()
    {
        IsCompleted = true;
        CompletedAt = DateTime.Now;
        UpdatedAt = DateTime.Now;
    }

    /// <summary>
    /// 완료 취소
    /// </summary>
    public void MarkAsIncomplete()
    {
        IsCompleted = false;
        CompletedAt = null;
        UpdatedAt = DateTime.Now;
    }

    /// <summary>
    /// 결강 처리
    /// </summary>
    public void MarkAsCancelled(string reason = "")
    {
        IsCancelled = true;
        CancelReason = reason;
        UpdatedAt = DateTime.Now;
    }

    /// <summary>
    /// 결강 취소
    /// </summary>
    public void MarkAsActive()
    {
        IsCancelled = false;
        CancelReason = string.Empty;
        UpdatedAt = DateTime.Now;
    }

    /// <summary>
    /// 복제본 생성 (새 날짜/교시로)
    /// </summary>
    public Schedule CloneToNewSlot(DateTime newDate, int newPeriod)
    {
        return new Schedule
        {
            CourseId = this.CourseId,
            Room = this.Room,
            Date = newDate,
            Period = newPeriod,
            IsCompleted = false,
            IsCancelled = false,
            IsPinned = false,
            Memo = this.Memo,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
    }

    public override string ToString()
    {
        return $"{Room} {SlotDisplay} ({StatusDisplay})";
    }

    #endregion
}

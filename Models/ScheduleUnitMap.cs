using System;

namespace NewSchool.Models;

/// <summary>
/// 수업-단원 매핑 (1:N 병합 지원)
/// 하나의 Schedule에 여러 CourseSection을 매핑 가능
/// </summary>
public class ScheduleUnitMap : NotifyPropertyChangedBase
{
    #region Fields

    private int _no = -1;
    private int _scheduleId;
    private int _courseSectionId;
    private int _allocatedHours = 1;
    private int _orderInSlot = 1;

    #endregion

    #region Properties - 기본 정보

    /// <summary>PK (자동 증가)</summary>
    public int No
    {
        get => _no;
        set => SetProperty(ref _no, value);
    }

    /// <summary>스케줄 번호 (FK: Schedule.No)</summary>
    public int ScheduleId
    {
        get => _scheduleId;
        set => SetProperty(ref _scheduleId, value);
    }

    /// <summary>단원 번호 (FK: CourseSection.No)</summary>
    public int CourseSectionId
    {
        get => _courseSectionId;
        set => SetProperty(ref _courseSectionId, value);
    }

    #endregion

    #region Properties - 배치 정보

    /// <summary>해당 슬롯에 할당된 시수 (기본 1)</summary>
    public int AllocatedHours
    {
        get => _allocatedHours;
        set => SetProperty(ref _allocatedHours, value);
    }

    /// <summary>슬롯 내 순서 (병합 시 1, 2, 3...)</summary>
    public int OrderInSlot
    {
        get => _orderInSlot;
        set => SetProperty(ref _orderInSlot, value);
    }

    #endregion

    #region Navigation Properties

    /// <summary>스케줄 정보</summary>
    public Schedule? Schedule { get; set; }

    /// <summary>단원 정보</summary>
    public CourseSection? CourseSection { get; set; }

    #endregion

    #region Computed Properties

    /// <summary>단원명 (CourseSection이 설정된 경우)</summary>
    public string SectionName => CourseSection?.SectionName ?? $"단원 #{CourseSectionId}";

    /// <summary>단원 전체 경로</summary>
    public string SectionFullPath => CourseSection?.FullPath ?? "";

    /// <summary>단원 유형</summary>
    public string SectionType => CourseSection?.SectionType ?? "Normal";

    /// <summary>단원 유형 표시</summary>
    public string SectionTypeDisplay => CourseSection?.SectionTypeDisplay ?? "";

    /// <summary>스케줄 슬롯 표시</summary>
    public string ScheduleSlotDisplay => Schedule?.SlotDisplay ?? "";

    /// <summary>스케줄 날짜</summary>
    public DateTime? ScheduleDate => Schedule?.Date;

    /// <summary>할당 시수 표시</summary>
    public string AllocatedHoursDisplay => $"{AllocatedHours}시간";

    /// <summary>첫 번째 항목 여부 (순서가 1인 경우)</summary>
    public bool IsFirst => OrderInSlot == 1;

    #endregion

    #region Methods

    public override string ToString()
    {
        return $"[{ScheduleSlotDisplay}] {SectionName} ({AllocatedHoursDisplay})";
    }

    #endregion
}

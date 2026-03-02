using System;

namespace NewSchool.Models;

/// <summary>
/// 진도 기록 (학급별 단원 완료 상태)
/// </summary>
public class LessonProgress
{
    /// <summary>
    /// 고유 번호 (PK)
    /// </summary>
    public int No { get; set; }

    /// <summary>
    /// 단원 번호 (FK → CourseSection)
    /// </summary>
    public int CourseSectionId { get; set; }

    /// <summary>
    /// 학급/강의실
    /// </summary>
    public string Room { get; set; } = string.Empty;

    /// <summary>
    /// 완료 여부
    /// </summary>
    public bool IsCompleted { get; set; }

    /// <summary>
    /// 완료 날짜
    /// </summary>
    public DateTime? CompletedDate { get; set; }

    /// <summary>
    /// 진도 유형
    /// </summary>
    public ProgressType ProgressType { get; set; } = ProgressType.Normal;

    /// <summary>
    /// 연결된 일정 번호 (FK → Schedule, 선택)
    /// </summary>
    public int? ScheduleId { get; set; }

    /// <summary>
    /// 메모
    /// </summary>
    public string? Memo { get; set; }

    /// <summary>
    /// 생성 시각
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 수정 시각
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    #region Navigation Properties

    /// <summary>
    /// 연결된 단원
    /// </summary>
    public CourseSection? CourseSection { get; set; }

    /// <summary>
    /// 연결된 일정
    /// </summary>
    public Schedule? Schedule { get; set; }

    #endregion

    #region Computed Properties

    /// <summary>
    /// 완료일 표시
    /// </summary>
    public string CompletedDateDisplay => CompletedDate?.ToString("M/d") ?? "-";

    /// <summary>
    /// 진도 유형 표시
    /// </summary>
    public string ProgressTypeDisplay => ProgressType switch
    {
        ProgressType.Normal => "정상",
        ProgressType.Makeup => "보강",
        ProgressType.Merged => "병합",
        ProgressType.Skipped => "건너뜀",
        ProgressType.Cancelled => "결강",
        _ => "알 수 없음"
    };

    /// <summary>
    /// 진도 유형 아이콘
    /// </summary>
    public string ProgressTypeIcon => ProgressType switch
    {
        ProgressType.Normal => "✓",
        ProgressType.Makeup => "➕",
        ProgressType.Merged => "🔗",
        ProgressType.Skipped => "⏭",
        ProgressType.Cancelled => "✕",
        _ => "?"
    };

    /// <summary>
    /// 상태 색상 (UI용)
    /// </summary>
    public string StatusColor => (IsCompleted, ProgressType) switch
    {
        (true, ProgressType.Normal) => "#4CAF50",    // Green
        (true, ProgressType.Makeup) => "#2196F3",    // Blue
        (true, ProgressType.Merged) => "#9C27B0",    // Purple
        (true, ProgressType.Skipped) => "#FF9800",   // Orange
        (_, ProgressType.Cancelled) => "#F44336",    // Red
        _ => "#9E9E9E"                                // Gray
    };

    /// <summary>
    /// 짧은 상태 표시
    /// </summary>
    public string ShortStatus
    {
        get
        {
            if (!IsCompleted && ProgressType != ProgressType.Cancelled && ProgressType != ProgressType.Skipped)
                return "";

            return ProgressType switch
            {
                ProgressType.Normal => "✓",
                ProgressType.Makeup => "+",
                ProgressType.Merged => "M",
                ProgressType.Skipped => "S",
                ProgressType.Cancelled => "X",
                _ => ""
            };
        }
    }

    /// <summary>
    /// 툴팁 텍스트
    /// </summary>
    public string TooltipText
    {
        get
        {
            var text = $"{ProgressTypeDisplay}";
            if (CompletedDate.HasValue)
                text += $" ({CompletedDate:M/d})";
            if (!string.IsNullOrEmpty(Memo))
                text += $"\n{Memo}";
            return text;
        }
    }

    /// <summary>
    /// 단원명 (Navigation Property 사용)
    /// </summary>
    public string SectionName => CourseSection?.SectionName ?? $"단원 #{CourseSectionId}";

    /// <summary>
    /// 수정 가능 여부
    /// </summary>
    public bool IsEditable => ProgressType != ProgressType.Merged;

    #endregion

    #region Methods

    /// <summary>
    /// 완료 처리
    /// </summary>
    public void MarkAsCompleted(DateTime? date = null, int? scheduleId = null)
    {
        IsCompleted = true;
        CompletedDate = date ?? DateTime.Today;
        ScheduleId = scheduleId;
        UpdatedAt = DateTime.Now;
    }

    /// <summary>
    /// 완료 취소
    /// </summary>
    public void MarkAsIncomplete()
    {
        IsCompleted = false;
        CompletedDate = null;
        ScheduleId = null;
        UpdatedAt = DateTime.Now;
    }

    /// <summary>
    /// 보강으로 표시
    /// </summary>
    public void MarkAsMakeup(DateTime date, string? memo = null)
    {
        ProgressType = ProgressType.Makeup;
        IsCompleted = true;
        CompletedDate = date;
        Memo = memo;
        UpdatedAt = DateTime.Now;
    }

    /// <summary>
    /// 건너뛰기 처리
    /// </summary>
    public void MarkAsSkipped(string? reason = null)
    {
        ProgressType = ProgressType.Skipped;
        IsCompleted = true;
        CompletedDate = DateTime.Today;
        Memo = reason;
        UpdatedAt = DateTime.Now;
    }

    /// <summary>
    /// 결강 처리
    /// </summary>
    public void MarkAsCancelled(string? reason = null)
    {
        ProgressType = ProgressType.Cancelled;
        IsCompleted = false;
        Memo = reason;
        UpdatedAt = DateTime.Now;
    }

    /// <summary>
    /// 복제 (다른 학급용)
    /// </summary>
    public LessonProgress CloneForRoom(string newRoom)
    {
        return new LessonProgress
        {
            CourseSectionId = CourseSectionId,
            Room = newRoom,
            IsCompleted = false,
            ProgressType = ProgressType.Normal,
            CreatedAt = DateTime.Now
        };
    }

    #endregion
}

/// <summary>
/// 진도 유형
/// </summary>
public enum ProgressType
{
    /// <summary>
    /// 정상 수업
    /// </summary>
    Normal = 0,

    /// <summary>
    /// 보강 수업
    /// </summary>
    Makeup = 1,

    /// <summary>
    /// 병합 수업 (여러 단원 한 번에)
    /// </summary>
    Merged = 2,

    /// <summary>
    /// 건너뜀
    /// </summary>
    Skipped = 3,

    /// <summary>
    /// 결강
    /// </summary>
    Cancelled = 4
}

/// <summary>
/// 진도 격차 정보
/// </summary>
public class ProgressGap
{
    /// <summary>
    /// 학급
    /// </summary>
    public string Room { get; set; } = string.Empty;

    /// <summary>
    /// 완료된 단원 수
    /// </summary>
    public int CompletedCount { get; set; }

    /// <summary>
    /// 전체 단원 수
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// 완료율 (%)
    /// </summary>
    public double CompletionRate => TotalCount > 0 
        ? Math.Round((double)CompletedCount / TotalCount * 100, 1) 
        : 0;

    /// <summary>
    /// 최대 완료 학급 대비 격차
    /// </summary>
    public int GapFromMax { get; set; }

    /// <summary>
    /// 평균 대비 격차
    /// </summary>
    public double GapFromAverage { get; set; }

    /// <summary>
    /// 격차 상태
    /// </summary>
    public GapStatus Status => GapFromMax switch
    {
        0 => GapStatus.Leading,
        1 or 2 => GapStatus.OnTrack,
        3 or 4 => GapStatus.SlightlyBehind,
        _ => GapStatus.Behind
    };

    /// <summary>
    /// 상태 표시
    /// </summary>
    public string StatusDisplay => Status switch
    {
        GapStatus.Leading => "선두",
        GapStatus.OnTrack => "정상",
        GapStatus.SlightlyBehind => "약간 뒤처짐",
        GapStatus.Behind => "뒤처짐",
        _ => "알 수 없음"
    };

    /// <summary>
    /// 상태 색상
    /// </summary>
    public string StatusColor => Status switch
    {
        GapStatus.Leading => "#4CAF50",
        GapStatus.OnTrack => "#8BC34A",
        GapStatus.SlightlyBehind => "#FF9800",
        GapStatus.Behind => "#F44336",
        _ => "#9E9E9E"
    };
}

/// <summary>
/// 격차 상태
/// </summary>
public enum GapStatus
{
    /// <summary>
    /// 선두 (가장 빠름)
    /// </summary>
    Leading,

    /// <summary>
    /// 정상 범위
    /// </summary>
    OnTrack,

    /// <summary>
    /// 약간 뒤처짐
    /// </summary>
    SlightlyBehind,

    /// <summary>
    /// 뒤처짐
    /// </summary>
    Behind
}

/// <summary>
/// 진도 매트릭스 셀
/// </summary>
public class ProgressMatrixCell
{
    /// <summary>
    /// 단원 번호
    /// </summary>
    public int CourseSectionId { get; set; }

    /// <summary>
    /// 학급
    /// </summary>
    public string Room { get; set; } = string.Empty;

    /// <summary>
    /// 진도 기록 (있는 경우)
    /// </summary>
    public LessonProgress? Progress { get; set; }

    /// <summary>
    /// 완료 여부
    /// </summary>
    public bool IsCompleted => Progress?.IsCompleted ?? false;

    /// <summary>
    /// 진도 유형
    /// </summary>
    public ProgressType ProgressType => Progress?.ProgressType ?? ProgressType.Normal;

    /// <summary>
    /// 표시 텍스트
    /// </summary>
    public string DisplayText => Progress?.ShortStatus ?? "";

    /// <summary>
    /// 배경색
    /// </summary>
    public string BackgroundColor => Progress?.StatusColor ?? "Transparent";
}

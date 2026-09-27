using System;

namespace NewSchool.Models;

/// <summary>
/// 진도 기록 (학급/강의실별 단원 완료 상태)
///
/// ⚠ 1.0 정리에서 진도 관리를 통째로 걷어냈다가 되살린 모델이다.
/// 그때 함께 사라진 <c>Schedule</c>(연간계획 전용 자동배치 테이블)을 참조하던
/// <c>ScheduleId</c> 는 되살리지 않았다 — 부모 테이블이 없는 FK 는
/// <c>foreign_keys=ON</c> 에서 INSERT 를 통째로 막는다.
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

    #endregion

    #region Computed Properties

    // CompletedDateDisplay 는 바인딩도 호출도 없어 지웠다(39차).

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
    /// 짧은 상태 표시 (매트릭스 셀 안에 찍는 한 글자)
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
            var text = ProgressTypeDisplay;
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

    #endregion

    #region Methods

    /// <summary>
    /// 완료 처리
    /// </summary>
    public void MarkAsCompleted(DateTime? date = null)
    {
        IsCompleted = true;
        ProgressType = ProgressType.Normal;
        CompletedDate = date ?? DateTime.Today;
        UpdatedAt = DateTime.Now;
    }

    /// <summary>
    /// 완료 취소. 유형도 함께 되돌린다 — 예전에는 <c>IsCompleted</c> 만 내려서
    /// 보강·건너뜀으로 칠해진 셀이 미완료로 바꿔도 색이 남았다.
    /// </summary>
    public void MarkAsIncomplete()
    {
        IsCompleted = false;
        ProgressType = ProgressType.Normal;
        CompletedDate = null;
        Memo = null;
        UpdatedAt = DateTime.Now;
    }

    // 보강·병합·건너뜀·결강 표시(MarkAsMakeup·MarkAsMerged·MarkAsSkipped·MarkAsCancelled)는
    // 지웠다(2026-09-28) — 진도 칸 메뉴에서 그 항목들을 뺀 뒤로 부르는 곳이 없었다.
    // 휴강·보강은 LessonChange 로 넣고, 건너뛴 단원은 뒤 단원을 완료로 표시한다.
    // 예전 기록을 읽어야 하므로 ProgressType 값은 그대로 둔다.

    #endregion
}

/// <summary>
/// 진도 유형
/// </summary>
public enum ProgressType
{
    /// <summary>정상 수업</summary>
    Normal = 0,

    /// <summary>보강 수업</summary>
    Makeup = 1,

    /// <summary>병합 수업 (여러 단원 한 번에)</summary>
    Merged = 2,

    /// <summary>건너뜀</summary>
    Skipped = 3,

    /// <summary>결강</summary>
    Cancelled = 4
}

// 진도 격차(ProgressGap·GapStatus)는 이를 채우던 LessonProgressRepository.GetProgressGapsAsync
// 와 함께 지웠다(2026-09-28) — 격차 분석 창이 학급 머리의 남은 시간·차시로 바뀐 뒤로
// 부르는 곳이 없었다.

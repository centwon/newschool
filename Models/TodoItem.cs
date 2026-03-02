using System;

namespace NewSchool.Models;

/// <summary>
/// 할 일 모델
/// 체크리스트 형태의 업무 관리
/// </summary>
public class TodoItem : NotifyPropertyChangedBase, IEntity
{
    #region Fields

    private int _no = -1;
    private string _teacherId = string.Empty;
    private string _title = string.Empty;
    private string _description = string.Empty;
    private TodoPriority _priority = TodoPriority.보통;
    private TodoStatus _status = TodoStatus.미완료;
    private DateTime? _dueDate;
    private DateTime? _completedAt;
    private string _tag = string.Empty;
    private int _sortOrder;
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

    /// <summary>교사 ID</summary>
    public string TeacherID
    {
        get => _teacherId;
        set => SetProperty(ref _teacherId, value);
    }

    /// <summary>제목</summary>
    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value ?? string.Empty);
    }

    /// <summary>상세 내용</summary>
    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value ?? string.Empty);
    }

    /// <summary>우선순위</summary>
    public TodoPriority Priority
    {
        get => _priority;
        set => SetProperty(ref _priority, value);
    }

    /// <summary>상태</summary>
    public TodoStatus Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    /// <summary>마감일 (없으면 null)</summary>
    public DateTime? DueDate
    {
        get => _dueDate;
        set => SetProperty(ref _dueDate, value);
    }

    /// <summary>완료 일시</summary>
    public DateTime? CompletedAt
    {
        get => _completedAt;
        set => SetProperty(ref _completedAt, value);
    }

    /// <summary>태그 (쉼표 구분)</summary>
    public string Tag
    {
        get => _tag;
        set => SetProperty(ref _tag, value ?? string.Empty);
    }

    /// <summary>정렬 순서</summary>
    public int SortOrder
    {
        get => _sortOrder;
        set => SetProperty(ref _sortOrder, value);
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

    /// <summary>완료 여부</summary>
    public bool IsCompleted => Status == TodoStatus.완료;

    /// <summary>마감일 초과 여부</summary>
    public bool IsOverdue => !IsCompleted && DueDate.HasValue && DueDate.Value.Date < DateTime.Today;

    /// <summary>오늘 마감 여부</summary>
    public bool IsDueToday => !IsCompleted && DueDate.HasValue && DueDate.Value.Date == DateTime.Today;

    /// <summary>마감일까지 남은 일수</summary>
    public int? DaysUntilDue => DueDate.HasValue ? (DueDate.Value.Date - DateTime.Today).Days : null;

    /// <summary>마감일 표시</summary>
    public string DueDateDisplay => DueDate.HasValue
        ? DueDate.Value.ToString("M/d(ddd)")
        : "마감일 없음";

    /// <summary>마감 상태 표시</summary>
    public string DueStatusDisplay
    {
        get
        {
            if (IsCompleted) return "✅ 완료";
            if (!DueDate.HasValue) return "";
            if (IsOverdue) return $"⚠ {-DaysUntilDue!.Value}일 초과";
            if (IsDueToday) return "🔴 오늘 마감";
            if (DaysUntilDue <= 3) return $"🟡 {DaysUntilDue}일 남음";
            return $"{DaysUntilDue}일 남음";
        }
    }

    /// <summary>우선순위 표시</summary>
    public string PriorityDisplay => Priority switch
    {
        TodoPriority.긴급 => "🔴 긴급",
        TodoPriority.높음 => "🟠 높음",
        TodoPriority.보통 => "🟡 보통",
        TodoPriority.낮음 => "🟢 낮음",
        _ => "보통"
    };

    /// <summary>우선순위 색상</summary>
    public string PriorityColor => Priority switch
    {
        TodoPriority.긴급 => "#FFE74C3C",
        TodoPriority.높음 => "#FFFF9800",
        TodoPriority.보통 => "#FFFFC107",
        TodoPriority.낮음 => "#FF27AE60",
        _ => "#FFBDC3C7"
    };

    #endregion

    #region Methods

    /// <summary>완료 처리</summary>
    public void MarkAsCompleted()
    {
        Status = TodoStatus.완료;
        CompletedAt = DateTime.Now;
        UpdatedAt = DateTime.Now;
    }

    /// <summary>완료 취소</summary>
    public void MarkAsIncomplete()
    {
        Status = TodoStatus.미완료;
        CompletedAt = null;
        UpdatedAt = DateTime.Now;
    }

    /// <summary>토글</summary>
    public void ToggleCompleted()
    {
        if (IsCompleted) MarkAsIncomplete();
        else MarkAsCompleted();
    }

    public override string ToString()
        => $"[{Priority}] {Title} ({DueStatusDisplay})";

    #endregion
}

/// <summary>
/// 할 일 우선순위
/// </summary>
public enum TodoPriority
{
    낮음,
    보통,
    높음,
    긴급
}

/// <summary>
/// 할 일 상태
/// </summary>
public enum TodoStatus
{
    미완료,
    진행중,
    완료
}

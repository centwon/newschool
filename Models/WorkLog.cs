using System;

namespace NewSchool.Models;

/// <summary>
/// 업무 일지 모델
/// 일일 업무 기록, 회의록, 메모 관리
/// </summary>
public class WorkLog : NotifyPropertyChangedBase, IEntity
{
    #region Fields

    private int _no = -1;
    private string _teacherId = string.Empty;
    private DateTime _date = DateTime.Today;
    private WorkLogCategory _category = WorkLogCategory.일반;
    private string _title = string.Empty;
    private string _content = string.Empty;
    private string _tag = string.Empty;
    private bool _isImportant;
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

    /// <summary>작성 교사 ID (FK: Teacher.TeacherID)</summary>
    public string TeacherID
    {
        get => _teacherId;
        set => SetProperty(ref _teacherId, value);
    }

    /// <summary>날짜</summary>
    public DateTime Date
    {
        get => _date;
        set => SetProperty(ref _date, value.Date);
    }

    /// <summary>카테고리</summary>
    public WorkLogCategory Category
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

    /// <summary>내용</summary>
    public string Content
    {
        get => _content;
        set => SetProperty(ref _content, value ?? string.Empty);
    }

    /// <summary>태그 (쉼표 구분)</summary>
    public string Tag
    {
        get => _tag;
        set => SetProperty(ref _tag, value ?? string.Empty);
    }

    /// <summary>중요 표시</summary>
    public bool IsImportant
    {
        get => _isImportant;
        set => SetProperty(ref _isImportant, value);
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

    /// <summary>날짜 표시</summary>
    public string DateDisplay => Date.ToString("yyyy-MM-dd (ddd)");

    /// <summary>카테고리 표시</summary>
    public string CategoryDisplay => Category.ToString();

    /// <summary>내용 미리보기 (50자)</summary>
    public string ContentPreview => Content.Length > 50
        ? Content[..50] + "..."
        : Content;

    /// <summary>내용 있음 여부</summary>
    public bool HasContent => !string.IsNullOrWhiteSpace(Content);

    #endregion

    #region Methods

    public override string ToString()
        => $"[{Category}] {Date:yyyy-MM-dd} {Title}";

    #endregion
}

/// <summary>
/// 업무 일지 카테고리
/// </summary>
public enum WorkLogCategory
{
    일반,
    회의,
    공문,
    행사,
    연수,
    학부모,
    기타
}

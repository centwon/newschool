using System;
using System.IO;

namespace NewSchool.Models;

/// <summary>
/// 공통 첨부파일 모델
/// OwnerType + OwnerNo로 어떤 레코드에든 첨부 가능
/// 지원 대상: LessonLog, ClassDiary, CourseSection
/// </summary>
public class Attachment : NotifyPropertyChangedBase, IEntity
{
    #region Fields

    private int _no = -1;
    private string _ownerType = string.Empty;
    private int _ownerNo;
    private string _fileName = string.Empty;
    private string _originalFileName = string.Empty;
    private long _fileSize;
    private string _filePath = string.Empty;
    private string _contentType = string.Empty;
    private string _description = string.Empty;
    private int _sortOrder;
    private DateTime _createdAt = DateTime.Now;

    #endregion

    #region Properties

    /// <summary>PK (자동 증가)</summary>
    public int No
    {
        get => _no;
        set => SetProperty(ref _no, value);
    }

    /// <summary>
    /// 소유자 유형
    /// "LessonLog", "ClassDiary", "CourseSection"
    /// </summary>
    public string OwnerType
    {
        get => _ownerType;
        set => SetProperty(ref _ownerType, value);
    }

    /// <summary>소유자 레코드의 No (FK 역할)</summary>
    public int OwnerNo
    {
        get => _ownerNo;
        set => SetProperty(ref _ownerNo, value);
    }

    /// <summary>저장된 파일명 (GUID 등 중복 방지 처리된 이름)</summary>
    public string FileName
    {
        get => _fileName;
        set => SetProperty(ref _fileName, value);
    }

    /// <summary>원본 파일명 (사용자에게 표시)</summary>
    public string OriginalFileName
    {
        get => _originalFileName;
        set => SetProperty(ref _originalFileName, value);
    }

    /// <summary>파일 크기 (바이트)</summary>
    public long FileSize
    {
        get => _fileSize;
        set => SetProperty(ref _fileSize, value);
    }

    /// <summary>파일 저장 경로 (Data/Attachments/{OwnerType}/{OwnerNo}/...)</summary>
    public string FilePath
    {
        get => _filePath;
        set => SetProperty(ref _filePath, value);
    }

    /// <summary>MIME 타입 (예: "application/pdf", "image/png")</summary>
    public string ContentType
    {
        get => _contentType;
        set => SetProperty(ref _contentType, value);
    }

    /// <summary>설명 (선택사항)</summary>
    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
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

    #endregion

    #region Computed Properties

    /// <summary>파일 확장자 (소문자, 예: ".pdf")</summary>
    public string Extension => Path.GetExtension(OriginalFileName)?.ToLowerInvariant() ?? "";

    /// <summary>파일 크기 표시 (KB/MB)</summary>
    public string FileSizeDisplay
    {
        get
        {
            if (FileSize < 1024) return $"{FileSize} B";
            if (FileSize < 1024 * 1024) return $"{FileSize / 1024.0:F1} KB";
            return $"{FileSize / (1024.0 * 1024.0):F1} MB";
        }
    }

    /// <summary>이미지 파일 여부</summary>
    public bool IsImage => Extension is ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp";

    /// <summary>문서 파일 여부</summary>
    public bool IsDocument => Extension is ".pdf" or ".doc" or ".docx" or ".ppt" or ".pptx" or ".xls" or ".xlsx" or ".hwp" or ".hwpx";

    /// <summary>파일 아이콘 (Segoe Fluent Icons)</summary>
    public string FileIcon => Extension switch
    {
        ".pdf" => "\uEA90",
        ".doc" or ".docx" => "\uE8A5",
        ".ppt" or ".pptx" => "\uE8A1",
        ".xls" or ".xlsx" => "\uE80A",
        ".hwp" or ".hwpx" => "\uE8A5",
        ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" => "\uEB9F",
        ".mp4" or ".avi" or ".mkv" => "\uE8B2",
        ".mp3" or ".wav" => "\uE8D6",
        ".zip" or ".rar" or ".7z" => "\uE8DE",
        _ => "\uE8A5"
    };

    /// <summary>실제 파일 존재 여부</summary>
    public bool FileExists => !string.IsNullOrEmpty(FilePath) && File.Exists(FilePath);

    #endregion

    #region Methods

    public override string ToString()
    {
        return $"{OriginalFileName} ({FileSizeDisplay})";
    }

    #endregion
}

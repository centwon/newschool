using System.ComponentModel;
using System.Runtime.CompilerServices;
using NewSchool.Helpers;
using NewSchool.Models;

namespace NewSchool.ViewModels;

/// <summary>
/// StudentSpecial ViewModel
/// UI 바인딩 및 선택 상태 관리
/// </summary>
public class StudentSpecialViewModel : INotifyPropertyChanged
{
    private StudentSpecial _special;
    private bool _isSelected;
    private bool _isModified;
    private string _byteInfo = string.Empty;
    private string _originalContent = string.Empty;

    // 학생 정보 (외부에서 설정)
    private int _grade;
    private int _classNum;
    private int _number;
    private string _studentName = string.Empty;

    public StudentSpecialViewModel(StudentSpecial special)
    {
        _special = special;
        _originalContent = special.Content ?? string.Empty;
        UpdateByteInfo();
    }

    public StudentSpecialViewModel(StudentSpecial special, int grade, int classNum, int number, string studentName)
    {
        _special = special;
        _originalContent = special.Content ?? string.Empty;
        _grade = grade;
        _classNum = classNum;
        _number = number;
        _studentName = studentName;
        UpdateByteInfo();
    }

    #region Properties

    /// <summary>
    /// 원본 StudentSpecial 모델
    /// </summary>
    public StudentSpecial Special
    {
        get => _special;
        set
        {
            if (_special != value)
            {
                _special = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(No));
                OnPropertyChanged(nameof(StudentID));
                OnPropertyChanged(nameof(Year));
                OnPropertyChanged(nameof(Type));
                OnPropertyChanged(nameof(Content));
                UpdateByteInfo();
            }
        }
    }

    /// <summary>
    /// 체크박스 선택 여부
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// 내용 변경 여부 (원본 대비)
    /// </summary>
    public bool IsModified
    {
        get => _isModified;
        private set
        {
            if (_isModified != value)
            {
                _isModified = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// 바이트 정보 (읽기 전용)
    /// </summary>
    public string ByteInfo
    {
        get => _byteInfo;
        private set
        {
            if (_byteInfo != value)
            {
                _byteInfo = value;
                OnPropertyChanged();
            }
        }
    }

    #endregion

    #region StudentSpecial Properties (바인딩용)

    public int No => _special.No;
    public string StudentID => _special.StudentID;
    public int Year => _special.Year;
    public string Type => _special.Type;
    public string Title => _special.Title;

    public string Content
    {
        get => _special.Content;
        set
        {
            if (_special.Content != value)
            {
                _special.Content = value;
                IsModified = (value ?? string.Empty) != _originalContent;
                if (IsModified) IsSelected = true;
                OnPropertyChanged();
                UpdateByteInfo();
            }
        }
    }

    public string Date => _special.Date;
    public string TeacherID => _special.TeacherID;
    public int CourseNo => _special.CourseNo;
    public string SubjectName => _special.SubjectName;
    public bool IsFinalized => _special.IsFinalized;
    public string Tag => _special.Tag;

    #endregion

    #region Student Info Properties (학생 정보)

    /// <summary>학년</summary>
    public int Grade
    {
        get => _grade;
        set
        {
            if (_grade != value)
            {
                _grade = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>반</summary>
    public int ClassNum
    {
        get => _classNum;
        set
        {
            if (_classNum != value)
            {
                _classNum = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>번호</summary>
    public int Number
    {
        get => _number;
        set
        {
            if (_number != value)
            {
                _number = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>학생 이름</summary>
    public string StudentName
    {
        get => _studentName;
        set
        {
            if (_studentName != value)
            {
                _studentName = value ?? string.Empty;
                OnPropertyChanged();
            }
        }
    }

    #endregion

    #region Methods

    /// <summary>
    /// 저장 성공 후 변경 상태 초기화
    /// </summary>
    public void MarkAsSaved()
    {
        _originalContent = _special.Content ?? string.Empty;
        IsModified = false;
        IsSelected = false;
    }

    /// <summary>
    /// 바이트 정보 업데이트
    /// </summary>
    private void UpdateByteInfo()
    {
        if (_special == null)
        {
            ByteInfo = "0 Byte (0자)";
            return;
        }

        int currentBytes = NeisHelper.CountByte(_special.Content);
        int maxBytes = NeisHelper.GetMaxBytes(_special.Type);
        int charCount = _special.Content?.Length ?? 0;

        ByteInfo = $"{currentBytes} / {maxBytes} Byte ({charCount}자)";
    }

    #endregion

    #region INotifyPropertyChanged

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    #endregion
}

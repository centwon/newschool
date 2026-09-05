using System.ComponentModel;
using System.Runtime.CompilerServices;
using NewSchool.Helpers;
using NewSchool.Models;

namespace NewSchool.ViewModels;

/// <summary>
/// StudentSpecial ViewModel
/// UI 바인딩 및 선택 상태 관리
/// </summary>
public class StudentSpecialViewModel : NotifyPropertyChangedBase
{
    private StudentSpecial _special;
    private bool _isSelected;
    private bool _isModified;
    private string _byteInfo = string.Empty;
    private bool _isOverByteLimit;
    private string _originalContent = string.Empty;
    private double _contentFontSize = DefaultContentFontSize;

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

    /// <summary>
    /// 한도를 넘겼는가. <b>목록에서도 눈에 띄게 하려고</b> 둔다.
    ///
    /// <para>⚠ 한 건씩 쓰는 화면(<c>StudentSpecBox</c>)과 일괄 입력 대화상자는 넘기면
    /// 글씨를 빨갛게 바꾸는데, <b>정작 40명 × 여러 영역을 한눈에 훑는 목록만</b> 회색으로
    /// 고정돼 있었다 — 넘긴 줄을 찾으려면 숫자를 하나씩 읽어야 했다(2026-09-05).</para>
    /// </summary>
    public bool IsOverByteLimit
    {
        get => _isOverByteLimit;
        private set
        {
            if (_isOverByteLimit != value)
            {
                _isOverByteLimit = value;
                OnPropertyChanged();
            }
        }
    }

    #endregion

    #region StudentSpecial Properties (바인딩용)

    /// <summary>
    /// 기록 내용 칸의 기본 글자 크기. <b>각 페이지의 글자 크기 슬라이더 기본값(Value)도
    /// 이 값과 같아야 한다</b> — 다르면 슬라이더를 처음 건드리는 순간 글자가 한 번 튄다.
    /// </summary>
    public const double DefaultContentFontSize = 14.0;

    /// <summary>
    /// 기록 내용 칸의 글자 크기. 툴바의 "글자 크기" 슬라이더가 이 값만 바꾼다.
    ///
    /// ⚠ 목록 컨트롤의 <c>FontSize</c> 를 직접 바꾸면 안 된다. 행의 각 칸(학년도·이름·과목…)에
    /// <c>FontSize="12"</c> 가 명시돼 있어 상속값이 먹히지 않고, 크기가 명시되지 않은
    /// <b>헤더 라벨만</b> 커진다 — 실제로 그렇게 동작해서 "기록은 그대로인데 엉뚱한 데가 커진다"는
    /// 문제가 있었다(2026-07-30 수정). 그래서 기록 내용 칸이 이 속성을 직접 바인딩한다.
    /// </summary>
    public double ContentFontSize
    {
        get => _contentFontSize;
        set
        {
            if (_contentFontSize != value)
            {
                _contentFontSize = value;
                OnPropertyChanged();
            }
        }
    }

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
    public int Semester => _special.Semester;

    /// <summary>
    /// 목록의 "과목/분야" 칸 표시값. 영역에 따라 담기는 정보가 달라서 한 칸에 모아 보여준다.
    ///  · 교과활동   → 과목명 + 학기("국어 (2학기)") — 교과 세특만 학기별이므로 학기를 함께 노출
    ///  · 개인별세특 → 과목명만(학년 단위라 학기 없음)
    ///  · 진로활동   → 희망분야(Title). 분량이 특기사항과 합산되므로 같은 행에서 함께 보여준다
    ///  · 그 외      → 빈칸
    /// </summary>
    public string SubjectDisplay => Helpers.NeisHelper.BuildSubjectDisplay(
        _special.Type, _special.SubjectName, _special.Title, _special.Semester);

    /// <summary>
    /// 목록에서 직접 편집 가능한 Title(진로활동 희망분야). 그 외 영역에서는 편집칸을 숨긴다.
    /// 변경 시 <see cref="Content"/> 와 동일하게 수정 표시·바이트 정보를 갱신한다.
    /// </summary>
    public string TitleEditable
    {
        get => _special.Title ?? string.Empty;
        set
        {
            if ((_special.Title ?? string.Empty) == (value ?? string.Empty)) return;
            _special.Title = value ?? string.Empty;
            IsModified = true;
            IsSelected = true;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SubjectDisplay));
            UpdateByteInfo();
        }
    }

    /// <summary>진로활동처럼 희망분야를 목록에서 입력하는 영역인가(편집칸 표시 조건).</summary>
    public bool IsTitleEditable => Helpers.NeisHelper.TitleCountsInBytes(_special.Type);

    /// <summary>희망분야 편집칸의 표시/숨김.</summary>
    public Microsoft.UI.Xaml.Visibility TitleEditVisibility =>
        IsTitleEditable ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    /// <summary>희망분야 편집칸이 뜨면 텍스트 표시는 감춘다(같은 칸을 공유).</summary>
    public Microsoft.UI.Xaml.Visibility SubjectTextVisibility =>
        IsTitleEditable ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
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
            IsOverByteLimit = false;
            return;
        }

        int currentBytes = NeisHelper.CountSpecBytes(_special.Type, _special.Title, _special.Content);
        int maxBytes = Settings.GetSpecMaxBytes(_special.Type, _special.Year);   // 설정 오버라이드 반영(입력 화면과 동일 기준)
        int charCount = _special.Content?.Length ?? 0;

        ByteInfo = $"{currentBytes} / {maxBytes} Byte ({charCount}자)";
        IsOverByteLimit = NeisHelper.IsOverLimit(currentBytes, maxBytes);
    }

    #endregion

    #region INotifyPropertyChanged



    #endregion
}

using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Data;

namespace NewSchool.ViewModels;

/// <summary>
/// 학생 목록 표시용 ViewModel
/// Enrollment + Student 조합
/// ListStudent 컨트롤에서 사용
/// WinUI3 x:Bind를 위한 Bindable 특성 추가
/// </summary>
[Microsoft.UI.Xaml.Data.Bindable]
public class StudentListItemViewModel : INotifyPropertyChanged
{
    #region Fields

    private bool _isSelected;
    private int _enrollmentNo;
    private string _studentId = string.Empty;
    private string _schoolCode = string.Empty;
    private int _year;
    private int _semester;
    private int _grade;
    private int _class;
    private int _number;
    private string _name = string.Empty;
    private string _sex = string.Empty;
    private string _status = string.Empty;

    #endregion

    #region Properties - 선택 상태

    /// <summary>체크박스 선택 여부 (다중 선택 모드용)</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    #endregion

    #region Properties - Enrollment 정보

    /// <summary>Enrollment PK</summary>
    public int EnrollmentNo
    {
        get => _enrollmentNo;
        set => SetProperty(ref _enrollmentNo, value);
    }

    /// <summary>학생 ID (FK)</summary>
    public string StudentID
    {
        get => _studentId;
        set => SetProperty(ref _studentId, value);
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

    /// <summary>학기</summary>
    public int Semester
    {
        get => _semester;
        set => SetProperty(ref _semester, value);
    }

    /// <summary>학년</summary>
    public int Grade
    {
        get => _grade;
        set => SetProperty(ref _grade, value);
    }

    /// <summary>반</summary>
    public int Class
    {
        get => _class;
        set => SetProperty(ref _class, value);
    }

    /// <summary>번호</summary>
    public int Number
    {
        get => _number;
        set => SetProperty(ref _number, value);
    }

    /// <summary>재학 상태 (재학/전학/졸업)</summary>
    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    #endregion

    #region Properties - Student 정보

    /// <summary>이름</summary>
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    /// <summary>성별 (남/여)</summary>
    public string Sex
    {
        get => _sex;
        set => SetProperty(ref _sex, value);
    }

    #endregion

    #region Computed Properties - 표시용

    /// <summary>학급 정보 (예: 1학년 1반)</summary>
    public string ClassInfo => $"{Grade}학년 {Class}반";

    /// <summary>번호와 이름 (예: 1번 홍길동)</summary>
    public string NumberAndName => $"{Number}번 {Name}";

    /// <summary>전체 정보 (예: 1학년 1반 1번 홍길동)</summary>
    public string FullInfo => $"{Grade}학년 {Class}반 {Number}번 {Name}";

    #endregion

    #region INotifyPropertyChanged

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    #endregion
}

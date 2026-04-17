using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Controls;
using NewSchool.Dialogs;
using NewSchool.Models;
using NewSchool.Services;

namespace NewSchool.Pages;

/// <summary>
/// 프로그램 초기 설정 창
/// 학교 검색 → 사용자 정보 입력 → 학년도/학기 설정
/// </summary>
public sealed partial class InitialSetupWindow : Window, INotifyPropertyChanged
{
    private School? _selectedSchool;
    private bool _isSchoolSelected;
    private bool _isUserNameEntered;
    private bool _isYearSemesterSet;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// 설정 완료 여부
    /// </summary>
    public bool IsSetupComplete => _isSchoolSelected && _isUserNameEntered && _isYearSemesterSet;

    /// <summary>
    /// 창이 정상적으로 완료되었는지 여부
    /// </summary>
    public bool IsCompleted { get; private set; }

    public InitialSetupWindow()
    {
        this.InitializeComponent();

        // 현재 연도로 기본값 설정
        WorkYearNumberBox.Value = DateTime.Now.Year;

        // 현재 월에 따라 학기 설정 (3-8월: 1학기, 9-2월: 2학기)
        int currentMonth = DateTime.Now.Month;
        WorkSemesterComboBox.SelectedIndex = (currentMonth >= 3 && currentMonth <= 8) ? 0 : 1;

        _isYearSemesterSet = true;

        UpdateSetupStatus();
    }

    #region 1단계: 학교 검색

    private async void OnSearchSchoolClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new SchoolSearchDialog
            {
                XamlRoot = this.Content.XamlRoot
            };

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary && dialog.SelectedSchool != null)
            {
                _selectedSchool = dialog.SelectedSchool;

                SchoolNameTextBox.Text = _selectedSchool.SchoolName;
                SchoolCodeTextBox.Text = _selectedSchool.SchoolCode;
                SchoolAddressTextBox.Text = _selectedSchool.Address;

                SchoolInfoBar.IsOpen = true;
                _isSchoolSelected = true;

                UpdateSetupStatus();

                Debug.WriteLine($"[InitialSetupWindow] 학교 선택: {_selectedSchool.SchoolName}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InitialSetupWindow] 학교 검색 오류: {ex.Message}");
            await MessageBox.ShowAsync(ex.Message, "학교 검색 오류");
        }
    }

    #endregion

    #region 2단계: 사용자 정보

    private void OnUserNameChanged(object sender, TextChangedEventArgs e)
    {
        _isUserNameEntered = !string.IsNullOrWhiteSpace(UserNameTextBox.Text);
        UpdateSetupStatus();
    }

    #endregion

    #region 3단계: 학년도/학기

    private void OnWorkYearChanged(NumberBox sender, NumberBoxValueChangedEventArgs e)
    {
        CheckYearSemesterSet();
    }

    private void OnWorkSemesterChanged(object sender, SelectionChangedEventArgs e)
    {
        CheckYearSemesterSet();
    }

    private void CheckYearSemesterSet()
    {
        _isYearSemesterSet = WorkYearNumberBox.Value > 0 &&
                             WorkSemesterComboBox.SelectedIndex >= 0;
        UpdateSetupStatus();
    }

    #endregion

    #region 버튼 이벤트

    private async void OnCompleteClick(object sender, RoutedEventArgs e)
    {
        // 유효성 검사
        if (!await ValidateInput())
            return;

        try
        {
            // 1. School 테이블에 저장
            await SaveSchoolAsync();

            // 2. Teacher 테이블에 현재 사용자 저장
            string teacherId = await SaveCurrentUserAsync();

            // 3. Settings에 저장
            await SaveSettingsAsync(teacherId);

            Debug.WriteLine("[InitialSetupWindow] 초기 설정 완료");

            IsCompleted = true;
            this.Close();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InitialSetupWindow] 초기 설정 저장 실패: {ex.Message}");
            await MessageBox.ShowAsync(ex.Message, "설정 저장 오류");
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        IsCompleted = false;
        this.Close();
    }

    #endregion

    #region 저장 로직

    /// <summary>
    /// 입력값 유효성 검사
    /// </summary>
    private async Task<bool> ValidateInput()
    {
        if (_selectedSchool == null)
        {
            await MessageBox.ShowAsync("학교를 선택해주세요.", "학교 선택 오류");
            return false;
        }

        if (string.IsNullOrWhiteSpace(UserNameTextBox.Text))
        {
            await MessageBox.ShowAsync("이름을 입력해주세요.", "사용자 정보 오류");
            return false;
        }

        if (WorkYearNumberBox.Value <= 0)
        {
            await MessageBox.ShowAsync("학년도를 입력해주세요.", "학년도 오류");
            return false;
        }

        if (WorkSemesterComboBox.SelectedIndex < 0)
        {
            await MessageBox.ShowAsync("학기를 선택해주세요.", "학기 오류");
            return false;
        }

        return true;
    }

    /// <summary>
    /// School 테이블에 저장
    /// </summary>
    private async Task SaveSchoolAsync()
    {
        if (_selectedSchool == null) return;

        using var schoolService = new SchoolService(SchoolDatabase.DbPath);
        await schoolService.SaveSchoolAsync(_selectedSchool);
        Debug.WriteLine("[InitialSetupWindow] 학교 정보 저장 완료");
    }

    /// <summary>
    /// Teacher 테이블에 현재 사용자 저장
    /// </summary>
    private async Task<string> SaveCurrentUserAsync()
    {
        if (_selectedSchool == null)
            throw new InvalidOperationException("학교가 선택되지 않았습니다.");

        using var teacherService = new TeacherService(SchoolDatabase.DbPath);

        // TeacherID 자동 생성 (T + YYYYMMDDHHMMSS + 난수 4자리)
        var now = DateTime.Now;
        var random = new Random().Next(1000, 9999);
        string teacherId = $"T{now:yyyyMMddHHmmss}{random}";
        string loginId = teacherId; // LoginID는 TeacherID와 동일

        // Teacher 객체 생성
        var teacher = new Teacher
        {
            TeacherID = teacherId,
            LoginID = loginId,
            Name = UserNameTextBox.Text.Trim(),
            Status = "재직",
            Phone = UserPhoneTextBox.Text.Trim(),
            Email = UserEmailTextBox.Text.Trim(),
            Subject = (UserSubjectComboBox.SelectedItem as ComboBoxItem)?.Content.ToString()??string.Empty,
            HireDate = DateTime.Now.ToString("yyyy-MM-dd"),
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };

        // Teacher 저장
        var (success, message) = await teacherService.CreateAsync(teacher);
        if (!success)
        {
            throw new Exception($"교사 정보 저장 실패: {message}");
        }

        // TeacherSchoolHistory 저장
        var history = new TeacherSchoolHistory
        {
            TeacherID = teacherId,
            SchoolCode = _selectedSchool.SchoolCode,
            StartDate = DateTime.Now.ToString("yyyy-MM-dd"),
            IsCurrent = true,
            Position = (UserSubjectComboBox.SelectedItem as ComboBoxItem)?.Content.ToString()??string.Empty,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };

        (success, message) = await teacherService.AddHistoryAsync(history);
        if (!success)
        {
            throw new Exception($"근무 이력 저장 실패: {message}");
        }

        Debug.WriteLine($"[InitialSetupWindow] 사용자 정보 저장 완료: {teacherId}");
        return teacherId;
    }

    /// <summary>
    /// Settings에 저장
    /// </summary>
    private async Task SaveSettingsAsync(string teacherId)
    {
        if (_selectedSchool == null) return;

        // ⭐ UI 요소 값을 미리 가져오기 (UI 스레드에서)
        string userName = UserNameTextBox.Text.Trim();
        int workYear = (int)WorkYearNumberBox.Value;
        int workSemester = WorkSemesterComboBox.SelectedIndex + 1;
        int homeGrade = HomeGradeNumberBox.Value > 0 ? (int)HomeGradeNumberBox.Value : 0;
        int homeRoom = HomeRoomNumberBox.Value > 0 ? (int)HomeRoomNumberBox.Value : 0;

        // ⭐ 이제 Task.Run 안에서 미리 가져온 값 사용
        await Task.Run(() =>
        {
            // 학교 정보
            Settings.SchoolCode.Set(_selectedSchool.SchoolCode);
            Settings.SchoolName.Set(_selectedSchool.SchoolName);
            Settings.ProvinceCode.Set(_selectedSchool.ATPT_OFCDC_SC_CODE ?? "");
            Settings.ProvinceName.Set(_selectedSchool.ATPT_OFCDC_SC_NAME ?? "");
            Settings.SchoolAddress.Set(_selectedSchool.Address ?? "");

            // 사용자 정보
            Settings.User.Set(teacherId);
            Settings.UserName.Set(userName);

            // 학년도/학기
            Settings.WorkYear.Set(workYear);
            Settings.WorkSemester.Set(workSemester);

            // 담임반 정보 (선택사항)
            if (homeGrade > 0)
            {
                Settings.HomeGrade.Set(homeGrade);
            }

            if (homeRoom > 0)
            {
                Settings.HomeRoom.Set(homeRoom);
            }

            Debug.WriteLine("[InitialSetupWindow] Settings 저장 완료");
        });
    }
    #endregion

    #region UI 업데이트

    /// <summary>
    /// 설정 진행 상태 업데이트
    /// </summary>
    private void UpdateSetupStatus()
    {
        // XAML 요소가 아직 로드되지 않았으면 리턴
        if (Step1Status == null || Step2Status == null || Step3Status == null)
            return;

        Step1Status.Text = _isSchoolSelected ? "☑ 학교 선택" : "□ 학교 선택";
        Step2Status.Text = _isUserNameEntered ? "☑ 사용자 정보" : "□ 사용자 정보";
        Step3Status.Text = _isYearSemesterSet ? "☑ 학년도/학기" : "□ 학년도/학기";

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSetupComplete)));
    }

    #endregion
}

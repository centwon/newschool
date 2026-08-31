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
    private bool _isBusy;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// 설정 완료 여부. 저장 중에는 꺼서 [완료]가 두 번 눌리지 않게 한다.
    /// </summary>
    public bool IsSetupComplete => _isSchoolSelected && _isUserNameEntered && _isYearSemesterSet && !_isBusy;

    /// <summary>
    /// 창이 정상적으로 완료되었는지 여부
    /// </summary>
    public bool IsCompleted { get; private set; }

    public InitialSetupWindow()
    {
        this.InitializeComponent();

        // 이 창이 뜰 때 메인 창은 아직 없다 — 등록하지 않으면 학교 검색 실패·설정 저장 실패
        // 안내가 띄울 창을 못 찾아 Debug 출력으로만 사라진다(첫 실행 사용자는 아무것도 못 본다).
        Controls.MessageBox.TrackWindow(this);

        // 저장된 테마로 연다. 창 여덟 중 여기만 빠져 있었다 — 첫 실행에는 테마가 기본값이라
        // 티가 나지 않지만, 다크로 쓰던 사용자가 학교 정보를 잃으면(복원 직후 등) 이 창이
        // 다시 뜨는데 그때 혼자 밝게 뜬다.
        Helpers.ThemeHelper.Apply(this);

        // 현재 학년도로 기본값 설정. 달력의 연도가 아니라 학년도다 — 1·2월은 아직 지난
        // 학년도의 2학기라서, 그냥 DateTime.Now.Year 를 넣으면 "2027학년도 2학기" 처럼
        // 있지도 않은 조합이 기본값으로 잡혔다(아래 학기 기본값은 이미 2학기를 고른다).
        WorkYearNumberBox.Value = DateTimeHelper.SchoolYearOf(DateTime.Now);

        // 현재 월에 따라 학기 설정 (3-8월: 1학기, 9-2월: 2학기) — 규칙은 DateTimeHelper 한 곳에.
        WorkSemesterComboBox.SelectedIndex = DateTimeHelper.SemesterOf(DateTime.Now) - 1;

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

            var result = await MessageBox.ShowDialogAsync(dialog);

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
            SetBusy(true);

            // 1. School 테이블에 저장
            await SaveSchoolAsync();

            // 2. Teacher 테이블에 현재 사용자 저장
            string teacherId = await SaveCurrentUserAsync();

            // 3. Settings에 저장
            await SaveSettingsAsync(teacherId);

            // 4. 학사일정 가져오기(선택) — 실패해도 초기 설정 자체는 끝낸다.
            await ImportSchedulesAsync();

            Debug.WriteLine("[InitialSetupWindow] 초기 설정 완료");

            IsCompleted = true;
            this.Close();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InitialSetupWindow] 초기 설정 저장 실패: {ex.Message}");
            await MessageBox.ShowAsync(ex.Message, "설정 저장 오류");
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>
    /// 저장하는 동안 버튼을 잠근다. 학사일정 내려받기는 네트워크라 몇 초 걸릴 수 있는데,
    /// 그동안 [완료]를 다시 눌리면 교사 행이 한 번 더 만들어진다(TeacherID 를 그때그때
    /// 새로 짓기 때문에 같은 사람이 둘이 된다).
    /// </summary>
    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        CancelButton.IsEnabled = !busy;
        SearchSchoolButton.IsEnabled = !busy;
        ImportScheduleCheckBox.IsEnabled = !busy;

        // CompleteButton 은 IsSetupComplete 에 묶여 있으므로 그 계산에 _isBusy 를 넣고 알린다.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSetupComplete)));
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

        // 교사와 근무 이력은 한 트랜잭션으로 함께 만든다. 예전에는 둘을 따로 저장해서,
        // 이력 저장이 실패하면 근무 이력 없는 교사 행만 DB 에 남았다(재시도하면 TeacherID 를
        // 새로 만들므로 그 고아 행은 영영 지워지지 않는다).
        var (success, message, _) = await teacherService.RegisterTeacherAsync(teacher, history);
        if (!success)
        {
            throw new Exception($"교사 정보 저장 실패: {message}");
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

    /// <summary>
    /// 선택한 학교의 학사일정을 NEIS 에서 받아 DB 에 넣는다(4단계, 선택).
    ///
    /// <para>첫 실행 뒤 곧바로 달력·홈에 학사일정이 보이게 하려는 것이다. 예전에는 이 자리가
    /// 없어서, 달력이나 홈이 열릴 때 슬그머니 내려받았는데 그건 저장을 하지 않아 한 번 보이고
    /// 사라졌다(<see cref="SchoolScheduleService.SyncSchoolYearFromNeisAsync"/> 주석 참고).</para>
    ///
    /// <para>여기서 실패하더라도 초기 설정은 완료시킨다 — 인증키가 없거나 학교가 NEIS 에
    /// 일정을 올리지 않았을 뿐인데 프로그램을 아예 못 쓰게 만들 이유가 없다. 나중에
    /// [설정 → 학사일정 관리]에서 다시 시도할 수 있다.</para>
    /// </summary>
    private async Task ImportSchedulesAsync()
    {
        if (_selectedSchool == null) return;
        if (ImportScheduleCheckBox.IsChecked != true) return;

        int year = (int)WorkYearNumberBox.Value;

        try
        {
            ScheduleProgressText.Text = $"{year}학년도 학사일정을 가져오는 중...";
            ScheduleProgressPanel.Visibility = Visibility.Visible;

            using var service = new SchoolScheduleService(SchoolDatabase.DbPath);
            var sync = await service.SyncSchoolYearFromNeisAsync(
                _selectedSchool.SchoolCode,
                _selectedSchool.ATPT_OFCDC_SC_CODE ?? string.Empty,
                year);

            if (!sync.Success)
            {
                // 창이 곧 닫히므로 InfoBar 는 사실상 안 보인다 — 이유는 대화 상자로 말한다.
                Debug.WriteLine($"[InitialSetupWindow] 학사일정 가져오기 실패: {sync.Message}");
                await MessageBox.ShowAsync(
                    $"학사일정을 가져오지 못했습니다.\n{sync.Message}\n\n"
                    + "나머지 설정은 그대로 저장됩니다. [설정 → 학사일정 관리]에서 다시 시도할 수 있습니다.",
                    "학사일정 가져오기");
                return;
            }

            Debug.WriteLine($"[InitialSetupWindow] 학사일정 {sync.Saved}건 저장 (내려받음 {sync.Downloaded}건)");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InitialSetupWindow] 학사일정 가져오기 오류: {ex.Message}");
            await MessageBox.ShowAsync(
                $"학사일정을 가져오는 중 오류가 발생했습니다.\n{ex.Message}\n\n"
                + "나머지 설정은 그대로 저장됩니다. [설정 → 학사일정 관리]에서 다시 시도할 수 있습니다.",
                "학사일정 가져오기");
        }
        finally
        {
            ScheduleProgressPanel.Visibility = Visibility.Collapsed;
        }
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

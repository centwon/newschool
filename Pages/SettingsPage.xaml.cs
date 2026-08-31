using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Controls;
using NewSchool.Dialogs;
using NewSchool.Models;
using NewSchool.Services;

namespace NewSchool.Pages;

/// <summary>캘린더 체크리스트 항목 (CalendarSettingsDialog UI용)</summary>
public sealed partial class GoogleCalendarCheckItem
{
    public string Title { get; set; } = string.Empty;
    public string GoogleId { get; set; } = string.Empty;
    public bool IsChecked { get; set; }
    public int CalendarNo { get; set; }
}

public sealed partial class SettingsPage : Page
{
    private bool _isInitialized = false;
    public SettingsPage()
    {
        this.InitializeComponent();
        this.Loaded += SettingsPage_Loaded;
    }

    private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            SchoolNameTextBox.Text = Settings.SchoolName.Value;
            ProvinceCodeTextBox.Text = Settings.ProvinceCode.Value;
            SchoolCodeTextBox.Text = Settings.SchoolCode.Value;
            ProvinceNameTextBox.Text = Settings.ProvinceName.Value;
            SchoolAddressTextBox.Text = Settings.SchoolAddress.Value;

            UserNameTextBox.Text = Settings.UserName.Value;
            HomeGradeNumberBox.Value = Settings.HomeGrade.Value;
            HomeRoomNumberBox.Value = Settings.HomeRoom.Value;

            WorkYearNumberBox.Value = Settings.WorkYear.Value;
            WorkSemesterComboBox.SelectedIndex = Settings.WorkSemester.Value - 1;

            DayStartingTimePicker.Time = Settings.DayStarting.Value;
            AssemblyTimePicker.Time = Settings.AssemblyTime.Value;
            OnePeriodNumberBox.Value = Settings.OnePeriod.Value.TotalMinutes;
            BreakTimeNumberBox.Value = Settings.BreakTime.Value.TotalMinutes;
            LunchTimeNumberBox.Value = Settings.LunchTime.Value.TotalMinutes;

            // 입력 상한은 XAML 이 아니라 여기서 건다 — 상한이 표시 쪽과 갈리면
            // "설정에서는 올라가는데 시간표에는 안 나오는" 교시가 생긴다(PeriodCounts.MaxSupported 주석).
            foreach (var box in new[] { PeriodsMonBox, PeriodsTueBox, PeriodsWedBox, PeriodsThuBox, PeriodsFriBox })
                box.Maximum = Models.PeriodCounts.MaxSupported;

            var periods = Models.PeriodCounts.Parse(Settings.PeriodsPerDay.Value);
            PeriodsMonBox.Value = periods.Mon;
            PeriodsTueBox.Value = periods.Tue;
            PeriodsWedBox.Value = periods.Wed;
            PeriodsThuBox.Value = periods.Thu;
            PeriodsFriBox.Value = periods.Fri;

            // 학생부 글자 제한 입력칸 생성 (NeisHelper.Areas 정의표 기반)
            // _isInitialized 이전에 만들어, 초기값 대입이 저장으로 흘러가지 않게 한다
            BuildSpecByteEditors();

            _isInitialized = true;

            // NEIS 추가 정보(학교종류·개교기념일·전화·팩스·홈페이지)는 Settings 에 없고 School DB 에만 있으므로
            // 저장된 학교 코드로 조회해 표시 전용 필드를 채운다(비동기 — UI 는 이미 로드됨).
            _ = LoadSchoolExtraInfoAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SettingsPage] 설정 로드 오류: {ex.Message}");
        }
    }

    #region 사용자 / 담임반

    private void OnUserNameChanged(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;
        Settings.UserName.Set(UserNameTextBox.Text);
    }

    private void OnHomeGradeChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_isInitialized) return;
        if (!double.IsNaN(args.NewValue))
            Settings.HomeGrade.Set((int)args.NewValue);
    }

    private void OnHomeRoomChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_isInitialized) return;
        if (!double.IsNaN(args.NewValue))
            Settings.HomeRoom.Set((int)args.NewValue);
    }

    #endregion

    #region 학교 정보 이벤트 핸들러

    private async void OnSearchSchoolClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new SchoolSearchDialog
            {
                XamlRoot = this.XamlRoot
            };

            var result = await MessageBox.ShowDialogAsync(dialog);

            if (result == ContentDialogResult.Primary && dialog.SelectedSchool != null)
            {
                var school = dialog.SelectedSchool;

                await SaveSchoolToDatabase(school);

                Settings.SchoolName.Set(school.SchoolName);
                Settings.SchoolCode.Set(school.SchoolCode);
                Settings.ProvinceCode.Set(school.ATPT_OFCDC_SC_CODE);
                Settings.ProvinceName.Set(school.ATPT_OFCDC_SC_NAME);
                Settings.SchoolAddress.Set(school.Address);

                SchoolNameTextBox.Text = school.SchoolName;
                SchoolCodeTextBox.Text = school.SchoolCode;
                ProvinceCodeTextBox.Text = school.ATPT_OFCDC_SC_CODE;
                ProvinceNameTextBox.Text = school.ATPT_OFCDC_SC_NAME;
                SchoolAddressTextBox.Text = school.Address;
                ShowSchoolExtraInfo(school);

                SchoolSearchInfoBar.Title = "학교 정보가 저장되었습니다";
                SchoolSearchInfoBar.Message = $"{school.SchoolName}의 정보가 Settings와 데이터베이스에 저장되었습니다.";
                SchoolSearchInfoBar.Severity = InfoBarSeverity.Success;
                SchoolSearchInfoBar.IsOpen = true;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SettingsPage] 학교 검색 오류: {ex.Message}");
            await MessageBox.ShowAsync(ex.Message, "학교 검색 오류");
        }
    }

    private async Task SaveSchoolToDatabase(School school)
    {
        try
        {
            using var schoolService = new SchoolService(SchoolDatabase.DbPath);
            await schoolService.SaveSchoolAsync(school);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SettingsPage] School 테이블 저장 실패: {ex.Message}");
            throw;
        }
    }

    /// <summary>School DB 에서 NEIS 추가 정보를 읽어 표시 전용 필드를 채운다. 저장된 학교가 없으면 비운다.</summary>
    private async Task LoadSchoolExtraInfoAsync()
    {
        try
        {
            string code = Settings.SchoolCode.Value;
            if (string.IsNullOrEmpty(code)) return;

            using var schoolService = new SchoolService(SchoolDatabase.DbPath);
            var school = await schoolService.GetSchoolByCodeAsync(code);
            if (school == null) return;

            ShowSchoolExtraInfo(school);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SettingsPage] 학교 추가 정보 로드 실패: {ex.Message}");
        }
    }

    /// <summary>NEIS 추가 정보를 표시 전용 필드에 반영(개교기념일은 8자리 YYYYMMDD → YYYY-MM-DD 로 정리).</summary>
    private void ShowSchoolExtraInfo(School school)
    {
        SchoolTypeTextBox.Text = school.SchoolType;
        FoundationDateTextBox.Text = FormatFoundationDate(school.FoundationDate);
        SchoolPhoneTextBox.Text = school.Phone;
        SchoolFaxTextBox.Text = school.Fax;
        SchoolWebsiteTextBox.Text = school.Website;
    }

    private static string FormatFoundationDate(string raw)
    {
        if (raw.Length == 8 &&
            DateTime.TryParseExact(raw, "yyyyMMdd", null,
                System.Globalization.DateTimeStyles.None, out var d))
        {
            return d.ToString("yyyy-MM-dd");
        }
        return raw;
    }

    private void OnSchoolNameChanged(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;
        if (SchoolNameTextBox != null)
            Settings.SchoolName.Set(SchoolNameTextBox.Text);
    }

    private void OnProvinceCodeChanged(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;
        if (ProvinceCodeTextBox != null)
            Settings.ProvinceCode.Set(ProvinceCodeTextBox.Text);
    }

    private void OnSchoolCodeChanged(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;
        if (SchoolCodeTextBox != null)
            Settings.SchoolCode.Set(SchoolCodeTextBox.Text);
    }

    private void OnProvinceNameChanged(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;
        if (ProvinceNameTextBox != null)
            Settings.ProvinceName.Set(ProvinceNameTextBox.Text);
    }

    private void OnSchoolAddressChanged(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;
        if (SchoolAddressTextBox != null)
            Settings.SchoolAddress.Set(SchoolAddressTextBox.Text);
    }

    #endregion

    #region 학년도/학기 이벤트 핸들러

    private void OnWorkYearChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_isInitialized) return;
        if (!double.IsNaN(args.NewValue))
            Settings.WorkYear.Set((int)args.NewValue);
    }

    private void OnWorkSemesterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized) return;
        if (WorkSemesterComboBox.SelectedItem is ComboBoxItem item)
        {
            // XAML 의 Tag="1" 은 **문자열**이다. 예전에는 (int)item.Tag 로 언박싱해서
            // 학기를 고를 때마다 InvalidCastException 이 났고, 그 줄에서 멈추는 바람에
            // 아래 저장이 아예 실행되지 않았다 — 학기가 영영 저장되지 않았다.
            if (int.TryParse(item.Tag?.ToString(), out int semester))
                Settings.WorkSemester.Set(semester);
        }
    }

    #endregion

    #region 시간표 이벤트 핸들러

    private void OnDayStartingChanged(object sender, TimeSpan e)
    {
        if (!_isInitialized) return;
        Settings.DayStarting.Set(e);
    }

    private void OnAssemblyTimeChanged(object sender, TimeSpan e)
    {
        if (!_isInitialized) return;
        Settings.AssemblyTime.Set(e);
    }

    private void OnOnePeriodChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_isInitialized) return;
        if (!double.IsNaN(args.NewValue))
            Settings.OnePeriod.Set(TimeSpan.FromMinutes(args.NewValue));
    }

    private void OnBreakTimeChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_isInitialized) return;
        if (!double.IsNaN(args.NewValue))
            Settings.BreakTime.Set(TimeSpan.FromMinutes(args.NewValue));
    }

    /// <summary>요일별 교시 수 5칸 공용 핸들러 — 어느 칸이 바뀌든 전체를 직렬화해 저장</summary>
    private void OnPeriodsPerDayChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_isInitialized) return;

        // 어느 한 칸이라도 비어 있으면(NaN) 저장 보류 — 입력이 완성되면 그 칸의 이벤트로 다시 저장됨
        double[] values = { PeriodsMonBox.Value, PeriodsTueBox.Value, PeriodsWedBox.Value, PeriodsThuBox.Value, PeriodsFriBox.Value };
        foreach (var v in values)
            if (double.IsNaN(v)) return;

        var periods = new Models.PeriodCounts((int)values[0], (int)values[1], (int)values[2], (int)values[3], (int)values[4]);
        Settings.PeriodsPerDay.Set(periods.Serialize());
    }

    private void OnLunchTimeChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_isInitialized) return;
        if (!double.IsNaN(args.NewValue))
            Settings.LunchTime.Set(TimeSpan.FromMinutes(args.NewValue));
    }

    #endregion

    #region 학생부 글자 제한

    /// <summary>0 = 모든 학년도(기본), 그 외 = 해당 학년도에만 적용</summary>
    private int SelectedSpecByteYear =>
        CBoxSpecByteYear?.SelectedItem is ComboBoxItem { Tag: string t } && int.TryParse(t, out int y) ? y : 0;

    /// <summary>
    /// 학생부 한도 입력칸을 <see cref="Helpers.NeisHelper.Areas"/> 정의표에서 생성한다.
    /// 영역을 추가·삭제해도 이 화면은 자동으로 따라간다(예전에는 XAML 에 6개가 고정돼 있어
    /// 정의표와 어긋날 수 있었고, 실제로 "봉사활동"이 빠져 있었다).
    /// </summary>
    private void BuildSpecByteEditors()
    {
        // 학년도 선택 목록은 최초 1회만 구성 (올해 기준 ±2년 + '모든 학년도')
        if (CBoxSpecByteYear.Items.Count == 0)
        {
            CBoxSpecByteYear.Items.Add(new ComboBoxItem { Content = "모든 학년도(기본)", Tag = "0" });
            int thisYear = Settings.WorkYear.Value > 0 ? Settings.WorkYear.Value : DateTime.Now.Year;
            for (int y = thisYear + 1; y >= thisYear - 2; y--)
                CBoxSpecByteYear.Items.Add(new ComboBoxItem { Content = $"{y}학년도", Tag = y.ToString() });
            CBoxSpecByteYear.SelectedIndex = 0;
        }

        int year = SelectedSpecByteYear;
        TxtSpecByteScope.Text = year > 0
            ? $"{year}학년도에만 적용됩니다. 비워 둔 영역은 '모든 학년도' 값을 따릅니다."
            : "모든 학년도에 적용됩니다. 특정 학년도만 다르게 하려면 위에서 학년도를 고르세요.";

        // 재생성 시 이전 컨트롤 참조가 남지 않도록 매핑도 함께 비운다
        PanelSpecBytes.Children.Clear();
        _specCharToHint.Clear();

        foreach (var area in Helpers.NeisHelper.Areas)
        {
            int bytes = Settings.GetSpecMaxBytes(area.Key, year);

            // 지침은 "500자"처럼 글자 수로 나오므로 입력은 글자 수 하나만 받고,
            // 실제 판정 단위인 바이트는 옆에 읽기 전용으로 보여준다(칸을 두 개 두면 중복).
            var charBox = new NumberBox
            {
                Header = area.Label,
                Tag = area.Key,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
                Minimum = 0,
                Maximum = 3000,
                SmallChange = 50,
                Width = 200,
                Value = bytes / BytesPerKoreanChar
            };

            var hint = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 6),
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                Text = FormatSpecByteHint(bytes)
            };

            _specCharToHint[charBox] = hint;
            charBox.ValueChanged += OnSpecCharCountChanged;

            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            row.Children.Add(charBox);
            row.Children.Add(hint);
            PanelSpecBytes.Children.Add(row);
        }
    }

    /// <summary>글자 수 옆에 붙는 바이트 안내 문구.</summary>
    private static string FormatSpecByteHint(int bytes) => $"= {bytes:N0} Byte (실제 판정 단위)";

    /// <summary>NEIS 바이트 계산에서 한글 1자 = 3바이트.</summary>
    private const int BytesPerKoreanChar = 3;

    /// <summary>글자 수 입력칸 → 옆에 붙은 바이트 안내 문구</summary>
    private readonly Dictionary<NumberBox, TextBlock> _specCharToHint = new();

    private void OnSpecCharCountChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_isInitialized) return;
        if (double.IsNaN(args.NewValue)) return;
        if (sender.Tag is not string type) return;

        int bytes = (int)args.NewValue * BytesPerKoreanChar;

        if (_specCharToHint.TryGetValue(sender, out var hint))
            hint.Text = FormatSpecByteHint(bytes);

        Settings.SetSpecByteOverride(type, bytes, SelectedSpecByteYear);
    }

    private void OnSpecByteYearChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized) return;
        BuildSpecByteEditors();   // 선택 학년도 기준으로 현재값 다시 표시
    }

    #endregion

}

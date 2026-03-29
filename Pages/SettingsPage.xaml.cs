using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Controls;
using NewSchool.Dialogs;
using NewSchool.Models;
using NewSchool.Repositories;

namespace NewSchool.Pages;

/// <summary>캘린더 체크리스트 항목 (CalendarSettingsDialog UI용)</summary>
[WinRT.GeneratedBindableCustomProperty]
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

            _isInitialized = true;
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

            var result = await dialog.ShowAsync();

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
            string dbPath = SchoolDatabase.DbPath;
            using var schoolRepo = new SchoolRepository(dbPath);

            var existingSchool = await schoolRepo.GetBySchoolCodeAsync(school.SchoolCode);

            if (existingSchool != null)
            {
                existingSchool.SchoolName = school.SchoolName;
                existingSchool.ATPT_OFCDC_SC_CODE = school.ATPT_OFCDC_SC_CODE;
                existingSchool.ATPT_OFCDC_SC_NAME = school.ATPT_OFCDC_SC_NAME;
                existingSchool.SchoolType = school.SchoolType;
                existingSchool.Address = school.Address;
                existingSchool.Phone = school.Phone;
                existingSchool.Fax = school.Fax;
                existingSchool.Website = school.Website;
                existingSchool.FoundationDate = school.FoundationDate;
                existingSchool.IsActive = true;
                existingSchool.UpdatedAt = DateTime.Now;

                await schoolRepo.UpdateAsync(existingSchool);
            }
            else
            {
                school.CreatedAt = DateTime.Now;
                school.UpdatedAt = DateTime.Now;
                school.IsActive = true;
                school.IsDeleted = false;

                await schoolRepo.CreateAsync(school);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SettingsPage] School 테이블 저장 실패: {ex.Message}");
            throw;
        }
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
            int semester = (int)item.Tag;
            Settings.WorkSemester.Set(semester);
        }
    }

    #endregion

    #region 시간표 이벤트 핸들러

    private void OnDayStartingChanged(object sender, TimePickerValueChangedEventArgs e)
    {
        if (!_isInitialized) return;
        Settings.DayStarting.Set(e.NewTime);
    }

    private void OnAssemblyTimeChanged(object sender, TimePickerValueChangedEventArgs e)
    {
        if (!_isInitialized) return;
        Settings.AssemblyTime.Set(e.NewTime);
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

    private void OnLunchTimeChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_isInitialized) return;
        if (!double.IsNaN(args.NewValue))
            Settings.LunchTime.Set(TimeSpan.FromMinutes(args.NewValue));
    }

    #endregion
}

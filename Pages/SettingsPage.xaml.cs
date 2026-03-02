using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Controls;
using NewSchool.Dialogs;
using NewSchool.Google;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.Services;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace NewSchool.Pages;

/// <summary>캘린더 체크리스트 항목 (Settings UI용)</summary>
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

    private async void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadSettings();
    }

    /// <summary>
    /// 모든 설정값을 UI에 로드
    /// </summary>
    private async Task LoadSettings()
    {
        try
        {
            // 학교 정보
            SchoolNameTextBox.Text = Settings.SchoolName.Value;
            ProvinceCodeTextBox.Text = Settings.ProvinceCode.Value;
            SchoolCodeTextBox.Text = Settings.SchoolCode.Value;
            ProvinceNameTextBox.Text = Settings.ProvinceName.Value;
            SchoolAddressTextBox.Text = Settings.SchoolAddress.Value;
            //NeisApiKeyTextBox.Text = Settings.NeisApiKey.Value;

            // 학년도/학기
            WorkYearNumberBox.Value = Settings.WorkYear.Value;
            WorkSemesterComboBox.SelectedIndex = Settings.WorkSemester.Value - 1;

            // 담임반
            HomeGradeNumberBox.Value = Settings.HomeGrade.Value;
            HomeRoomNumberBox.Value = Settings.HomeRoom.Value;
            UserNameTextBox.Text = Settings.UserName.Value;

            // 시간표
            DayStartingTimePicker.Time = Settings.DayStarting.Value;
            AssemblyTimePicker.Time = Settings.AssemblyTime.Value;
            OnePeriodNumberBox.Value = Settings.OnePeriod.Value.TotalMinutes;
            BreakTimeNumberBox.Value = Settings.BreakTime.Value.TotalMinutes;
            LunchTimeNumberBox.Value = Settings.LunchTime.Value.TotalMinutes;

            // 일정 관리
            ShowEventsToggle.IsOn = Settings.ShowEvents.Value;
            ShowTasksToggle.IsOn = Settings.ShowTasks.Value;
            EventFontSizeNumberBox.Value = Settings.EventFontSize.Value;
            TaskFontSizeNumberBox.Value = Settings.TaskFontSize.Value;
            UseGoogleToggle.IsOn = Settings.UseGoogle.Value;
            GoogleAutoSyncToggle.IsOn = Settings.GoogleAutoSync.Value;
            GoogleSyncIntervalNumberBox.Value = Settings.GoogleSyncIntervalMinutes.Value;
            UpdateGoogleAuthStatus();
            await LoadGoogleCalendarListAsync();

            // 일반 설정
            TopMostToggle.IsOn = Settings.TopMost.Value;
            ThemeComboBox.SelectedIndex = GetThemeIndex(Settings.Theme.Value);
            LanguageComboBox.SelectedIndex = Settings.Language.Value == "ko-KR" ? 0 : 1;

            // 성능/캐시
            EnableCacheToggle.IsOn = Settings.EnableCache.Value;
            DefaultPageSizeNumberBox.Value = Settings.DefaultPageSize.Value;

            // 백업
            AutoBackupToggle.IsOn = Settings.AutoBackup.Value;
            AutoBackupIntervalDaysNumberBox.Value = Settings.AutoBackupIntervalDays.Value;
            BackupRetentionCountNumberBox.Value = Settings.BackupRetentionCount.Value;

            // 고급
            LogLevelComboBox.SelectedIndex = GetLogLevelIndex(Settings.LogLevel.Value);
            _isInitialized = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SettingsPage] 설정 로드 오류: {ex.Message}");
            await MessageBox.ShowAsync(ex.Message, "설정 로드 오류");
        }
    }

    private int GetThemeIndex(string theme)
    {
        return theme switch
        {
            "Light" => 0,
            "Dark" => 1,
            "Default" => 2,
            _ => 0
        };
    }

    private int GetLogLevelIndex(string logLevel)
    {
        return logLevel switch
        {
            "Debug" => 0,
            "Info" => 1,
            "Warning" => 2,
            "Error" => 3,
            _ => 1
        };
    }

    #region 학교 정보 이벤트 핸들러

    // SettingsPage.xaml.cs의 OnSearchSchoolClick 메서드 수정

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

                // ⭐ 1. School 테이블에 저장 (추가)
                await SaveSchoolToDatabase(school);

                // 2. Settings에 저장 (기존)
                Settings.SchoolName.Set(school.SchoolName);
                Settings.SchoolCode.Set(school.SchoolCode);
                Settings.ProvinceCode.Set(school.ATPT_OFCDC_SC_CODE);
                Settings.ProvinceName.Set(school.ATPT_OFCDC_SC_NAME);
                Settings.SchoolAddress.Set(school.Address);

                // 3. UI 업데이트 (기존)
                SchoolNameTextBox.Text = school.SchoolName;
                SchoolCodeTextBox.Text = school.SchoolCode;
                ProvinceCodeTextBox.Text = school.ATPT_OFCDC_SC_CODE;
                ProvinceNameTextBox.Text = school.ATPT_OFCDC_SC_NAME;
                SchoolAddressTextBox.Text = school.Address;

                // 4. 성공 메시지
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

    /// <summary>
    /// ⭐ School 테이블에 학교 정보 저장 (신규 추가)
    /// 기존 학교가 있으면 업데이트, 없으면 새로 추가
    /// </summary>
    private async Task SaveSchoolToDatabase(School school)
    {
        try
        {
            string dbPath = SchoolDatabase.DbPath;
            using var schoolRepo = new SchoolRepository(dbPath);

            // 1. 기존 학교 확인
            var existingSchool = await schoolRepo.GetBySchoolCodeAsync(school.SchoolCode);

            if (existingSchool != null)
            {
                // 2-1. 기존 학교 업데이트
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
                Debug.WriteLine($"[SettingsPage] School 테이블 업데이트: {school.SchoolCode}");
            }
            else
            {
                // 2-2. 새 학교 추가
                school.CreatedAt = DateTime.Now;
                school.UpdatedAt = DateTime.Now;
                school.IsActive = true;
                school.IsDeleted = false;

                await schoolRepo.CreateAsync(school);
                Debug.WriteLine($"[SettingsPage] School 테이블에 새 학교 추가: {school.SchoolCode}");
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
        {
            Settings.SchoolName.Set(SchoolNameTextBox.Text);
        }
    }

    private void OnProvinceCodeChanged(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;

        if (ProvinceCodeTextBox != null)
        {
            Settings.ProvinceCode.Set(ProvinceCodeTextBox.Text);
        }
    }

    private void OnSchoolCodeChanged(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;

        if (SchoolCodeTextBox != null)
        {
            Settings.SchoolCode.Set(SchoolCodeTextBox.Text);
        }
    }

    private void OnProvinceNameChanged(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;

        if (ProvinceNameTextBox != null)
        {
            Settings.ProvinceName.Set(ProvinceNameTextBox.Text);
        }
    }

    private void OnSchoolAddressChanged(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;

        if (SchoolAddressTextBox != null)
        {
            Settings.SchoolAddress.Set(SchoolAddressTextBox.Text);
        }
    }

    //private void OnNeisApiKeyChanged(object sender, RoutedEventArgs e)
    //{
    //    if (NeisApiKeyTextBox != null)
    //    {
    //        Settings.NeisApiKey.Set(NeisApiKeyTextBox.Text);
    //    }
    //}

    #endregion

    #region 학년도/학기 이벤트 핸들러

    private void OnWorkYearChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_isInitialized) return;

        if (!double.IsNaN(args.NewValue))
        {
            Settings.WorkYear.Set((int)args.NewValue);
        }
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

    #region 담임반 이벤트 핸들러

    private void OnHomeGradeChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_isInitialized) return;

        if (!double.IsNaN(args.NewValue))
        {
            Settings.HomeGrade.Set((int)args.NewValue);
        }
    }

    private void OnHomeRoomChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_isInitialized) return;

        if (!double.IsNaN(args.NewValue))
        {
            Settings.HomeRoom.Set((int)args.NewValue);
        }
    }

    private void OnUserNameChanged(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;

        if (UserNameTextBox != null)
        {
            Settings.UserName.Set(UserNameTextBox.Text);
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
        {
            Settings.OnePeriod.Set(TimeSpan.FromMinutes(args.NewValue));
        }
    }

    private void OnBreakTimeChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_isInitialized) return;

        if (!double.IsNaN(args.NewValue))
        {
            Settings.BreakTime.Set(TimeSpan.FromMinutes(args.NewValue));
        }
    }

    private void OnLunchTimeChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_isInitialized) return;

        if (!double.IsNaN(args.NewValue))
        {
            Settings.LunchTime.Set(TimeSpan.FromMinutes(args.NewValue));
        }
    }

    #endregion

    #region 일정 관리 이벤트 핸들러

    private void OnShowEventsToggled(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;

        Settings.ShowEvents.Set(ShowEventsToggle.IsOn);
    }

    private void OnShowTasksToggled(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;

        Settings.ShowTasks.Set(ShowTasksToggle.IsOn);
    }

    private void OnEventFontSizeChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_isInitialized) return;

        if (!double.IsNaN(args.NewValue))
        {
            Settings.EventFontSize.Set(args.NewValue);
        }
    }

    private void OnTaskFontSizeChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_isInitialized) return;

        if (!double.IsNaN(args.NewValue))
        {
            Settings.TaskFontSize.Set(args.NewValue);
        }
    }

    private void OnUseGoogleToggled(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;

        Settings.UseGoogle.Set(UseGoogleToggle.IsOn);
    }

    private async void OnGoogleAuthClicked(object sender, RoutedEventArgs e)
    {
        if (!GoogleAuthService.HasCredentials)
        {
            GoogleAuthStatusText.Text = "Google 연동 기능이 아직 설정되지 않았습니다.";
            return;
        }

        GoogleAuthButton.IsEnabled = false;
        GoogleAuthStatusText.Text = "브라우저에서 Google 로그인 중...";

        try
        {
            using var authService = new GoogleAuthService();
            bool success = await authService.AuthenticateAsync();

            if (success)
            {
                GoogleAuthStatusText.Text = "✅ Google 계정 연동 완료 — 캘린더 목록 가져오는 중...";
                Debug.WriteLine("[SettingsPage] Google 인증 성공");
                await FetchAndSaveGoogleCalendarsAsync(authService);
            }
            else
            {
                GoogleAuthStatusText.Text = "❌ 인증에 실패했습니다.";
                Debug.WriteLine("[SettingsPage] Google 인증 실패");
            }
        }
        catch (Exception ex)
        {
            GoogleAuthStatusText.Text = $"❌ 오류: {ex.Message}";
            Debug.WriteLine($"[SettingsPage] Google 인증 오류: {ex.Message}");
        }
        finally
        {
            GoogleAuthButton.IsEnabled = true;
            UpdateGoogleAuthStatus();
        }
    }

    private async void OnGoogleSignOutClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            using var authService = new GoogleAuthService();
            await authService.SignOutAsync();
            GoogleAuthStatusText.Text = "연동이 해제되었습니다.";
            Debug.WriteLine("[SettingsPage] Google 연동 해제");
        }
        catch (Exception ex)
        {
            GoogleAuthStatusText.Text = $"오류: {ex.Message}";
            Debug.WriteLine($"[SettingsPage] Google 연동 해제 오류: {ex.Message}");
        }

        UpdateGoogleAuthStatus();
    }

    private void OnGoogleAutoSyncToggled(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;
        Settings.GoogleAutoSync.Set(GoogleAutoSyncToggle.IsOn);
    }

    private void OnGoogleSyncIntervalChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_isInitialized) return;
        if (!double.IsNaN(args.NewValue))
        {
            Settings.GoogleSyncIntervalMinutes.Set((int)args.NewValue);
        }
    }

    private async void OnGoogleSyncNowClicked(object sender, RoutedEventArgs e)
    {
        GoogleSyncNowButton.IsEnabled = false;
        GoogleSyncProgressRing.IsActive = true;
        GoogleSyncProgressRing.Visibility = Visibility.Visible;
        GoogleSyncStatusText.Text = "동기화 중...";

        try
        {
            using var authService = new GoogleAuthService();
            var apiClient = new GoogleCalendarApiClient(authService);
            using var syncService = new GoogleSyncService(authService, apiClient);

            var result = await syncService.SyncAllAsync();

            if (result.Success)
            {
                GoogleSyncStatusText.Text = $"✅ 동기화 완료 — {result.Summary}";
            }
            else
            {
                GoogleSyncStatusText.Text = $"⚠️ 동기화 완료 (일부 오류) — {result.Summary}";
                if (result.ErrorMessages.Count > 0)
                {
                    GoogleSyncStatusText.Text += $"\n{string.Join("\n", result.ErrorMessages)}";
                }
            }

            Debug.WriteLine($"[SettingsPage] 동기화 결과: {result.Summary}");
        }
        catch (Exception ex)
        {
            GoogleSyncStatusText.Text = $"❌ 동기화 실패: {ex.Message}";
            Debug.WriteLine($"[SettingsPage] 동기화 오류: {ex.Message}");
        }
        finally
        {
            GoogleSyncNowButton.IsEnabled = true;
            GoogleSyncProgressRing.IsActive = false;
            GoogleSyncProgressRing.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateGoogleAuthStatus()
    {
        if (!GoogleAuthService.HasCredentials)
        {
            GoogleAuthStatusText.Text = "Google 연동 기능이 비활성화 상태입니다.";
            GoogleAuthButton.IsEnabled = false;
            GoogleSignOutButton.IsEnabled = false;
            GoogleSyncNowButton.IsEnabled = false;
            return;
        }

        using var authService = new GoogleAuthService();
        if (authService.IsAuthenticated)
        {
            GoogleAuthStatusText.Text = "✅ Google 계정 연동됨";
            GoogleAuthButton.Content = "다시 연동";
            GoogleSignOutButton.IsEnabled = true;
            GoogleSyncNowButton.IsEnabled = UseGoogleToggle.IsOn;
        }
        else
        {
            GoogleAuthStatusText.Text = "연동되지 않음";
            GoogleAuthButton.Content = "Google 계정 연동";
            GoogleAuthButton.IsEnabled = true;
            GoogleSignOutButton.IsEnabled = false;
            GoogleSyncNowButton.IsEnabled = false;
        }
    }

    /// <summary>
    /// Google 인증 후 캘린더 자동 매핑:
    /// - 학교 캘린더(수업/담임/업무) → Google에 "학교이름" 캘린더 생성 또는 기존 매칭
    /// - 개인 캘린더 → Google primary(기본) 캘린더에 매핑
    /// </summary>
    private async Task FetchAndSaveGoogleCalendarsAsync(GoogleAuthService authService)
    {
        try
        {
            var apiClient = new GoogleCalendarApiClient(authService);
            var googleCalendars = await apiClient.GetCalendarListAsync();
            using var service = Scheduler.Scheduler.CreateService();

            string schoolName = Settings.SchoolName.Value;
            if (string.IsNullOrWhiteSpace(schoolName)) schoolName = "학교";

            // 1) Google primary 캘린더 찾기 (개인용)
            var primaryCal = googleCalendars.Find(c => c.Primary == true);

            // 2) Google에서 학교 이름과 일치하는 캘린더 찾기
            var schoolGoogleCal = googleCalendars.Find(c =>
                string.Equals(c.Summary, schoolName, StringComparison.OrdinalIgnoreCase));

            // 없으면 Google에 새 캘린더 생성
            if (schoolGoogleCal == null)
            {
                GoogleAuthStatusText.Text = $"Google에 '{schoolName}' 캘린더 생성 중...";
                var created = await apiClient.InsertCalendarAsync(schoolName, "학사 일정 · 수업 · 담임 · 업무");
                if (created != null)
                {
                    schoolGoogleCal = new GoogleCalendarListEntry
                    {
                        Id = created.Id,
                        Summary = created.Summary,
                        BackgroundColor = "#4285F4"
                    };
                    Debug.WriteLine($"[SettingsPage] Google 캘린더 생성됨: {created.Id} ({created.Summary})");
                }
            }

            // 3) 로컬 캘린더에 GoogleId 매핑
            var allLocal = await service.GetAllCalendarsAsync();
            int mapped = 0;

            foreach (var local in allLocal)
            {
                string? targetGoogleId = null;

                if (local.Title == "개인" && primaryCal != null)
                {
                    // 개인 → Google primary 캘린더
                    targetGoogleId = primaryCal.Id;
                }
                else if (local.Title is "수업" or "담임" or "업무" && schoolGoogleCal != null)
                {
                    // 학교 관련 → Google 학교 캘린더
                    targetGoogleId = schoolGoogleCal.Id;
                }

                if (targetGoogleId != null && local.GoogleId != targetGoogleId)
                {
                    local.GoogleId = targetGoogleId;
                    if (local.SyncMode == "None")
                        local.SyncMode = "TwoWay";
                    await service.UpdateCalendarAsync(local);
                    mapped++;
                    Debug.WriteLine($"[SettingsPage] '{local.Title}' → GoogleId: {targetGoogleId}");
                }
            }

            string summary = $"학교({schoolName}) 캘린더 + 개인 캘린더 매핑 완료 ({mapped}개)";
            Debug.WriteLine($"[SettingsPage] {summary}");
            GoogleAuthStatusText.Text = $"✅ {summary}";
            await LoadGoogleCalendarListAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SettingsPage] 캘린더 매핑 실패: {ex.Message}");
            GoogleAuthStatusText.Text = "✅ 연동됨 (캘린더 매핑 실패)";
        }
    }

    /// <summary>
    /// 로컬 DB에서 Google 연동 캘린더 목록을 불러와 UI에 표시
    /// </summary>
    private async Task LoadGoogleCalendarListAsync()
    {
        try
        {
            using var service = Scheduler.Scheduler.CreateService();
            var allCalendars = await service.GetAllCalendarsAsync();
            var googleCalendars = allCalendars
                .Where(c => !string.IsNullOrEmpty(c.GoogleId))
                .ToList();

            if (googleCalendars.Count == 0)
            {
                GoogleCalendarListView.Visibility = Visibility.Collapsed;
                GoogleCalendarListHint.Text = "계정을 연동하면 캘린더 목록이 표시됩니다.";
                return;
            }

            var items = googleCalendars.Select(c => new GoogleCalendarCheckItem
            {
                Title = c.Title,
                GoogleId = c.GoogleId,
                IsChecked = c.SyncMode is "OneWay" or "TwoWay",
                CalendarNo = c.No
            }).ToList();

            GoogleCalendarListView.ItemsSource = items;
            GoogleCalendarListView.Visibility = Visibility.Visible;
            GoogleCalendarSaveButton.Visibility = Visibility.Visible;
            GoogleCalendarListHint.Text = $"동기화할 캘린더를 선택하세요 ({googleCalendars.Count}개)";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SettingsPage] 캘린더 목록 로드 실패: {ex.Message}");
        }
    }

    private async void OnGoogleCalendarSaveClicked(object sender, RoutedEventArgs e)
    {
        if (GoogleCalendarListView.ItemsSource is not List<GoogleCalendarCheckItem> items) return;

        try
        {
            using var service = Scheduler.Scheduler.CreateService();
            int saved = 0;

            foreach (var item in items)
            {
                var cal = await service.GetCalendarByGoogleIdAsync(item.GoogleId);
                if (cal != null)
                {
                    string newMode = item.IsChecked ? "TwoWay" : "None";
                    if (cal.SyncMode != newMode)
                    {
                        cal.SyncMode = newMode;
                        await service.UpdateCalendarAsync(cal);
                        saved++;
                    }
                }
            }

            GoogleCalendarListHint.Text = $"✅ 저장 완료 ({saved}개 변경됨)";
            Debug.WriteLine($"[SettingsPage] 캘린더 선택 저장: {saved}개 변경");
        }
        catch (Exception ex)
        {
            GoogleCalendarListHint.Text = $"저장 실패: {ex.Message}";
            Debug.WriteLine($"[SettingsPage] 캘린더 선택 저장 오류: {ex.Message}");
        }
    }

    private async void OnUploadSchoolScheduleClicked(object sender, RoutedEventArgs e)
    {
        if (!GoogleAuthService.HasCredentials)
        {
            UploadScheduleStatusText.Text = "Google 연동 기능이 비활성화 상태입니다.";
            return;
        }

        UploadSchoolScheduleButton.IsEnabled = false;
        UploadScheduleProgressRing.IsActive = true;
        UploadScheduleProgressRing.Visibility = Visibility.Visible;
        UploadScheduleStatusText.Text = "학사일정 조회 중...";

        try
        {
            // 1) 현재 학년도 학사일정 조회
            string schoolCode = Settings.SchoolCode.Value;
            int year = Settings.WorkYear.Value;

            if (string.IsNullOrEmpty(schoolCode))
            {
                UploadScheduleStatusText.Text = "학교 설정이 필요합니다.";
                return;
            }

            using var scheduleService = new SchoolScheduleService(SchoolDatabase.DbPath);
            var (success, message, schedules) = await scheduleService.GetSchedulesBySchoolYearAsync(schoolCode, year);

            if (!success || schedules.Count == 0)
            {
                UploadScheduleStatusText.Text = $"학사일정이 없습니다. ({message})";
                return;
            }

            UploadScheduleStatusText.Text = $"학사일정 {schedules.Count}건 등록 중...";

            // 2) Google Calendar에 일괄 등록
            using var authService = new GoogleAuthService();
            var apiClient = new GoogleCalendarApiClient(authService);
            using var syncService = new GoogleSyncService(authService, apiClient);

            var result = await syncService.UploadSchoolSchedulesAsync(schedules);

            if (result.Success)
            {
                UploadScheduleStatusText.Text = $"✅ {year}학년도 학사일정 등록 완료 — {result.Created}건 등록";
            }
            else
            {
                UploadScheduleStatusText.Text = $"⚠️ 등록 완료 (일부 오류) — {result.Summary}";
            }

            Debug.WriteLine($"[SettingsPage] 학사일정 등록 결과: {result.Summary}");
        }
        catch (Exception ex)
        {
            UploadScheduleStatusText.Text = $"❌ 등록 실패: {ex.Message}";
            Debug.WriteLine($"[SettingsPage] 학사일정 등록 오류: {ex.Message}");
        }
        finally
        {
            UploadSchoolScheduleButton.IsEnabled = true;
            UploadScheduleProgressRing.IsActive = false;
            UploadScheduleProgressRing.Visibility = Visibility.Collapsed;
        }
    }

    #endregion

    #region 일반 설정 이벤트 핸들러

    private void OnTopMostToggled(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;

        Settings.TopMost.Set(TopMostToggle.IsOn);

        // 실제 창의 TopMost 속성도 변경
        var window = App.MainWindow;
        if (window != null)
        {
            var hwnd = WindowNative.GetWindowHandle(window);
            // TODO: P/Invoke를 사용하여 창을 항상 위에 표시
        }
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized) return;

        if (ThemeComboBox.SelectedItem is ComboBoxItem item)
        {
            string? theme = item.Tag as string;
            if (theme != null)
            {
                Settings.Theme.Set(theme);

                // 테마 즉시 적용
                ApplyTheme(theme);
            }
        }
    }

    private async void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized) return;

        if (LanguageComboBox.SelectedItem is ComboBoxItem item)
        {
            string? language = item.Tag?.ToString();
            if (!string.IsNullOrEmpty(language))
            {
                Settings.Language.Set(language);
                await MessageBox.ShowAsync("언어 변경은 앱을 다시 시작한 후 적용됩니다.", "언어 변경");
            }
        }
    }

    private void ApplyTheme(string theme)
    {
        var rootElement = App.MainWindow?.Content as FrameworkElement;
        if (rootElement != null)
        {
            rootElement.RequestedTheme = theme switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ElementTheme.Default
            };
        }
    }

    #endregion

    #region 성능/캐시 이벤트 핸들러

    private void OnEnableCacheToggled(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;

        Settings.EnableCache.Set(EnableCacheToggle.IsOn);
    }

    private void OnDefaultPageSizeChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_isInitialized) return;

        if (!double.IsNaN(args.NewValue))
        {
            Settings.DefaultPageSize.Set((int)args.NewValue);
        }
    }

    #endregion

    #region 백업 이벤트 핸들러

    private void OnAutoBackupToggled(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;

        Settings.AutoBackup.Set(AutoBackupToggle.IsOn);
    }

    private void OnAutoBackupIntervalDaysChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_isInitialized) return;

        if (!double.IsNaN(args.NewValue))
        {
            Settings.AutoBackupIntervalDays.Set((int)args.NewValue);
        }
    }

    private void OnBackupRetentionCountChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_isInitialized) return;

        if (!double.IsNaN(args.NewValue))
        {
            Settings.BackupRetentionCount.Set((int)args.NewValue);
        }
    }

    private async void OnBackupClick(object sender, RoutedEventArgs e)
    {
        try
        {
            string? backupPath = Settings.Backup();
            if (!string.IsNullOrEmpty(backupPath))
            {
                await MessageBox.ShowAsync($"백업이 완료되었습니다.\n경로: {backupPath}", "백업 완료");
            }
            else
            {
                await MessageBox.ShowAsync("백업 중 오류가 발생했습니다.", "백업 실패");
            }
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync(ex.Message, "백업 오류");
        }
    }

    private async void OnRestoreClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker();
            var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
            InitializeWithWindow.Initialize(picker, hwnd);

            picker.ViewMode = PickerViewMode.List;
            picker.FileTypeFilter.Add(".db");

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                bool success = Settings.Restore(file.Path);
                if (success)
                {
                    await LoadSettings(); // UI 새로고침
                    await MessageBox.ShowAsync("설정이 복원되었습니다.", "복원 완료");
                }
                else
                {
                    await MessageBox.ShowAsync("설정 복원 중 오류가 발생했습니다.", "복원 실패");
                }
            }
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync(ex.Message, "복원 오류");
        }
    }

    #endregion

    #region 고급 이벤트 핸들러

    private void OnLogLevelChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized) return;

        if (LogLevelComboBox.SelectedItem is ComboBoxItem item)
        {
            string? logLevel = item.Tag?.ToString();
            if (logLevel != null)
            {
                Settings.LogLevel.Set(logLevel);
            }
        }
    }

    private async void OnResetSettingsClick(object sender, RoutedEventArgs e)
    {
        var confirmed = await MessageBox.ShowConfirmAsync(
            "모든 설정을 기본값으로 초기화하시겠습니까?\n이 작업은 되돌릴 수 없습니다.",
            "설정 초기화", "초기화", "취소");
        if (confirmed)
        {
            Settings.ResetToDefaults();
            await LoadSettings();
            await MessageBox.ShowAsync("모든 설정이 기본값으로 초기화되었습니다.", "초기화 완료");
        }
    }

    private async void OnPrintSettingsClick(object sender, RoutedEventArgs e)
    {
        Settings.PrintAll();
        await MessageBox.ShowAsync("설정 정보가 디버그 콘솔에 출력되었습니다.", "설정 출력");
    }

    #endregion

}

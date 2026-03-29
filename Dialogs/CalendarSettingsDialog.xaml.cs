using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Google;
using NewSchool.Models;
using NewSchool.Pages;

namespace NewSchool.Dialogs;

public sealed partial class CalendarSettingsDialog : ContentDialog
{
    public CalendarSettingsDialog()
    {
        this.InitializeComponent();
        this.Loaded += CalendarSettingsDialog_Loaded;
        this.Closing += CalendarSettingsDialog_Closing;
    }

    private async void CalendarSettingsDialog_Loaded(object sender, RoutedEventArgs e)
    {
        // 설정값 로드
        ShowEventsToggle.IsOn = Settings.ShowEvents.Value;
        ShowTasksToggle.IsOn = Settings.ShowTasks.Value;
        EventFontSizeNumberBox.Value = Settings.EventFontSize.Value;
        TaskFontSizeNumberBox.Value = Settings.TaskFontSize.Value;
        UseGoogleToggle.IsOn = Settings.UseGoogle.Value;
        GoogleAutoSyncToggle.IsOn = Settings.GoogleAutoSync.Value;
        GoogleSyncIntervalNumberBox.Value = Settings.GoogleSyncIntervalMinutes.Value;
        UpdateGoogleAuthStatus();
        await LoadGoogleCalendarListAsync();
    }

    private void CalendarSettingsDialog_Closing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        // 닫을 때 설정값 저장
        Settings.ShowEvents.Set(ShowEventsToggle.IsOn);
        Settings.ShowTasks.Set(ShowTasksToggle.IsOn);
        if (!double.IsNaN(EventFontSizeNumberBox.Value))
            Settings.EventFontSize.Set(EventFontSizeNumberBox.Value);
        if (!double.IsNaN(TaskFontSizeNumberBox.Value))
            Settings.TaskFontSize.Set(TaskFontSizeNumberBox.Value);
        Settings.UseGoogle.Set(UseGoogleToggle.IsOn);
        Settings.GoogleAutoSync.Set(GoogleAutoSyncToggle.IsOn);
        if (!double.IsNaN(GoogleSyncIntervalNumberBox.Value))
            Settings.GoogleSyncIntervalMinutes.Set((int)GoogleSyncIntervalNumberBox.Value);
    }

    #region Google 연동

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
                await FetchAndSaveGoogleCalendarsAsync(authService);
            }
            else
            {
                GoogleAuthStatusText.Text = "❌ 인증에 실패했습니다.";
            }
        }
        catch (Exception ex)
        {
            GoogleAuthStatusText.Text = $"❌ 오류: {ex.Message}";
            Debug.WriteLine($"[CalendarSettingsDialog] Google 인증 오류: {ex.Message}");
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
        }
        catch (Exception ex)
        {
            GoogleAuthStatusText.Text = $"오류: {ex.Message}";
        }
        UpdateGoogleAuthStatus();
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
                    GoogleSyncStatusText.Text += $"\n{string.Join("\n", result.ErrorMessages)}";
            }
        }
        catch (Exception ex)
        {
            GoogleSyncStatusText.Text = $"❌ 동기화 실패: {ex.Message}";
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

    private async Task FetchAndSaveGoogleCalendarsAsync(GoogleAuthService authService)
    {
        try
        {
            var apiClient = new GoogleCalendarApiClient(authService);
            var googleCalendars = await apiClient.GetCalendarListAsync();
            using var service = Scheduler.Scheduler.CreateService();

            string schoolName = Settings.SchoolName.Value;
            if (string.IsNullOrWhiteSpace(schoolName)) schoolName = "학교";

            var primaryCal = googleCalendars.Find(c => c.Primary == true);
            var schoolGoogleCal = googleCalendars.Find(c =>
                string.Equals(c.Summary, schoolName, StringComparison.OrdinalIgnoreCase));

            if (schoolGoogleCal == null)
            {
                GoogleAuthStatusText.Text = $"Google에 '{schoolName}' 캘린더 생성 중...";
                var created = await apiClient.InsertCalendarAsync(schoolName, "학사 일정 · 수업 · 학급 · 업무");
                if (created != null)
                {
                    schoolGoogleCal = new GoogleCalendarListEntry
                    {
                        Id = created.Id,
                        Summary = created.Summary,
                        BackgroundColor = "#4285F4"
                    };
                }
            }

            var allLocal = await service.GetAllCalendarsAsync();
            int mapped = 0;

            foreach (var local in allLocal)
            {
                string? targetGoogleId = null;

                if (local.Title == CategoryNames.Personal && primaryCal != null)
                    targetGoogleId = primaryCal.Id;
                else if (local.Title is CategoryNames.Lesson or CategoryNames.Homeroom or CategoryNames.Work && schoolGoogleCal != null)
                    targetGoogleId = schoolGoogleCal.Id;

                if (targetGoogleId != null && local.GoogleId != targetGoogleId)
                {
                    local.GoogleId = targetGoogleId;
                    if (local.SyncMode == "None") local.SyncMode = "TwoWay";
                    await service.UpdateCalendarAsync(local);
                    mapped++;
                }
            }

            GoogleAuthStatusText.Text = $"✅ 학교({schoolName}) + 개인 캘린더 매핑 완료 ({mapped}개)";
            await LoadGoogleCalendarListAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CalendarSettingsDialog] 캘린더 매핑 실패: {ex.Message}");
            GoogleAuthStatusText.Text = "✅ 연동됨 (캘린더 매핑 실패)";
        }
    }

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
            Debug.WriteLine($"[CalendarSettingsDialog] 캘린더 목록 로드 실패: {ex.Message}");
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
        }
        catch (Exception ex)
        {
            GoogleCalendarListHint.Text = $"저장 실패: {ex.Message}";
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
            string schoolCode = Settings.SchoolCode.Value;
            int year = Settings.WorkYear.Value;

            if (string.IsNullOrEmpty(schoolCode))
            {
                UploadScheduleStatusText.Text = "학교 설정이 필요합니다.";
                return;
            }

            using var scheduleService = new Services.SchoolScheduleService(SchoolDatabase.DbPath);
            var (success, message, schedules) = await scheduleService.GetSchedulesBySchoolYearAsync(schoolCode, year);

            if (!success || schedules.Count == 0)
            {
                UploadScheduleStatusText.Text = $"학사일정이 없습니다. ({message})";
                return;
            }

            UploadScheduleStatusText.Text = $"학사일정 {schedules.Count}건 등록 중...";

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
        }
        catch (Exception ex)
        {
            UploadScheduleStatusText.Text = $"❌ 등록 실패: {ex.Message}";
            Debug.WriteLine($"[CalendarSettingsDialog] 학사일정 등록 오류: {ex.Message}");
        }
        finally
        {
            UploadSchoolScheduleButton.IsEnabled = true;
            UploadScheduleProgressRing.IsActive = false;
            UploadScheduleProgressRing.Visibility = Visibility.Collapsed;
        }
    }

    #endregion
}

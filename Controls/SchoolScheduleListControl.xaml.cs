using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Models;
using NewSchool.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace NewSchool.Controls;

/// <summary>
/// 학사일정 목록 컨트롤
/// </summary>
public sealed partial class SchoolScheduleListControl : UserControl
{
    private ObservableCollection<SchoolScheduleGroup> _scheduleGroups = new();

    public ObservableCollection<SchoolScheduleGroup> ScheduleGroups
    {
        get => _scheduleGroups;
        set
        {
            _scheduleGroups = value ?? new ObservableCollection<SchoolScheduleGroup>();
            ScheduleItemsRepeater.ItemsSource = _scheduleGroups;
        }
    }

    public SchoolScheduleListControl()
    {
        this.InitializeComponent();
        ScheduleItemsRepeater.ItemsSource = _scheduleGroups;
    }

    /// <summary>
    /// 학사일정 로드 (날짜 범위)
    /// </summary>
    public async Task LoadSchedulesAsync(DateTime startDate, int days = 30, bool includeDownload = false)
    {
        try
        {
            using var service = new SchoolScheduleService(SchoolDatabase.DbPath);
            List<SchoolSchedule>? schedules = null;

            if (Settings.IsNeisEventDownloaded.Value || !includeDownload)
            {
                // DB에서 조회
                var (Success, Message, Schedules) = await service.GetSchedulesByDataRangeAsync(
                    Settings.SchoolCode, startDate, startDate.AddDays(days + 1));

                if (Success && Schedules != null && Schedules.Any())
                {
                    schedules = Schedules;
                }
                else
                {
                    Debug.WriteLine($"[SchoolScheduleListControl] 학사일정 조회 실패: {Message}");
                }
            }
            else
            {
                // NEIS API에서 다운로드
                var (Success, Message, Schedules) = await service.DownloadFromNeisAsync(
                    Settings.SchoolCode, Settings.ProvinceCode, startDate.Year,
                    startDate, startDate.AddDays(days + 1));

                if (Success && Schedules != null)
                {
                    schedules = Schedules;
                    Settings.IsNeisEventDownloaded.Set(true);
                }
                else
                {
                    Debug.WriteLine($"[SchoolScheduleListControl] 학사일정 다운로드 실패: {Message}");
                }
            }

            // 그룹화하여 표시
            if (schedules != null)
            {
                var grouped = SchoolScheduleGroupHelper.GroupSchedules(schedules);
                _scheduleGroups.Clear();
                foreach (var group in grouped)
                {
                    _scheduleGroups.Add(group);
                }
            }
            else
            {
                _scheduleGroups.Clear();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SchoolScheduleListControl] 학사일정 로드 오류: {ex.Message}");
        }
    }

    /// <summary>
    /// 학사일정 설정 (직접 할당)
    /// </summary>
    public void SetSchedules(List<SchoolSchedule> schedules)
    {
        try
        {
            if (schedules == null || schedules.Count == 0)
            {
                _scheduleGroups.Clear();
                return;
            }

            var grouped = SchoolScheduleGroupHelper.GroupSchedules(schedules);
            _scheduleGroups.Clear();
            foreach (var group in grouped)
            {
                _scheduleGroups.Add(group);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SchoolScheduleListControl] 학사일정 설정 오류: {ex.Message}");
        }
    }
}

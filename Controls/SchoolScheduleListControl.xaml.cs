using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Models;
using NewSchool.Services;

namespace NewSchool.Controls;

/// <summary>
/// 학사일정 목록 컨트롤
/// </summary>
public sealed partial class SchoolScheduleListControl : UserControl
{
    private ObservableCollection<SchoolScheduleGroup> _scheduleGroups = new();

    // 바깥에서 목록을 통째로 갈아 끼우던 ScheduleGroups 속성은 지웠다(2026-08-30) —
    // 부르는 곳이 없었다. 이 컨트롤은 스스로 학사일정을 읽어 _scheduleGroups 를 채우고,
    // ItemsSource 는 생성자에서 한 번만 묶는다.

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

            // 받아 오라는 부탁을 받았고 아직 한 번도 받은 적이 없으면, 학년도 전체를 받아
            // DB 에 넣는다. 예전에는 받은 것을 그리기만 하고 저장하지 않은 채 깃발만 켜서,
            // 그 다음부터는 빈 DB 를 읽어 학사일정이 사라졌다.
            if (includeDownload && !Settings.IsNeisEventDownloaded.Value)
            {
                var sync = await service.SyncSchoolYearFromNeisAsync(
                    Settings.SchoolCode, Settings.ProvinceCode, DateTimeHelper.SchoolYearOf(startDate));

                if (!sync.Success)
                {
                    Debug.WriteLine($"[SchoolScheduleListControl] 학사일정 다운로드 실패: {sync.Message}");
                }
            }

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

    // 학사일정을 밖에서 넣어 주던 SetSchedules 는 호출부가 없어 지웠다(39차) —
    // 이 컨트롤은 기간이 정해지면 스스로 DB 에서 읽는다.
}

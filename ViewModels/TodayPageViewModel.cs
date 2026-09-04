using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using NewSchool.Collections;
using NewSchool.Models;
using NewSchool.Scheduler;
using NewSchool.Services;

namespace NewSchool.ViewModels;

/// <summary>
/// TodayPage의 ViewModel
/// ⚡ 성능 최적화: OptimizedObservableCollection 사용으로 대량 업데이트 80% 향상
/// </summary>
public class TodayPageViewModel : NotifyPropertyChangedBase
{
    private readonly DispatcherQueue _dispatcherQueue;

    /// <summary>
    /// 학사일정 컬렉션 (최적화됨)
    /// </summary>
    public OptimizedObservableCollection<SchoolSchedule> SchoolEvents { get; }

    /// <summary>
    /// 학사일정 그룹 (표시용)
    /// </summary>
    // 그룹화한 학사일정(SchoolEventGroups)은 지웠다(2026-08-30) — 채우기만 하고 아무도
    // 읽지 않았다(바인딩·코드 참조 0건). 그런데 채우려고 목록을 읽을 때마다
    // SchoolScheduleGroupHelper.GroupSchedules 로 묶는 일까지 했다.
    // 그룹화가 필요한 곳(SchoolScheduleListControl·GoogleSyncService)은 각자 직접 부른다.

    /// <summary>
    /// 할일 목록 (최적화됨, KEvent ItemType="task")
    /// </summary>
    public OptimizedObservableCollection<KEvent> Tasks { get; }
    
    /// <summary>
    /// 급식 정보 (단일 객체이므로 프로퍼티로 관리)
    /// </summary>
    private List<SchoolMeal>? _meals;
    public List<SchoolMeal>? Meals
    {
        get => _meals;
        set
        {
            if (_meals != value)
            {
                _meals = value;
                OnPropertyChanged();
            }
        }
    }
    
    /// <summary>
    /// 로딩 상태
    /// </summary>
    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (_isLoading != value)
            {
                _isLoading = value;
                OnPropertyChanged();
            }
        }
    }
    
    /// <summary>
    /// 생성자
    /// </summary>
    /// <param name="dispatcherQueue">UI 스레드 디스패처</param>
    public TodayPageViewModel(DispatcherQueue dispatcherQueue)
    {
        _dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));

        SchoolEvents = new OptimizedObservableCollection<SchoolSchedule>();
        Tasks = new OptimizedObservableCollection<KEvent>();
        _meals = null;
        _isLoading = false;
    }
    
    /// <summary>
    /// 모든 데이터를 비동기로 로드
    /// </summary>
    public async Task LoadDataAsync()
    {
        IsLoading = true;
        
        try
        {
            // 병렬로 데이터 로드
            var schoolEventsTask = LoadSchoolEventsAsync();
            var tasksTask = LoadTasksAsync();
            var mealsTask = LoadMealsAsync();
            
            await Task.WhenAll(schoolEventsTask, tasksTask, mealsTask);
        }
        catch (Exception ex)
        {
            NewSchool.Logging.Log.Error("TodayPageViewModel", "오늘 화면 자료를 읽지 못했다", ex);
            Debug.WriteLine($"[TodayPageViewModel] StackTrace: {ex.StackTrace}");
        }
        finally
        {
            IsLoading = false;
        }
    }
    
    /// <summary>
    /// 학사일정 로드
    /// </summary>
    private async Task LoadSchoolEventsAsync()
    {
        try
        {
            using var schoolScheduleService = new SchoolScheduleService(SchoolDatabase.DbPath);
            
            List<SchoolSchedule>? schedules = null;

            // 아직 한 번도 받은 적이 없으면 학년도 전체를 받아 DB 에 넣는다.
            // 예전에는 여기서 30일치만 받아 화면에 그리고 저장은 하지 않은 채 깃발만 켰다 —
            // 그러면 다음 실행부터는 "이미 받았다"며 빈 DB 를 읽어 학사일정이 사라졌다.
            if (!Settings.IsNeisEventDownloaded.Value)
            {
                var sync = await schoolScheduleService.SyncSchoolYearFromNeisAsync(
                    Settings.SchoolCode, Settings.ProvinceCode, DateTimeHelper.SchoolYearOf(DateTime.Today));

                if (!sync.Success)
                {
                    Debug.WriteLine($"[TodayPageViewModel] 학사일정 다운로드 실패: {sync.Message}");
                }
            }

            var (Success, Message, Schedules) = await schoolScheduleService.GetSchedulesByDataRangeAsync(
                Settings.SchoolCode, DateTime.Today, DateTime.Today.AddDays(29)); // +1일로 수정

            if (Success && Schedules != null && Schedules.Any())
            {
                schedules = Schedules;
            }
            else
            {
                Debug.WriteLine($"[TodayPageViewModel] 학사일정 조회 실패: {Message}");
            }

            // ⚡ UI 스레드에서 컬렉션 업데이트 (최적화됨 - AddRange 사용)
            if (schedules != null)
            {
                _dispatcherQueue.TryEnqueue(() =>
                {
                    SchoolEvents.ReplaceAll(schedules); // 80% 성능 향상!
                    
                });
            }
        }
        catch (Exception ex)
        {
            NewSchool.Logging.Log.Error("TodayPageViewModel", "학사일정을 읽지 못했다 — 일정이 없는 것처럼 보인다", ex);
        }
    }
    
    /// <summary>
    /// 할일 목록 로드
    /// </summary>
    private async Task LoadTasksAsync()
    {
        try
        {
            using var service = Scheduler.Scheduler.CreateService();
            var ktasklist = await service.GetTasksByDateAsync(DateTime.Today, 42, true); // returns List<KEvent> (ItemType="task")
            
            // ⚡ UI 스레드에서 컬렉션 업데이트 (최적화됨 - AddRange 사용)
            if (ktasklist != null)
            {
                _dispatcherQueue.TryEnqueue(() =>
                {
                    Tasks.ReplaceAll(ktasklist); // 80% 성능 향상!
                });
            }
        }
        catch (Exception ex)
        {
            NewSchool.Logging.Log.Error("TodayPageViewModel", "할 일 목록을 읽지 못했다 — 할 일이 없는 것처럼 보인다", ex);
        }
    }
    
    /// <summary>
    /// 급식 정보 로드
    /// </summary>
    private async Task LoadMealsAsync()
    {
        try
        {
            Debug.WriteLine($"[TodayPageViewModel] 급식 정보 로드 시작 - 날짜: {DateTime.Today:yyyy-MM-dd}");
            var meals = await Functions.GetSchoolMealsAsync(DateTime.Today, mmealScCode: "");
            Debug.WriteLine($"[TodayPageViewModel] 급식 정보 API 응답 - 개수: {meals?.Count ?? 0}");

            if (meals != null && meals.Count > 0)
            {
                foreach (var meal in meals)
                {
                    Debug.WriteLine($"[TodayPageViewModel] 급식: {meal.MLSV_YMD:yyyy-MM-dd} {meal.MMEAL_SC_NM} - {meal.DDISH_NM}");
                }
            }

            // UI 스레드에서 프로퍼티 업데이트
            _dispatcherQueue.TryEnqueue(() =>
            {
                Meals = meals;
                Debug.WriteLine($"[TodayPageViewModel] Meals 프로퍼티 업데이트 완료 - 개수: {Meals?.Count ?? 0}");
            });
        }
        catch (Exception ex)
        {
            NewSchool.Logging.Log.Warning("TodayPageViewModel", $"급식 정보를 읽지 못했다: {ex.Message}");
            Debug.WriteLine($"[TodayPageViewModel] StackTrace: {ex.StackTrace}");
        }
    }
    
    #region INotifyPropertyChanged 구현
    
    
    
    #endregion
}

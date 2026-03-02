using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using NewSchool.Board;
using NewSchool.Board.Services;
using NewSchool.ViewModels;

namespace NewSchool.Pages;

/// <summary>
/// 오늘의 할일, 학사일정, 급식 등을 표시하는 메인 페이지
/// MVVM 패턴 적용: TodayPageViewModel 사용
/// </summary>
public sealed partial class TodayPage : Page, INotifyPropertyChanged
{
    private TodayPageViewModel? _viewModel;
    
    /// <summary>
    /// ViewModel - x:Bind를 위한 public 속성
    /// </summary>
    public TodayPageViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            if (_viewModel != value)
            {
                _viewModel = value;
                OnPropertyChanged();
            }
        }
    }
    
    public TodayPage()
    {
        InitializeComponent();
    }
    
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        
        // ViewModel 초기화 (DispatcherQueue 전달)
        ViewModel = new TodayPageViewModel(DispatcherQueue);
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        Debug.WriteLine("[TodayPage] ViewModel 초기화 완료");
    }
    
    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);

        // 메모리 최적화: 리소스 명시적 해제
        if (ViewModel != null)
        {
            // 이벤트 구독 해제 (메모리 누수 방지)
            ViewModel.PropertyChanged -= ViewModel_PropertyChanged;

            // 컬렉션 비우기 (GC 도움)
            ViewModel.SchoolEvents.Clear();
            ViewModel.Tasks.Clear();
            ViewModel.Meals = null;
        }

        // KAgendaControl은 내부 상태만 가지므로 별도 해제 불필요

        // ViewModel 참조 해제
        ViewModel = null;

        Debug.WriteLine("[TodayPage] 메모리 정리 완료");
    }
    
    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // ViewModel의 속성 변경 처리 (필요시)
    }
    
    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        // ViewModel을 로컬 변수로 캡처 (Task 실행 중 null이 되는 것 방지)
        var vm = ViewModel;
        if (vm == null)
        {
            Debug.WriteLine("[TodayPage] ViewModel이 초기화되지 않았습니다.");
            return;
        }

        try
        {
            // ⚡ 시간표 로드
            try
            {
                await LecTimeTable.LoadMyScheduleAsync();
#if DEBUG
                Debug.WriteLine("[TodayPage] ✓ 시간표 로드 완료");
#endif
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TodayPage] ✗ 시간표 로드 실패: {ex.GetType().Name} - {ex.Message}");
            }

            // ⚡ 학사일정 로드
            try
            {
                await ScheduleList.LoadSchedulesAsync(DateTime.Today, 28, true);
#if DEBUG
                Debug.WriteLine("[TodayPage] ✓ 학사일정 로드 완료");
#endif
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TodayPage] ✗ 학사일정 로드 실패: {ex.GetType().Name} - {ex.Message}");
            }

            // ⚡ 통합 할 일 + 일정 로드
            try
            {
                await AgendaList.LoadPendingAndFutureAsync();
#if DEBUG
                Debug.WriteLine("[TodayPage] ✓ 할 일/일정 로드 완료");
#endif
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TodayPage] ✗ 할 일/일정 로드 실패: {ex.GetType().Name} - {ex.Message}");
            }

            // ⚡ 급식 정보 로드
            try
            {
                await MealBox.LoadMealsAsync(DateTime.Today);
#if DEBUG
                Debug.WriteLine("[TodayPage] ✓ 급식 정보 로드 완료");
#endif
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TodayPage] ✗ 급식 정보 로드 실패: {ex.GetType().Name} - {ex.Message}");
            }

#if DEBUG
            Debug.WriteLine("[TodayPage] ⚡ 데이터 로드 완료");
#endif
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TodayPage] 페이지 로드 오류: {ex.GetType().Name} - {ex.Message}");
            Debug.WriteLine($"[TodayPage] StackTrace: {ex.StackTrace}");
        }
    }

    
    #region INotifyPropertyChanged 구현
    
    public event PropertyChangedEventHandler? PropertyChanged;
    
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    
    #endregion
}

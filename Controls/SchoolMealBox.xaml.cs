using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Models;

namespace NewSchool.Controls;

/// <summary>
/// 급식 정보 표시 컨트롤 (날짜 선택 기능 포함)
/// </summary>
public sealed partial class SchoolMealBox : UserControl, INotifyPropertyChanged
{
    private ObservableCollection<SchoolMeal> _meals = new();
    private DateTimeOffset _selectedDate = DateTimeOffset.Now;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// 선택된 날짜
    /// </summary>
    public DateTimeOffset SelectedDate
    {
        get => _selectedDate;
        set
        {
            if (_selectedDate != value)
            {
                _selectedDate = value;
                OnPropertyChanged();
            }
        }
    }

    public ObservableCollection<SchoolMeal> Meals
    {
        get => _meals;
        set
        {
            _meals = value ?? new ObservableCollection<SchoolMeal>();
            MealsRepeater.ItemsSource = _meals;
        }
    }

    public SchoolMealBox()
    {
        this.InitializeComponent();
        MealsRepeater.ItemsSource = _meals;
        
        // 초기 날짜를 오늘로 설정
        SelectedDate = DateTimeOffset.Now;
    }

    /// <summary>
    /// 특정 날짜의 급식 정보 로드
    /// </summary>
    public async Task LoadMealsAsync(DateTime date)
    {
        SetStatus(null);

        try
        {
            Debug.WriteLine($"[SchoolMealBox] 급식 정보 로드 시작 - 날짜: {date:yyyy-MM-dd}");
            
            var meals = await Functions.GetSchoolMealsAsync(date, mmealScCode: "");
            
            if (meals != null && meals.Count > 0)
            {
                _meals.Clear();
                foreach (var meal in meals)
                {
                    _meals.Add(meal);
                }
                Debug.WriteLine($"[SchoolMealBox] 급식 정보 로드 완료 - {meals.Count}개");
            }
            else
            {
                _meals.Clear();
                Debug.WriteLine("[SchoolMealBox] 급식 정보 없음");
            }
        }
        catch (Exception ex)
        {
            // 화면은 비우되 실패 사실은 호출부로 올린다.
            // (홈 화면은 이 예외를 받아 전역 InfoBar 로 알린다 — 예전에는 여기서 삼켜서
            //  "오늘 급식 없음"과 구분되지 않았다)
            Debug.WriteLine($"[SchoolMealBox] 급식 정보 로드 오류: {ex}");
            _meals.Clear();
            throw;
        }
    }

    /// <summary>
    /// 사용자가 상자 안에서 날짜를 바꾼 경우의 조회 — 실패해도 앱이 죽으면 안 되므로
    /// 여기서 잡아 상자 안에 안내한다.
    ///
    /// <para>⚠ <see cref="LoadMealsAsync"/> 는 실패를 <b>예외로 올린다</b>(홈 화면이 전역
    /// InfoBar 로 모으기 위해서다). 그래서 <c>async void</c> 핸들러에서 그대로 부르면
    /// 미처리 예외가 된다 — 반드시 이 경로를 쓸 것.</para>
    /// </summary>
    private async Task LoadMealsForUserAsync(DateTime date)
    {
        try
        {
            await LoadMealsAsync(date);
        }
        catch (Exception ex)
        {
            SetStatus($"급식 정보를 불러오지 못했습니다. {ex.Message}");
        }
    }

    private void SetStatus(string? message)
    {
        if (TxtMealStatus == null) return;

        TxtMealStatus.Text = message ?? string.Empty;
        TxtMealStatus.Visibility = string.IsNullOrEmpty(message)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    /// <summary>
    /// 급식 정보 직접 설정
    /// </summary>
    public void SetMeals(List<SchoolMeal> meals)
    {
        try
        {
            _meals.Clear();
            if (meals != null)
            {
                foreach (var meal in meals)
                {
                    _meals.Add(meal);
                }
            }
            Debug.WriteLine($"[SchoolMealBox] 급식 정보 설정 완료 - {meals?.Count ?? 0}개");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SchoolMealBox] 급식 정보 설정 오류: {ex.Message}");
        }
    }

    #region 날짜 선택 이벤트 핸들러

    /// <summary>
    /// 이전 날짜 버튼 클릭
    /// </summary>
    private async void PreviousDayButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedDate = SelectedDate.AddDays(-1);
        await LoadMealsForUserAsync(SelectedDate.DateTime);
    }

    /// <summary>
    /// 다음 날짜 버튼 클릭
    /// </summary>
    private async void NextDayButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedDate = SelectedDate.AddDays(1);
        await LoadMealsForUserAsync(SelectedDate.DateTime);
    }

    /// <summary>
    /// 오늘 버튼 클릭
    /// </summary>
    private async void TodayButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedDate = DateTimeOffset.Now;
        await LoadMealsForUserAsync(SelectedDate.DateTime);
    }

    /// <summary>
    /// 날짜 선택기 변경
    /// </summary>
    private async void DatePicker_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (args.NewDate.HasValue)
        {
            await LoadMealsForUserAsync(args.NewDate.Value.DateTime);
        }
    }

    #endregion

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

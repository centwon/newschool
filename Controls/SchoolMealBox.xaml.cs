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
        try
        {
            Debug.WriteLine($"[SchoolMealBox] 급식 정보 로드 시작 - 날짜: {date:yyyy-MM-dd}");
            
            var meals = await Functions.GetSchoolMealsAsync(date);
            
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
            Debug.WriteLine($"[SchoolMealBox] 급식 정보 로드 오류: {ex.Message}");
            _meals.Clear();
        }
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
        await LoadMealsAsync(SelectedDate.DateTime);
    }

    /// <summary>
    /// 다음 날짜 버튼 클릭
    /// </summary>
    private async void NextDayButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedDate = SelectedDate.AddDays(1);
        await LoadMealsAsync(SelectedDate.DateTime);
    }

    /// <summary>
    /// 오늘 버튼 클릭
    /// </summary>
    private async void TodayButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedDate = DateTimeOffset.Now;
        await LoadMealsAsync(SelectedDate.DateTime);
    }

    /// <summary>
    /// 날짜 선택기 변경
    /// </summary>
    private async void DatePicker_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (args.NewDate.HasValue)
        {
            await LoadMealsAsync(args.NewDate.Value.DateTime);
        }
    }

    #endregion

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

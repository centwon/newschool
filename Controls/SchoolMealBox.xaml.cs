using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Models;

namespace NewSchool.Controls;

/// <summary>
/// 급식 정보 표시 컨트롤.
///
/// <para>날짜는 <b>스스로 들지 않는다</b>. 어느 날짜를 볼지는 이 상자를 안고 있는 화면
/// (<c>TodayPage</c>)의 날짜 이동이 정하고, 여기는 <see cref="LoadMealsAsync"/> 로
/// 받은 날짜만 그린다 — 기준이 하나면 헤더와 급식이 어긋날 일도 없다.</para>
/// </summary>
public sealed partial class SchoolMealBox : UserControl
{
    private ObservableCollection<SchoolMeal> _meals = new();

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
    }

    /// <summary>
    /// 특정 날짜의 급식 정보 로드
    /// </summary>
    public async Task LoadMealsAsync(DateTime date)
    {
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

    // 급식 정보를 밖에서 넣어 주던 SetMeals 는 호출부가 없어 지웠다(39차) —
    // 이 컨트롤은 날짜가 정해지면 스스로 NEIS 에서 받아 온다.
    //
    // 상자 안의 날짜 이동(◀ ▶ · 달력 선택기)도 지웠다. 홈 화면 머리의 날짜 이동과 하는 일이
    // 같은데 기준을 둘로 나눠 놓아, 상자에서 날짜를 옮기면 헤더는 어제를 말하고 급식만 내일을
    // 보여 줄 수 있었다. 그와 함께 상자 안 실패 안내(TxtMealStatus)와, 그것을 띄우려고
    // 예외를 삼키던 LoadMealsForUserAsync 도 갈 곳을 잃어 함께 걷었다 —
    // 이제 실패 경로는 "LoadMealsAsync 가 던지고 홈 화면이 전역 InfoBar 로 모은다" 하나뿐이다.
}

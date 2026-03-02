using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using NewSchool.Models;
using NewSchool.Services;
using NewSchool.ViewModels;

namespace NewSchool.Controls;

/// <summary>
/// 학급일지 목록 조회 다이얼로그
/// </summary>
public sealed partial class ClassDiaryListWin : Window
{
    #region Fields

    private readonly ClassDiaryService _diaryService;
    private readonly int _year;
    private readonly int _semester;
    private readonly int _grade;
    private readonly int _classNumber;
    private readonly ObservableCollection<ClassDiaryViewModel> _diaries = new();

    #endregion

    #region Events

    /// <summary>일지 선택 이벤트</summary>
    public event EventHandler<ClassDiaryViewModel>? DiarySelected;

    #endregion

    #region Constructor

    /// <summary>
    /// 생성자
    /// </summary>
    public ClassDiaryListWin(int year, int semester, int grade, int classNumber)
    {
        this.InitializeComponent();
        
        _diaryService = new ClassDiaryService(SchoolDatabase.DbPath);
        _year = year;
        _semester = semester;
        _grade = grade;
        _classNumber = classNumber;
        
        // 제목 설정
        Title = $"{year}학년도 {semester}학기 {grade}학년 {classNumber}반 학급일지";
        
        // ItemsSource 바인딩
        DiaryItemsRepeater.ItemsSource = _diaries;
        
        // 초기화
        InitializeAsync();
        
        this.Closed += OnWindowClosed;
    }

    #endregion

    #region Initialization

    private async void InitializeAsync()
    {
        // 기본 기간 설정 (현재 달)
        var today = DateTime.Today;
        DatePickerStart.Date = new DateTimeOffset(new DateTime(today.Year, today.Month, 1));
        DatePickerEnd.Date = new DateTimeOffset(today);

        // 일지 로드
        await LoadDiariesAsync();
    }

    #endregion

    #region Data Loading

    /// <summary>
    /// 일지 목록 로드
    /// </summary>
    private async Task LoadDiariesAsync()
    {
        try
        {
            if (!DatePickerStart.Date.HasValue || !DatePickerEnd.Date.HasValue)
            {
                await MessageBox.ShowAsync("날짜를 선택해주세요.", "알림");
                return;
            }

            var startDate = DatePickerStart.Date.Value.DateTime;
            var endDate = DatePickerEnd.Date.Value.DateTime;

            if (startDate > endDate)
            {
                await MessageBox.ShowAsync("시작일이 종료일보다 늦습니다.", "알림");
                return;
            }
            
            // 일지 조회
            var diaries = await _diaryService.GetDateRangeDiariesAsync(
                Settings.SchoolCode,
                _year,
                _semester,
                _grade,
                _classNumber,
                startDate,
                endDate);
            
            // ViewModel으로 변환
            _diaries.Clear();
            foreach (var diary in diaries.OrderByDescending(d => d.Date))
            {
                _diaries.Add(new ClassDiaryViewModel(diary));
            }
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"일지 목록 로드 실패: {ex.Message}", "오류");
        }
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// 조회 버튼
    /// </summary>
    private async void BtnSearch_Click(object sender, RoutedEventArgs e)
    {
        await LoadDiariesAsync();
    }

    /// <summary>
    /// 일지 아이템 클릭
    /// </summary>
    private void DiaryItem_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border && border.DataContext is ClassDiaryViewModel diary)
        {
            // 선택 이벤트 발생
            DiarySelected?.Invoke(this, diary);
            
            // 창 닫기
            Close();
        }
    }

    /// <summary>
    /// 일지 아이템 마우스 오버
    /// </summary>
    private void DiaryItem_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border)
        {
            border.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"];
        }
    }

    /// <summary>
    /// 일지 아이템 마우스 아웃
    /// </summary>
    private void DiaryItem_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border)
        {
            border.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
        }
    }

    /// <summary>
    /// 닫기 버튼
    /// </summary>
    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>
    /// 윈도우 닫힘
    /// </summary>
    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _diaryService?.Dispose();
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// 다이얼로그 표시
    /// </summary>
    public void ShowDialog()
    {
        this.Activate();
    }

    /// <summary>
    /// 윈도우 크기 설정
    /// </summary>
    public void SetSize(int width, int height)
    {
        var appWindow = this.AppWindow;
        if (appWindow != null)
        {
            appWindow.Resize(new Windows.Graphics.SizeInt32(width, height));
        }
    }

    #endregion
}

using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Board.Pages;
using NewSchool.Board.Models;
using NewSchool.Services;
using NewSchool.Controls;

namespace NewSchool.Pages;

public sealed partial class ClassDiaryPage : Page
{
    private DateTime _currentDate = DateTime.Today;
    private int _currentYear;
    private int _currentGrade;
    private int _currentClass;
    private PostListPage? _postListPage;

    public ClassDiaryPage()
    {
        this.InitializeComponent();
        InitializeControls();
    }

    /// <summary>
    /// 컨트롤 초기화
    /// </summary>
    private void InitializeControls()
    {
        // 날짜 초기화
        DatePicker.Date = DateTime.Today;
    }

    #region Board 초기화

    /// <summary>
    /// PostListPage 초기화
    /// </summary>
    private void InitializeBoardFrame()
    {
        if (_currentGrade == 0 || _currentClass == 0)
            return;

        BoardFrame.Navigate(typeof(PostListPage), new PostListPageParameter
        {
            Category = "학급",
            ViewMode = BoardViewMode.Table,
            AllowCategoryChange = false,
            AllowViewModeChange = true,
            ShowSubjectFilter = true,
            IsEmbedded = false,
            Title = "학급 게시판"
        });

        _postListPage = BoardFrame.Content as PostListPage;
    }

    #endregion

    #region 데이터 로드

    /// <summary>
    /// 학생 목록 로드
    /// </summary>
    private async Task LoadStudentsAsync()
    {
        if (_currentYear == 0 || _currentGrade == 0 || _currentClass == 0)
        {
            StudentList.ClearStudents();
            return;
        }

        using var enrollmentService = new EnrollmentService();
        var students = await enrollmentService.GetClassRosterAsync(
            Settings.SchoolCode.Value,
            _currentYear,
            _currentGrade,
            _currentClass);

        StudentList.LoadStudents(students);
    }

    /// <summary>
    /// 학급일지 로드
    /// </summary>
    private async Task LoadDiaryAsync()
    {
        if (_currentGrade == 0 || _currentClass == 0 || _currentYear == 0) return;

        await DiaryBox.LoadDiaryAsync(_currentGrade, _currentClass, _currentDate);
    }

    /// <summary>
    /// 모든 데이터 새로고침
    /// </summary>
    private async Task RefreshAllDataAsync()
    {
        await LoadDiaryAsync();
    }

    #endregion

    #region 이벤트 핸들러

    /// <summary>
    /// SchoolFilterPicker 선택 변경
    /// </summary>
    private async void FilterPicker_SelectionChanged(object? sender, Controls.SchoolFilterChangedEventArgs e)
    {
        _currentYear = e.Year;
        _currentGrade = e.Grade;
        _currentClass = e.Class;

        if (_currentYear > 0 && _currentGrade > 0 && _currentClass > 0)
        {
            await LoadStudentsAsync();
            InitializeBoardFrame();
            await RefreshAllDataAsync();
        }
    }

    /// <summary>
    /// 날짜 선택 변경
    /// </summary>
    private async void DatePicker_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (args.NewDate == null || args.NewDate == args.OldDate) return;

        // 이전 일지 자동 저장
        await DiaryBox.SaveDiaryAsync();

        // 새 날짜로 변경
        _currentDate = args.NewDate.Value.DateTime;

        // 새 일지 로드
        await RefreshAllDataAsync();
    }

    /// <summary>
    /// 이전 날짜
    /// </summary>
    private void BtnDatePrev_Click(object sender, RoutedEventArgs e)
    {
        if (DatePicker.Date == null) return;
        DatePicker.Date = DatePicker.Date.Value.AddDays(-1);
    }

    /// <summary>
    /// 다음 날짜
    /// </summary>
    private void BtnDateNext_Click(object sender, RoutedEventArgs e)
    {
        if (DatePicker.Date == null) return;
        DatePicker.Date = DatePicker.Date.Value.AddDays(1);
    }

    /// <summary>
    /// 일지 목록 보기
    /// </summary>
    private async void BtnViewDiaryList_Click(object sender, RoutedEventArgs e)
    {
        if (_currentYear == 0 || _currentGrade == 0 || _currentClass == 0)
        {
            await MessageBox.ShowAsync("학년도/학년/반을 먼저 선택해주세요.", "알림");
            return;
        }

        var listWin = new ClassDiaryListWin(
            _currentYear,
            Settings.WorkSemester.Value,
            _currentGrade,
            _currentClass);

        // 일지 선택 시 해당 날짜로 이동
        listWin.DiarySelected += async (s, diary) =>
        {
            // 현재 일지 저장
            await DiaryBox.SaveDiaryAsync();

            // 선택된 날짜로 이동
            DatePicker.Date = diary.Date;
        };

        listWin.SetSize(1400, 800);
        listWin.ShowDialog();
    }

    #endregion
}

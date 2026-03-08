using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Services;
using NewSchool.Controls;
using NewSchool.Models;
using NewSchool.ViewModels;

namespace NewSchool.Pages;

public sealed partial class ClassDiaryPage : Page
{
    private DateTime _currentDate = DateTime.Today;
    private int _currentYear;
    private int _currentGrade;
    private int _currentClass;

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

        // 당일 기록 뷰어: 번호+이름 모드, 전체 카테고리
        DailyLogList.StudentInfoMode = StudentInfoMode.NumName;
        DailyLogList.Category = LogCategory.전체;

        // 학생 목록 컨텍스트 메뉴
        SetupStudentContextMenu();
    }

    /// <summary>
    /// 학생 목록 우클릭 컨텍스트 메뉴 설정
    /// </summary>
    private void SetupStudentContextMenu()
    {
        var menu = new MenuFlyout();

        var miAddLog = new MenuFlyoutItem
        {
            Text = "누가기록 작성",
            Icon = new FontIcon { Glyph = "\uE70F" }  // Edit
        };
        miAddLog.Click += ContextMenu_AddLog_Click;

        var miViewLogs = new MenuFlyoutItem
        {
            Text = "오늘의 기록 보기",
            Icon = new FontIcon { Glyph = "\uE8FD" }  // List
        };
        miViewLogs.Click += ContextMenu_ViewTodayLogs_Click;

        var miViewInfo = new MenuFlyoutItem
        {
            Text = "학생 정보 보기",
            Icon = new FontIcon { Glyph = "\uE77B" }  // Contact
        };
        miViewInfo.Click += ContextMenu_ViewStudentInfo_Click;

        menu.Items.Add(miAddLog);
        menu.Items.Add(miViewLogs);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(miViewInfo);

        StudentList.ItemContextFlyout = menu;
    }

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
    /// 당일 학생 기록 로드
    /// </summary>
    private async Task LoadDailyLogsAsync()
    {
        if (_currentYear == 0 || _currentGrade == 0 || _currentClass == 0)
        {
            DailyLogList.Clear();
            TxtDailyLogCount.Text = "";
            return;
        }

        try
        {
            var logs = await StudentLogService.GetByClassAsync(
                Settings.SchoolCode.Value,
                _currentYear,
                _currentGrade,
                _currentClass,
                _currentDate);

            var viewModels = new List<StudentLogViewModel>();
            foreach (var log in logs)
            {
                var vm = await StudentLogViewModel.CreateAsync(log);
                viewModels.Add(vm);
            }

            DailyLogList.LoadLogs(viewModels);

            // 제목/건수 업데이트
            TxtDailyLogTitle.Text = $"{_currentDate:M월 d일} 학생 기록";
            TxtDailyLogCount.Text = logs.Count > 0 ? $"{logs.Count}건" : "기록 없음";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ClassDiaryPage] 당일 기록 로드 오류: {ex.Message}");
            DailyLogList.Clear();
            TxtDailyLogCount.Text = "로드 실패";
        }
    }

    /// <summary>
    /// 모든 데이터 새로고침
    /// </summary>
    private async Task RefreshAllDataAsync()
    {
        await LoadDiaryAsync();
        await LoadDailyLogsAsync();
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
    /// 학생 기록 추가 (학급별 일괄 입력 다이얼로그)
    /// </summary>
    private async void BtnAddDailyLog_Click(object sender, RoutedEventArgs e)
    {
        if (_currentYear == 0 || _currentGrade == 0 || _currentClass == 0)
        {
            await MessageBox.ShowAsync("학년도/학년/반을 먼저 선택해주세요.", "알림");
            return;
        }

        var logDialog = new Dialogs.StudentLogDialog(
            SchoolDatabase.DbPath,
            LogCategory.기타,
            _currentYear,
            Settings.WorkSemester.Value,
            _currentGrade,
            _currentClass);
        logDialog.Closed += async (s, args) =>
        {
            await LoadDailyLogsAsync();
        };
        logDialog.Activate();
    }

    /// <summary>
    /// 당일 기록 새로고침
    /// </summary>
    private async void BtnRefreshLogs_Click(object sender, RoutedEventArgs e)
    {
        await LoadDailyLogsAsync();
    }

    /// <summary>
    /// 컨텍스트 메뉴: 누가기록 작성
    /// </summary>
    private async void ContextMenu_AddLog_Click(object sender, RoutedEventArgs e)
    {
        var student = StudentList.SelectedStudent;
        if (student == null) return;

        if (_currentYear == 0)
        {
            await MessageBox.ShowAsync("학년도를 먼저 선택해주세요.", "알림");
            return;
        }

        var logDialog = new Dialogs.StudentLogDialog(
            student,
            _currentYear,
            Settings.WorkSemester.Value);
        logDialog.Closed += async (s, args) =>
        {
            await LoadDailyLogsAsync();
        };
        logDialog.Activate();
    }

    /// <summary>
    /// 컨텍스트 메뉴: 오늘의 기록 보기 (해당 학생 기록만 필터)
    /// </summary>
    private async void ContextMenu_ViewTodayLogs_Click(object sender, RoutedEventArgs e)
    {
        var student = StudentList.SelectedStudent;
        if (student == null) return;

        try
        {
            var logs = await StudentLogService.GetByClassAsync(
                Settings.SchoolCode.Value,
                _currentYear,
                _currentGrade,
                _currentClass,
                _currentDate);

            var filtered = logs.Where(l => l.StudentID == student.StudentID).ToList();

            var viewModels = new List<StudentLogViewModel>();
            foreach (var log in filtered)
            {
                var vm = await StudentLogViewModel.CreateAsync(log);
                viewModels.Add(vm);
            }

            DailyLogList.LoadLogs(viewModels);

            TxtDailyLogTitle.Text = $"{_currentDate:M월 d일} {student.Name} 기록";
            TxtDailyLogCount.Text = filtered.Count > 0 ? $"{filtered.Count}건" : "기록 없음";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ClassDiaryPage] 학생 기록 필터 오류: {ex.Message}");
        }
    }

    /// <summary>
    /// 컨텍스트 메뉴: 학생 정보 보기
    /// </summary>
    private async void ContextMenu_ViewStudentInfo_Click(object sender, RoutedEventArgs e)
    {
        var student = StudentList.SelectedStudent;
        if (student == null) return;

        var card = new StudentCard();
        await card.LoadStudentAsync(student.StudentID);

        var dialog = new ContentDialog
        {
            Title = $"{student.Name} — 학생 정보",
            Content = card,
            CloseButtonText = "닫기",
            XamlRoot = this.XamlRoot,
            MinWidth = 700,
            MaxHeight = 600
        };

        await dialog.ShowAsync();
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

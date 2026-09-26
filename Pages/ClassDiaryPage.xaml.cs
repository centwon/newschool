using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Controls;
using NewSchool.Models;
using NewSchool.Services;
using NewSchool.ViewModels;

namespace NewSchool.Pages;

public sealed partial class ClassDiaryPage : Page, NewSchool.Controls.IAsksBeforeLeaving
{
    private DateTime _currentDate = DateTime.Today;
    private int _currentYear;

    /// <summary>
    /// 화면에서 고른 학기.
    ///
    /// <para>⚠ 이 화면의 학년도·학기는 <b>위쪽 피커</b>가 정한다. <c>Settings.WorkYear</c>·
    /// <c>Settings.WorkSemester</c> 를 섞어 쓰지 말 것 — 예전에는 학생 목록·당일 기록만
    /// 피커를 따르고 <b>학급일지와 그 시간표는 설정값</b>을 봐서, 피커로 지난 학년도를 펼쳐
    /// 놓고 쓴 일지가 <b>올해 행에 저장됐다.</b> 그 해의 일지는 열 방법도 없었다.</para>
    /// </summary>
    private int _currentSemester;

    private int _currentGrade;
    private int _currentClass;

    public ClassDiaryPage()
    {
        this.InitializeComponent();
        InitializeControls();
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
    }

    /// <summary>
    /// 학생이 한 명도 없으면 빈 화면 대신 다음 할 일을 띄운다.
    /// </summary>
    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        EmptyState.Visibility = await Helpers.SetupProgress.HasAnyStudentAsync()
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    /// <summary>
    /// 안내판의 [학생 추가하기] — 학생 관리 화면으로 보낸다.
    /// </summary>
    private void EmptyState_ActionInvoked(object sender, EventArgs e)
    {
        MainWindow.NavigateFromPage(this.Frame, typeof(StudentManagementPage), "Settings_Student");
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

        await DiaryBox.LoadDiaryAsync(
            _currentYear, _currentSemester, _currentGrade, _currentClass, _currentDate);
    }

    /// <summary>
    /// 당일 학생 기록 로드
    /// </summary>
    private async Task LoadDailyLogsAsync()
    {
        // 다시 읽기 전에 고친 기록을 묻는다 — 학급·날짜를 바꾸거나 새로고침·기록 추가 뒤 다시
        // 읽는 길이 모두 여기를 지난다.
        await CheckUnSavedLogsAsync();

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

            // 배치 조회로 변환 (기록 건마다 학적·기본정보를 재조회하던 N+1 제거)
            var viewModels = await StudentLogViewModel.CreateManyAsync(logs);

            DailyLogList.LoadLogs(viewModels);

            // 제목/건수 업데이트
            TxtDailyLogTitle.Text = $"{_currentDate:M월 d일} 학생 기록";
            TxtDailyLogCount.Text = logs.Count > 0 ? $"{logs.Count}건" : "기록 없음";
        }
        catch (Exception ex)
        {
            NewSchool.Logging.Log.Error("ClassDiaryPage", "당일 누가기록을 읽지 못했다 — 기록이 없는 것처럼 보인다", ex);
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

    private async void YearSemPicker_YearSemesterChanged(object? sender, YearSemesterChangedEventArgs e)
    {
        await ClassFilter.LoadAsync(e.Year, e.Semester);
    }

    private async void ClassFilter_ClassChanged(object? sender, ClassChangedEventArgs e)
    {
        _currentYear = e.Year;
        _currentSemester = e.Semester;
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
            LogCategory.기타,
            _currentYear,
            _currentSemester,
            _currentGrade,
            _currentClass);
        logDialog.Closed += OnLogDialogClosedReloadDaily;
        logDialog.Activate();
    }

    // 다이얼로그 Closed → 당일 로그 재로드 공용 핸들러 (자기 이벤트 해제)
    private async void OnLogDialogClosedReloadDaily(object sender, Microsoft.UI.Xaml.WindowEventArgs args)
    {
        if (sender is Window w) w.Closed -= OnLogDialogClosedReloadDaily;
        if (!IsLoaded) return;   // 창을 연 채 이 화면을 떠났다 — 닫힌 서비스로 읽지 않는다(PageStudentLog 주석)
        await LoadDailyLogsAsync();
    }

    /// <summary>
    /// 고친 기록 저장.
    ///
    /// <para>예전에는 이 목록의 칸을 고칠 수만 있고 저장할 길이 없어서, 고친 것은 날짜·학급을
    /// 바꾸거나 화면을 떠나는 순간 늘 사라졌다. 학생 정보 화면의 [저장] 과 같은 길
    /// (<see cref="LogListViewer.SaveChangedLogsAsync"/> — 학적 확인 포함)을 쓴다.</para>
    /// </summary>
    private async void BtnSaveDailyLogs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var (attempted, saved) = await DailyLogList.SaveChangedLogsAsync();

            if (attempted == 0)
            {
                await MessageBox.ShowAsync(
                    "저장할 기록이 없습니다.\n수정한 기록의 체크박스를 선택한 뒤 저장하세요.", "저장");
            }
            else if (saved == attempted)
            {
                await MessageBox.ShowAsync($"누가기록 {saved}건이 저장되었습니다.", "저장");
            }
            else
            {
                NewSchool.Logging.Log.Warning("ClassDiaryPage", $"누가기록 저장 일부 실패: {saved}/{attempted}");
                await MessageBox.ShowAsync(
                    $"{attempted}건 중 {saved}건만 저장되었습니다.\n저장되지 않은 기록을 다시 확인해 주세요.", "저장 실패");
            }
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"저장 오류: {ex.Message}", "오류");
        }
    }

    /// <summary>
    /// Ctrl+S — 포커스가 기록 목록 안이면 기록을, 그 밖(일지 칸 등)이면 일지를 저장한다.
    /// 일지는 스스로 저장되지만, 일지를 쓰다 Ctrl+S 를 눌렀는데 "저장할 기록이 없습니다" 가
    /// 뜨면 무엇이 저장되는지 헷갈린다.
    /// </summary>
    private async void SaveAccelerator_Invoked(
        Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
        Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;

        if (IsFocusInside(DailyLogList))
            BtnSaveDailyLogs_Click(BtnSaveDailyLogs, new RoutedEventArgs());
        else
            await DiaryBox.SaveDiaryAsync();
    }

    private bool IsFocusInside(DependencyObject container)
    {
        var node = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        while (node != null)
        {
            if (ReferenceEquals(node, container)) return true;
            node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node);
        }
        return false;
    }

    /// <summary>
    /// 목록을 다시 읽기 전에 고친 채 저장하지 않은 기록을 묻는다 — 누가기록 화면과 같은 한 벌
    /// (<see cref="LogListViewer.AskSaveModifiedAsync"/>). 이 목록은 여러 학생의 기록이 섞여
    /// 있으므로 대상자는 행마다 제 학생을 쓴다.
    /// </summary>
    private async Task CheckUnSavedLogsAsync()
    {
        try
        {
            await DailyLogList.AskSaveModifiedAsync();
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"저장 확인 중 오류가 발생했습니다: {ex.Message}", "오류");
        }
    }

    /// <summary>앱을 닫기 전에 부른다 — 닫을 때는 아래 Unloaded 가 창이 닫힌 뒤라 물을 수 없다.</summary>
    public async Task AskBeforeLeavingAsync()
    {
        await CheckUnSavedLogsAsync();
        DailyLogList.ClearSelection();   // 저장하지 않은 것은 버린 것 — 닫히는 창에서 또 묻지 않게
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _diaryListWin?.Close();

        // 화면을 떠날 때도 누가기록 화면(LogList_Unloaded)처럼 고친 기록을 묻는다.
        _ = CheckUnSavedLogsAsync().ContinueWith(t =>
        {
            if (t.IsFaulted)
                System.Diagnostics.Debug.WriteLine($"[ClassDiaryPage] {t.Exception?.InnerException?.Message}");
        }, TaskContinuationOptions.OnlyOnFaulted);
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
            _currentSemester);
        logDialog.Closed += OnLogDialogClosedReloadDaily;
        logDialog.Activate();
    }

    /// <summary>
    /// 컨텍스트 메뉴: 오늘의 기록 보기 (해당 학생 기록만 필터)
    /// </summary>
    private async void ContextMenu_ViewTodayLogs_Click(object sender, RoutedEventArgs e)
    {
        var student = StudentList.SelectedStudent;
        if (student == null) return;

        await CheckUnSavedLogsAsync();

        try
        {
            var logs = await StudentLogService.GetByClassAsync(
                Settings.SchoolCode.Value,
                _currentYear,
                _currentGrade,
                _currentClass,
                _currentDate);

            var filtered = logs.Where(l => l.StudentID == student.StudentID).ToList();

            // 배치 조회로 변환 (기록 건마다 학적·기본정보를 재조회하던 N+1 제거)
            var viewModels = await StudentLogViewModel.CreateManyAsync(filtered);

            DailyLogList.LoadLogs(viewModels);

            TxtDailyLogTitle.Text = $"{_currentDate:M월 d일} {student.Name} 기록";
            TxtDailyLogCount.Text = filtered.Count > 0 ? $"{filtered.Count}건" : "기록 없음";
        }
        catch (Exception ex)
        {
            NewSchool.Logging.Log.Warning("ClassDiaryPage", $"학생 기록으로 걸러 내지 못했다: {ex.Message}");
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

        await MessageBox.ShowDialogAsync(dialog);
    }

    /// <summary>
    /// 일지 목록 보기
    /// </summary>
    /// <summary>
    /// 열어 둔 일지 목록 창. 이 화면이 내려갈 때 함께 닫는다 — 독립 창이라 그냥 두면 화면을
    /// 떠나거나 앱을 닫아도 혼자 남았고, 거기서 일지를 고르면 이미 치운 일지 칸을 건드렸다.
    /// </summary>
    private ClassDiaryListWin? _diaryListWin;

    private async void BtnViewDiaryList_Click(object sender, RoutedEventArgs e)
    {
        if (_currentYear == 0 || _currentGrade == 0 || _currentClass == 0)
        {
            await MessageBox.ShowAsync("학년도/학년/반을 먼저 선택해주세요.", "알림");
            return;
        }

        var listWin = new ClassDiaryListWin(
            _currentYear,
            _currentSemester,
            _currentGrade,
            _currentClass);

        // 일지 선택 시 해당 날짜로 이동 — 람다 대신 named method 로 구독
        listWin.DiarySelected += OnDiarySelected;
        listWin.Closed += (s, e) =>
        {
            listWin.DiarySelected -= OnDiarySelected;
            if (ReferenceEquals(_diaryListWin, listWin)) _diaryListWin = null;
        };
        _diaryListWin?.Close();   // 하나만 — 두 번 누르면 새것으로 바꾼다
        _diaryListWin = listWin;

        // 목록 한 줄은 날짜(140) + 내용 + 아이콘(40)뿐이라 1400 은 지나치게 넓었다.
        // 같은 화면에서 여는 일지 편집 창(ClassDiaryBox)과 크기를 맞춘다.
        listWin.SetSize(1000, 800);
        listWin.ShowDialog();
    }

    private async void OnDiarySelected(object? sender, ClassDiaryViewModel diary)
    {
        // 현재 일지 저장 후 선택된 날짜로 이동
        await DiaryBox.SaveDiaryAsync();
        DatePicker.Date = diary.Date;
    }

    #endregion
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using NewSchool.Dialogs;
using NewSchool.ViewModels;
using NewSchool.Models;
using NewSchool.Services;
using System.Threading.Tasks;

namespace NewSchool.Controls;

/// <summary>
/// 학생 기록 목록 뷰어 (WinUI3)
/// WPF LogListViewer를 WinUI3로 전환
/// 
/// 주요 기능:
/// 1. StudentLog 목록 표시
/// 2. 체크박스 다중 선택
/// 3. 편집 가능 (일시, 주제, 기록 내용)
/// 4. 학생 정보 표시 모드 전환
/// 5. 카테고리별 컬럼 조정
/// </summary>
public sealed partial class LogListViewer : UserControl
{
    #region Fields

    private StudentInfoMode _studentInfoMode = StudentInfoMode.NameOnly;
    private LogCategory _category = LogCategory.전체;
    private StudentLogViewModel? _focusedLog;
    private Border? _focusedBorder;
    private bool _isLoading;
    private double _contentFontSize = StudentLogViewModel.DefaultContentFontSize;

    #endregion

    #region Properties

    /// <summary>학생 기록 목록</summary>
    public ObservableCollection<StudentLogViewModel> Logs { get; } = new();

    /// <summary>학생 정보 표시 모드</summary>
    public StudentInfoMode StudentInfoMode
    {
        get => _studentInfoMode;
        set
        {
            _studentInfoMode = value;
            ApplyStudentInfoMode();
        }
    }

    /// <summary>카테고리 (영역별 컬럼 조정용)</summary>
    public LogCategory Category
    {
        get => _category;
        set
        {
            _category = value;
            ApplyCategoryMode();
        }
    }

    /// <summary>
    /// 기록 내용 칸의 글자 크기(툴바 "글자 크기" 슬라이더가 쓴다). 기본 12.
    ///
    /// ⚠ <c>FontSize</c> 를 대신 쓰면 안 된다. 행의 각 칸에 <c>FontSize="12"</c> 가 명시돼 있어
    /// 상속값이 무시되고, 크기가 명시되지 않은 <b>헤더 라벨만</b> 커진다 — 즉 정작 읽으려는
    /// 기록은 그대로이고 엉뚱한 곳이 커졌다(2026-07-30 수정). 값은 각 행 ViewModel 로 내려보내
    /// 기록 내용 칸이 직접 바인딩한다. 나중에 목록에 추가되는 행에도 적용되도록
    /// 컬렉션 변경도 지켜본다.
    /// </summary>
    public double ContentFontSize
    {
        get => _contentFontSize;
        set
        {
            if (_contentFontSize == value) return;
            _contentFontSize = value;
            ApplyContentFontSize();
        }
    }

    /// <summary>
    /// 글자 크기를 각 행 ViewModel 로 내려보낸다.
    ///
    /// <para><paramref name="added"/> 가 주어지면 그 행들만 손댄다. 예전에는 컬렉션이 바뀔 때마다
    /// 목록 전체를 다시 돌았는데, 행을 하나씩 <c>Add</c> 로 채우는 화면에서는 N 번째 추가마다
    /// N 개를 훑어 O(N²) 이 됐다(기록 200건이면 대입 2만 번).</para>
    /// </summary>
    private void ApplyContentFontSize(System.Collections.IList? added = null)
    {
        if (added != null)
        {
            foreach (var item in added)
            {
                if (item is StudentLogViewModel vm) vm.ContentFontSize = _contentFontSize;
            }
            return;
        }

        foreach (var item in Logs)
            item.ContentFontSize = _contentFontSize;
    }

    /// <summary>선택된 로그 목록</summary>
    public ObservableCollection<StudentLogViewModel> SelectedLogs
    {
        get
        {
            var selected = new ObservableCollection<StudentLogViewModel>();
            foreach (var log in Logs.Where(l => l.IsSelected))
            {
                selected.Add(log);
            }
            return selected;
        }
    }

    // SelectedCount 는 쓰는 곳이 없어 지웠다(39차) — 선택 결과는 SelectedLogs 로 받아 센다.

    /// <summary>로그 편집 후 변경됨 이벤트 (외부에서 목록 새로고침용)</summary>
    public event EventHandler<StudentLog>? LogEdited;

    #endregion

    #region Constructor

    public LogListViewer()
    {
        this.InitializeComponent();

        // DataContext 설정 (바인딩용)
        this.DataContext = this;

        // ItemsSource 바인딩
        LogItemsRepeater.ItemsSource = Logs;

        // 나중에 추가되는 행에도 현재 글자 크기가 적용되도록
        // 추가된 행만 손댄다 — Reset(전체 교체)이면 e.NewItems 가 없으므로 전체를 다시 훑는다.
        Logs.CollectionChanged += (_, e) => ApplyContentFontSize(e.NewItems);

        // 초기 모드 적용
        ApplyStudentInfoMode();
        ApplyCategoryMode();
    }

    #endregion

    #region Student Info Mode Management

    /// <summary>학생 정보 표시 모드 적용</summary>
    private void ApplyStudentInfoMode()
    {
        switch (_studentInfoMode)
        {
            case StudentInfoMode.HideAll:
                // 학생 정보 모두 숨김 (개인별 보기)
                ColYearHeader.Width = new GridLength(0);
                ColSemesterHeader.Width = new GridLength(0);
                ColGradeHeader.Width = new GridLength(0);
                ColClassHeader.Width = new GridLength(0);
                ColNumberHeader.Width = new GridLength(0);
                ColNameHeader.Width = new GridLength(0);

                TxtYearHeader.Visibility = Visibility.Collapsed;
                TxtSemesterHeader.Visibility = Visibility.Collapsed;
                TxtGradeHeader.Visibility = Visibility.Collapsed;
                TxtClassHeader.Visibility = Visibility.Collapsed;
                TxtNumberHeader.Visibility = Visibility.Collapsed;
                TxtNameHeader.Visibility = Visibility.Collapsed;
                break;

            case StudentInfoMode.ShowAll:
                // 모두 표시
                ColYearHeader.Width = new GridLength(60);
                ColSemesterHeader.Width = new GridLength(50);
                ColGradeHeader.Width = new GridLength(50);
                ColClassHeader.Width = new GridLength(50);
                ColNumberHeader.Width = new GridLength(50);
                ColNameHeader.Width = new GridLength(70);

                TxtYearHeader.Visibility = Visibility.Visible;
                TxtSemesterHeader.Visibility = Visibility.Visible;
                TxtGradeHeader.Visibility = Visibility.Visible;
                TxtClassHeader.Visibility = Visibility.Visible;
                TxtNumberHeader.Visibility = Visibility.Visible;
                TxtNameHeader.Visibility = Visibility.Visible;
                break;

            case StudentInfoMode.GradeClassNumName:
                // 학년, 반, 번호, 이름
                ColYearHeader.Width = new GridLength(0);
                ColSemesterHeader.Width = new GridLength(0);
                ColGradeHeader.Width = new GridLength(50);
                ColClassHeader.Width = new GridLength(50);
                ColNumberHeader.Width = new GridLength(50);
                ColNameHeader.Width = new GridLength(70);

                TxtYearHeader.Visibility = Visibility.Collapsed;
                TxtSemesterHeader.Visibility = Visibility.Collapsed;
                TxtGradeHeader.Visibility = Visibility.Visible;
                TxtClassHeader.Visibility = Visibility.Visible;
                TxtNumberHeader.Visibility = Visibility.Visible;
                TxtNameHeader.Visibility = Visibility.Visible;
                break;

            case StudentInfoMode.ClassNumName:
                // 반, 번호, 이름
                ColYearHeader.Width = new GridLength(0);
                ColSemesterHeader.Width = new GridLength(0);
                ColGradeHeader.Width = new GridLength(0);
                ColClassHeader.Width = new GridLength(50);
                ColNumberHeader.Width = new GridLength(50);
                ColNameHeader.Width = new GridLength(70);

                TxtYearHeader.Visibility = Visibility.Collapsed;
                TxtSemesterHeader.Visibility = Visibility.Collapsed;
                TxtGradeHeader.Visibility = Visibility.Collapsed;
                TxtClassHeader.Visibility = Visibility.Visible;
                TxtNumberHeader.Visibility = Visibility.Visible;
                TxtNameHeader.Visibility = Visibility.Visible;
                break;

            case StudentInfoMode.NumName:
                // 번호, 이름
                ColYearHeader.Width = new GridLength(0);
                ColSemesterHeader.Width = new GridLength(0);
                ColGradeHeader.Width = new GridLength(0);
                ColClassHeader.Width = new GridLength(0);
                ColNumberHeader.Width = new GridLength(50);
                ColNameHeader.Width = new GridLength(70);

                TxtYearHeader.Visibility = Visibility.Collapsed;
                TxtSemesterHeader.Visibility = Visibility.Collapsed;
                TxtGradeHeader.Visibility = Visibility.Collapsed;
                TxtClassHeader.Visibility = Visibility.Collapsed;
                TxtNumberHeader.Visibility = Visibility.Visible;
                TxtNameHeader.Visibility = Visibility.Visible;
                break;

            case StudentInfoMode.NameOnly:
                // 이름만
                ColYearHeader.Width = new GridLength(0);
                ColSemesterHeader.Width = new GridLength(0);
                ColGradeHeader.Width = new GridLength(0);
                ColClassHeader.Width = new GridLength(0);
                ColNumberHeader.Width = new GridLength(0);
                ColNameHeader.Width = new GridLength(70);

                TxtYearHeader.Visibility = Visibility.Collapsed;
                TxtSemesterHeader.Visibility = Visibility.Collapsed;
                TxtGradeHeader.Visibility = Visibility.Collapsed;
                TxtClassHeader.Visibility = Visibility.Collapsed;
                TxtNumberHeader.Visibility = Visibility.Collapsed;
                TxtNameHeader.Visibility = Visibility.Visible;
                break;
        }

        // ItemsRepeater의 각 항목 업데이트
        UpdateDataRowColumns();
        SyncColumnVisibilityToViewModels();

        HeaderGrid.InvalidateMeasure();
        LogItemsRepeater.InvalidateMeasure();
        this.UpdateLayout();
    }

    #endregion

    #region Category Mode Management

    /// <summary>카테고리별 컬럼 조정</summary>
    private void ApplyCategoryMode()
    {
        switch (_category)
        {
            case LogCategory.전체:
                // 모든 컬럼 표시
                ColCategoryHeader.Width = new GridLength(80);
                ColSubjectHeader.Width = new GridLength(80);
                TxtCategoryHeader.Visibility = Visibility.Visible;
                TxtSubjectHeader.Text = "세부영역";
                TxtClassHeader.Text = "소속";
                break;

            case LogCategory.교과활동:
                // 영역 숨김, 과목 표시
                ColCategoryHeader.Width = new GridLength(0);
                ColSubjectHeader.Width = new GridLength(80);
                TxtCategoryHeader.Visibility = Visibility.Collapsed;
                TxtSubjectHeader.Text = "과목";
                TxtClassHeader.Text = "강의실";
                break;

            case LogCategory.동아리활동:
                // 영역 숨김, 동아리 표시
                ColCategoryHeader.Width = new GridLength(0);
                ColSubjectHeader.Width = new GridLength(80);
                TxtCategoryHeader.Visibility = Visibility.Collapsed;
                TxtSubjectHeader.Text = "동아리";
                TxtClassHeader.Text = "강의실";
                break;

            case LogCategory.개인별세특:
            case LogCategory.봉사활동:
            case LogCategory.상담기록:
            case LogCategory.자율활동:
            case LogCategory.진로활동:
            case LogCategory.종합의견:
                // 과목 숨김
                ColCategoryHeader.Width = new GridLength(0);
                ColSubjectHeader.Width = new GridLength(0);
                TxtCategoryHeader.Visibility = Visibility.Collapsed;
                TxtClassHeader.Text = "학급";
                break;

            case LogCategory.기타:
                // 영역, 과목 모두 숨김
                ColCategoryHeader.Width = new GridLength(0);
                ColSubjectHeader.Width = new GridLength(0);
                TxtCategoryHeader.Visibility = Visibility.Collapsed;
                break;
        }

        // ItemsRepeater의 각 항목 업데이트
        UpdateDataRowColumns();
        SyncColumnVisibilityToViewModels();

        HeaderGrid.InvalidateMeasure();
        LogItemsRepeater.InvalidateMeasure();
        this.UpdateLayout();
    }

    /// <summary>
    /// 실제 데이터에 따라 빈 칼럼 숨김
    /// </summary>
    private void AdjustColumnsToData()
    {
        if (Logs.Count == 0) return;

        // 세부영역(SubjectName)이 하나도 없으면 칼럼 숨김
        bool hasSubject = Logs.Any(l => !string.IsNullOrWhiteSpace(l.SubjectName));
        if (!hasSubject)
        {
            ColSubjectHeader.Width = new GridLength(0);
        }

        // 전체 모드에서 카테고리가 모두 동일하면 영역 칼럼 숨김
        if (_category == LogCategory.전체 && Logs.Select(l => l.Category).Distinct().Count() == 1)
        {
            ColCategoryHeader.Width = new GridLength(0);
            TxtCategoryHeader.Visibility = Visibility.Collapsed;
        }

        UpdateDataRowColumns();
        SyncColumnVisibilityToViewModels();
    }

    /// <summary>
    /// 데이터 행 Grid Loaded 이벤트 - 열 너비를 헤더와 동기화
    /// </summary>
    private void OnDataRowGridLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Grid grid && grid.ColumnDefinitions.Count >= 9)
        {
            grid.ColumnDefinitions[1].Width = ColYearHeader.Width;
            grid.ColumnDefinitions[2].Width = ColSemesterHeader.Width;
            grid.ColumnDefinitions[3].Width = ColCategoryHeader.Width;
            grid.ColumnDefinitions[4].Width = ColSubjectHeader.Width;
            grid.ColumnDefinitions[5].Width = ColGradeHeader.Width;
            grid.ColumnDefinitions[6].Width = ColClassHeader.Width;
            grid.ColumnDefinitions[7].Width = ColNumberHeader.Width;
            grid.ColumnDefinitions[8].Width = ColNameHeader.Width;
        }
    }

    /// <summary>
    /// 헤더 Visibility를 각 ViewModel 항목에 동기화 (x:Bind용)
    /// </summary>
    private void SyncColumnVisibilityToViewModels()
    {
        foreach (var log in Logs)
        {
            log.YearColumnVisibility = TxtYearHeader.Visibility;
            log.SemesterColumnVisibility = TxtSemesterHeader.Visibility;
            log.CategoryColumnVisibility = TxtCategoryHeader.Visibility;
            log.SubjectColumnVisibility = TxtSubjectHeader.Visibility;
            log.GradeColumnVisibility = TxtGradeHeader.Visibility;
            log.ClassColumnVisibility = TxtClassHeader.Visibility;
            log.NumberColumnVisibility = TxtNumberHeader.Visibility;
            log.NameColumnVisibility = TxtNameHeader.Visibility;
        }
    }

    /// <summary>
    /// ItemsRepeater의 각 데이터 행 컬럼을 헤더와 동기화
    /// </summary>
    private void UpdateDataRowColumns()
    {
        // ItemsRepeater가 렌더링된 후에 각 항목을 찾아서 업데이트
        this.DispatcherQueue.TryEnqueue(() =>
        {
            for (int i = 0; i < Logs.Count; i++)
            {
                var element = LogItemsRepeater.TryGetElement(i);
                var grid = FindDataRowGrid(element);
                if (grid != null && grid.ColumnDefinitions.Count >= 9)
                {
                    grid.ColumnDefinitions[1].Width = ColYearHeader.Width;
                    grid.ColumnDefinitions[2].Width = ColSemesterHeader.Width;
                    grid.ColumnDefinitions[3].Width = ColCategoryHeader.Width;
                    grid.ColumnDefinitions[4].Width = ColSubjectHeader.Width;
                    grid.ColumnDefinitions[5].Width = ColGradeHeader.Width;
                    grid.ColumnDefinitions[6].Width = ColClassHeader.Width;
                    grid.ColumnDefinitions[7].Width = ColNumberHeader.Width;
                    grid.ColumnDefinitions[8].Width = ColNameHeader.Width;
                }
            }
        });
    }

    #endregion

    #region Selection Management

    /// <summary>전체 선택</summary>
    private void OnSelectAllChecked(object sender, RoutedEventArgs e)
    {
        foreach (var log in Logs)
        {
            log.IsSelected = true;
        }
    }

    /// <summary>전체 선택 해제</summary>
    private void OnSelectAllUnchecked(object sender, RoutedEventArgs e)
    {
        foreach (var log in Logs)
        {
            log.IsSelected = false;
        }
    }

    #endregion

    #region Row Selection

    /// <summary>행 클릭 시 포커스 선택</summary>
    private void OnRowTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (sender is Border border && border.Tag is StudentLogViewModel log)
        {
            SetFocusedRow(border, log);
        }
    }

    private void SetFocusedRow(Border rowBorder, StudentLogViewModel log)
    {
        // 이전 포커스 해제
        if (_focusedBorder != null)
        {
            var prevIndicator = FindSelectionIndicator(_focusedBorder);
            if (prevIndicator != null)
                prevIndicator.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }

        _focusedLog = log;
        _focusedBorder = rowBorder;

        // 선택 행 하이라이트 (왼쪽 악센트 바)
        var indicator = FindSelectionIndicator(rowBorder);
        if (indicator != null)
            indicator.Background = (Microsoft.UI.Xaml.Media.Brush)
                Microsoft.UI.Xaml.Application.Current.Resources["AccentFillColorDefaultBrush"];
    }

    private static Border? FindSelectionIndicator(Border rowBorder)
    {
        if (rowBorder.Child is Grid wrapperGrid && wrapperGrid.Children.Count > 0
            && wrapperGrid.Children[0] is Border indicator && indicator.Name == "SelectionIndicator")
        {
            return indicator;
        }
        return null;
    }

    /// <summary>Border → 래퍼Grid → DataRowGrid 탐색</summary>
    private static Grid? FindDataRowGrid(UIElement? element)
    {
        if (element is Border border && border.Child is Grid wrapperGrid
            && wrapperGrid.Children.Count > 1 && wrapperGrid.Children[1] is Grid dataRowGrid)
        {
            return dataRowGrid;
        }
        return null;
    }

    #endregion

    #region Edit Button

    /// <summary>
    /// 포커스된 행 또는 체크된 로그 1건을 StudentLogDialog로 전체 편집 (외부 호출용)
    /// </summary>
    public async void EditSelectedLog()
    {
        // 포커스된 행 우선 사용
        var vm = _focusedLog;

        // 포커스된 행이 없으면 체크된 항목 사용
        if (vm == null)
        {
            var selected = Logs.Where(l => l.IsSelected).ToList();

            if (selected.Count == 0)
            {
                await MessageBox.ShowAsync("편집할 기록을 선택해주세요.", "선택 필요");
                return;
            }

            if (selected.Count > 1)
            {
                await MessageBox.ShowAsync("전체 편집은 1건만 선택해주세요.", "단일 선택");
                return;
            }

            vm = selected[0];
        }

        var log = vm.StudentLog;
        if (log == null) return;

        var dialog = new StudentLogDialog(log);
        var capturedVm = vm;
        // 람다 대신 지역 named function 을 써 핸들러 체인에서 자기 제거
        void OnEditDialogClosed(object s, Microsoft.UI.Xaml.WindowEventArgs args)
        {
            dialog.Closed -= OnEditDialogClosed;
            if (dialog.IsSuccess && dialog.SavedLogs.Count > 0)
            {
                var saved = dialog.SavedLogs[0];
                capturedVm.RefreshFromLog();
                capturedVm.IsSelected = false;
                LogEdited?.Invoke(this, saved);
            }
        }
        dialog.Closed += OnEditDialogClosed;
        dialog.Activate();
    }


    #endregion

    #region Text Change Handlers

    /// <summary>주제 변경 시 자동 선택 (사용자 입력 시에만)</summary>
    private void OnTopicChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading) return;
        if (sender is TextBox textBox && textBox.FocusState != FocusState.Unfocused
            && textBox.Tag is StudentLogViewModel log)
        {
            log.IsSelected = true;
        }
    }

    /// <summary>기록 내용 변경 시 자동 선택 (사용자 입력 시에만)</summary>
    private void OnLogChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading) return;
        if (sender is TextBox textBox && textBox.FocusState != FocusState.Unfocused
            && textBox.Tag is StudentLogViewModel log)
        {
            log.IsSelected = true;
        }
    }

    #endregion

    #region Public Methods

    /// <summary>로그 목록 로드</summary>
    public void LoadLogs(System.Collections.Generic.IEnumerable<StudentLogViewModel> logs)
    {
        System.Diagnostics.Debug.WriteLine($"[LogListViewer] LoadLogs 시작");
        _isLoading = true;
        Logs.Clear();
        int count = 0;
        foreach (var log in logs)
        {
            Logs.Add(log);
            count++;
        }
        _isLoading = false;
        System.Diagnostics.Debug.WriteLine($"[LogListViewer] LoadLogs 완료: {count}건 추가됨, Logs.Count={Logs.Count}");
        SyncColumnVisibilityToViewModels();
        AdjustColumnsToData();
    }

    /// <summary>모든 선택 해제</summary>
    public void ClearSelection()
    {
        foreach (var log in Logs)
        {
            log.IsSelected = false;
        }
        ChkSelectAll.IsChecked = false;
    }

    /// <summary>로그 추가</summary>
    public async Task AddLog(StudentLog log)
    {
        var viewModel = await StudentLogViewModel.CreateAsync(log);
        Logs.Insert(0, viewModel);
    }

    /// <summary>모든 로그 초기화</summary>
    public void Clear()
    {
        Logs.Clear();
    }

    /// <summary>
    /// 저장 대상 로그를 저장한다.
    /// 체크된 항목뿐 아니라 아직 DB 에 없는 신규 항목(No &lt;= 0)도 포함한다
    /// — 신규 항목은 기본적으로 체크가 안 된 상태라 예전에는 조용히 사라졌다.
    /// </summary>
    /// <returns>(저장 시도 건수, 실제 반영 건수)</returns>
    public async System.Threading.Tasks.Task<(int Attempted, int Saved)> SaveChangedLogsAsync()
    {
        var targets = Logs.Where(l => l.IsSelected || l.No <= 0).ToList();
        if (targets.Count == 0)
            return (0, 0);

        // 학교를 떠난 학생에게 그 뒤 날짜로 기록을 남기려는 것이면 알린다.
        // 막지는 않는다 — 전출일을 뒤늦게 넣은 경우 이미 적어 둔 기록을 못 고치게 된다.
        // 이 컨트롤을 쓰는 화면 다섯 곳이 한꺼번에 이 검사를 받는다.
        if (!await ConfirmRecordsAfterLeavingAsync(targets))
            return (0, 0);

        using var logService = new Services.StudentLogService();
        int saved = 0;

        foreach (var log in targets)
        {
            if (log.No > 0)
            {
                // 기존 로그 업데이트
                if (await logService.UpdateAsync(log.StudentLog))
                    saved++;
            }
            else
            {
                // 새 로그 삽입
                var no = await logService.InsertAsync(log.StudentLog);
                if (no > 0)
                {
                    log.No = no;
                    saved++;
                }
            }
            log.IsSelected = false;
        }

        return (targets.Count, saved);
    }

    /// <summary>
    /// 저장하려는 기록 중 <b>학교를 떠난 학생</b>의 것이 있으면 물어본다.
    ///
    /// <para>학생마다 따로 묻지 않고 한 번에 모아 묻는다 — 반 전체를 저장할 때 대화상자가
    /// 연달아 뜨면 사람이 읽지 않고 넘긴다.</para>
    /// </summary>
    /// <returns>계속 저장해도 되면 true, 사용자가 취소했으면 false.</returns>
    private static async System.Threading.Tasks.Task<bool> ConfirmRecordsAfterLeavingAsync(
        System.Collections.Generic.List<StudentLogViewModel> targets)
    {
        var notices = new System.Collections.Generic.List<string>();
        var asked = new System.Collections.Generic.HashSet<string>();

        foreach (var log in targets)
        {
            var studentId = log.StudentLog.StudentID;
            if (string.IsNullOrWhiteSpace(studentId) || !asked.Add(studentId)) continue;

            var notice = await Services.EnrollmentGuard.DescribeRecordAfterLeavingAsync(
                studentId, log.StudentLog.Year, log.StudentLog.Date);

            if (notice != null) notices.Add(notice);
        }

        if (notices.Count == 0) return true;

        return await MessageBox.ShowConfirmAsync(
            string.Join("\n\n", notices), "학적 확인", "계속", "취소");
    }

    /// <summary>선택된 로그 삭제</summary>
    /// <returns>(삭제 시도 건수, 실제 삭제 건수)</returns>
    public async System.Threading.Tasks.Task<(int Attempted, int Deleted)> DeleteSelectedLogsAsync()
    {
        var logsToDelete = Logs.Where(l => l.IsSelected).ToList();
        if (logsToDelete.Count == 0)
            return (0, 0);

        using var logService = new Services.StudentLogService();
        int deleted = 0;

        foreach (var logVm in logsToDelete)
        {
            if (logVm.No > 0)
            {
                // DB 삭제가 실패하면 목록에서도 지우지 않는다(표시/DB 불일치 방지)
                if (!await logService.DeleteAsync(logVm.No))
                    continue;
            }

            Logs.Remove(logVm);
            deleted++;
        }

        return (logsToDelete.Count, deleted);
    }

    #endregion
}

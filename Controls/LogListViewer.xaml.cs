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

    /// <summary>선택된 로그 수</summary>
    public int SelectedCount => Logs.Count(l => l.IsSelected);

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

        HeaderGrid.InvalidateMeasure();
        LogItemsRepeater.InvalidateMeasure();
        this.UpdateLayout();
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
                if (element is Border border && border.Child is Grid grid)
                {
                    if (grid.ColumnDefinitions.Count >= 9)
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

    #region Edit Button

    /// <summary>
    /// 체크된 로그 1건을 StudentLogDialog로 전체 편집 (외부 호출용)
    /// </summary>
    public async void EditSelectedLog()
    {
        var selected = Logs.Where(l => l.IsSelected).ToList();

        if (selected.Count == 0)
        {
            await MessageBox.ShowAsync("편집할 기록을 체크해주세요.", "선택 필요");
            return;
        }

        if (selected.Count > 1)
        {
            await MessageBox.ShowAsync("전체 편집은 1건만 선택해주세요.", "단일 선택");
            return;
        }

        var vm = selected[0];
        var log = vm.StudentLog;
        if (log == null) return;

        var dialog = new StudentLogDialog(log);
        dialog.Closed += (s, args) =>
        {
            if (dialog.IsSuccess && dialog.SavedLogs.Count > 0)
            {
                var saved = dialog.SavedLogs[0];
                vm.RefreshFromLog();
                vm.IsSelected = false;
                LogEdited?.Invoke(this, saved);
            }
        };
        dialog.Activate();
    }


    #endregion

    #region Text Change Handlers

    /// <summary>주제 변경 시 자동 선택</summary>
    private void OnTopicChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox textBox && textBox.DataContext is StudentLogViewModel log)
        {
            log.IsSelected = true;
        }
    }

    /// <summary>기록 내용 변경 시 자동 선택 + 바이트 계산</summary>
    private void OnLogChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox textBox && textBox.DataContext is StudentLogViewModel log)
        {
            log.IsSelected = true;
            // LogByteInfo는 Log 속성 변경 시 자동 업데이트됨 (ViewModel에서 처리)
        }
    }

    #endregion

    #region Public Methods

    /// <summary>로그 목록 로드</summary>
    public void LoadLogs(System.Collections.Generic.IEnumerable<StudentLogViewModel> logs)
    {
        System.Diagnostics.Debug.WriteLine($"[LogListViewer] LoadLogs 시작");
        Logs.Clear();
        int count = 0;
        foreach (var log in logs)
        {
            Logs.Add(log);
            count++;
        }
        System.Diagnostics.Debug.WriteLine($"[LogListViewer] LoadLogs 완료: {count}건 추가됨, Logs.Count={Logs.Count}");
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

    /// <summary>변경된 로그 저장</summary>
    public async System.Threading.Tasks.Task SaveChangedLogsAsync()
    {
        var logService = new Services.StudentLogService();
        
        foreach (var log in Logs.Where(l => l.IsSelected))
        {
            if (log.No > 0)
            {
                // 기존 로그 업데이트
                await logService.UpdateAsync(log.StudentLog);
            }
            else
            {
                // 새 로그 삽입
                var no = await logService.InsertAsync(log.StudentLog);
                log.No = no;
            }
            log.IsSelected = false;
        }
        
        logService.Dispose();
    }

    /// <summary>선택된 로그 삭제</summary>
    public async System.Threading.Tasks.Task DeleteSelectedLogsAsync()
    {
        var logService = new Services.StudentLogService();
        var logsToDelete = Logs.Where(l => l.IsSelected).ToList();
        
        foreach (var logVm in logsToDelete)
        {
            if (logVm.No > 0)
            {
                await logService.DeleteAsync(logVm.No);
            }
            Logs.Remove(logVm);
        }
        
        logService.Dispose();
    }

    #endregion
}

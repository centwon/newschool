using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Models;
using NewSchool.ViewModels;
using NewSchool.Repositories;
using Windows.Storage;
using Windows.Storage.Pickers;
using MiniExcelLibs;
using NewSchool.Services;
using NewSchool.Controls;


namespace NewSchool.Pages;

/// <summary>
/// 학사일정 관리 페이지 (WinUI3)
/// NEIS API 동기화 + 수동 입력 + CRUD
/// 
/// 주요 기능:
/// 1. NEIS API에서 학사일정 가져오기 (동기화)
/// 2. 학년도별/날짜 범위별 조회
/// 3. 수동 일정 추가
/// 4. 체크박스로 선택 후 일괄 저장/삭제
/// 5. Excel 내보내기
/// </summary>
public sealed partial class SchoolScheduleManagementPage : Page
{
    //private readonly SchoolScheduleRepository _repository;
    private readonly SchoolScheduleService _scheduleservice;
    private readonly ObservableCollection<SchoolScheduleViewModel> _schedules = new();
    private bool _isInitialized = false;

    public SchoolScheduleManagementPage()
    {
        this.InitializeComponent();

        // Services 초기화
        _scheduleservice = new SchoolScheduleService(SchoolDatabase.DbPath);
        // Repository 초기화
        //_repository = new SchoolScheduleRepository(SchoolDatabase.DbPath);

        // ItemsSource 바인딩
        ScheduleList.ItemsSource = _schedules;

        this.Loaded += OnPageLoaded;
    }

    #region 초기화

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized)
        {
            InitializeFilters();
            _isInitialized = true;
        }
    }

    /// <summary>
    /// 필터 초기화 (학년도 콤보박스)
    /// </summary>
    private void InitializeFilters()
    {
        // 학년도 콤보박스 (현재년도 ±3년)
        var currentYear = DateTime.Today.Year;
        var years = Enumerable.Range(currentYear - 3, 7).Reverse().ToList();

        CBoxYear.ItemsSource = years;
        CBoxYear.SelectedItem = Settings.WorkYear.Value > 0 
            ? Settings.WorkYear.Value 
            : currentYear;

        // 날짜 범위 기본값 (해당 학년도 전체)
        var selectedYear = (int)CBoxYear.SelectedItem;
        StartDatePicker.Date = new DateTimeOffset(new DateTime(selectedYear, 3, 1));
        EndDatePicker.Date = new DateTimeOffset(new DateTime(selectedYear + 1, 2, 28));
    }

    #endregion

    #region 조회

    /// <summary>
    /// 조회 버튼 클릭
    /// </summary>
    private async void OnSearchClick(object sender, RoutedEventArgs e)
    {
        await LoadSchedulesAsync();
    }

    /// <summary>
    /// 학사일정 로드
    /// </summary>
    private async Task LoadSchedulesAsync()
    {
        if (CBoxYear.SelectedItem == null)
        {
            await MessageBox.ShowAsync("학년도를 선택하세요.", "오류");
            return;
        }

        try
        {
            LoadingRing.IsActive = true;
            TxtStatus.Text = "조회 중...";
            _schedules.Clear();

            var year = (int)CBoxYear.SelectedItem;
            List<SchoolSchedule> schedules;

            // 날짜 범위가 설정되어 있으면 범위로 조회
            if (StartDatePicker.Date != null && EndDatePicker.Date != null)
            {
                var startDate = StartDatePicker.Date.Value.DateTime;
                var endDate = EndDatePicker.Date.Value.DateTime;
                var value = await _scheduleservice.GetSchedulesByDataRangeAsync(Settings.SchoolCode, startDate, endDate);
                schedules = value.Schedules;
            }
            else
            {
                // 학년도 전체 조회
                var value = await _scheduleservice.GetSchedulesBySchoolYearAsync(Settings.SchoolCode, year);
                schedules = value.Schedules;
            }

            // ViewModel로 변환
            foreach (var schedule in schedules)
            {
                _schedules.Add(new SchoolScheduleViewModel(schedule));
            }

            // UI 바인딩 완료 후 초기화 완료 호출 (Dispatcher 사용)
            DispatcherQueue.TryEnqueue(() =>
            {
                foreach (var vm in _schedules)
                {
                    vm.CompleteInitialization();
                }
            });

            UpdateUI();
            TxtStatus.Text = $"조회 완료: {_schedules.Count}개";
            TxtLastUpdate.Text = $"마지막 조회: {DateTime.Now:yyyy-MM-dd HH:mm}";

            Debug.WriteLine($"[SchoolSchedule] 조회 완료: {_schedules.Count}개");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SchoolSchedule] 조회 실패: {ex.Message}");
            await MessageBox.ShowAsync($"조회 중 오류가 발생했습니다.\n{ex.Message}", "오류");
        }
        finally
        {
            LoadingRing.IsActive = false;
        }
    }

    /// <summary>
    /// 학년도 변경
    /// </summary>
    private void OnYearChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized || CBoxYear.SelectedItem == null)
            return;

        // 날짜 범위 자동 조정
        var year = (int)CBoxYear.SelectedItem;
        StartDatePicker.Date = new DateTimeOffset(new DateTime(year, 3, 1));
        EndDatePicker.Date = new DateTimeOffset(new DateTime(year + 1, 2, 28));
    }

    /// <summary>
    /// 날짜 범위 변경
    /// </summary>
    private void OnDateRangeChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        // 날짜 변경시 자동 조회하지 않음 (조회 버튼 클릭 필요)
    }

    #endregion

    #region NEIS 동기화

    /// <summary>
    /// NEIS 동기화 버튼 클릭
    /// </summary>
    private async void OnSyncNeisClick(object sender, RoutedEventArgs e)
    {
        await SyncFromNeisAsync();
    }

    /// <summary>
    /// NEIS API에서 학사일정 가져와서 DB 저장
    /// </summary>
    private async Task SyncFromNeisAsync()
    {
        if (CBoxYear.SelectedItem == null)
        {
            await MessageBox.ShowAsync("학년도를 선택하세요.", "오류");
            return;
        }

        var year = (int)CBoxYear.SelectedItem;

        // 확인 다이얼로그
        var confirmed = await MessageBox.ShowConfirmAsync(
            $"{year}학년도 학사일정을 NEIS에서 가져오시겠습니까?\n기존 데이터와 중복되지 않는 항목만 추가됩니다.",
            "NEIS 동기화", "동기화", "취소");
        if (!confirmed)
            return;

        try
        {
            LoadingRing.IsActive = true;
            TxtStatus.Text = "NEIS API 호출 중...";

            // Functions.GetSchoolSchedulesAsync() 호출
            var startDate = new DateTime(year, 3, 1);
            var endDate = new DateTime(year + 1, 2, 28);
            var service = new SchoolScheduleService(SchoolDatabase.DbPath);
            var neisSchedules = await service.DownloadFromNeisAsync(
                Settings.SchoolCode,
                Settings.ProvinceCode,
                year,
                startDate,
                endDate);

            if (neisSchedules.Schedules == null || neisSchedules.Schedules.Count == 0)
            {
                await MessageBox.ShowAsync("NEIS에서 가져온 학사일정이 없습니다.", "알림");
                return;
            }

            TxtStatus.Text = $"DB 저장 중... ({neisSchedules.Schedules.Count}개)";

            // DB에 일괄 저장 (중복 제외)
            foreach (var schedule in neisSchedules.Schedules)
            {
                schedule.IsManual = false;
                schedule.CreatedAt = DateTime.Now;
                schedule.UpdatedAt = DateTime.Now;
            }
            Settings.IsNeisEventDownloaded.Set(true); // NEIS 일정 다운로드 플래그 설정
            var value = await _scheduleservice.CreateBulkScheduleAsync(neisSchedules.Schedules);
            var savedCount = value.Count;
            var duplicateCount = neisSchedules.Schedules.Count - savedCount;

            var message = $"동기화 완료!\n총 {neisSchedules.Schedules.Count}개 중 {savedCount}개 신규 저장";
            if (duplicateCount > 0)
            {
                message += $"\n({duplicateCount}개는 이미 존재하여 건너뜀)";
            }
            
            await MessageBox.ShowAsync(message, "알림");
            
            // 목록 새로고침
            await LoadSchedulesAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SchoolSchedule] NEIS 동기화 실패: {ex.Message}");
            await MessageBox.ShowAsync($"NEIS 동기화 중 오류가 발생했습니다.\n{ex.Message}", "오류");
        }
        finally
        {
            LoadingRing.IsActive = false;
            TxtStatus.Text = "";
        }
    }

    #endregion

    #region 추가

    /// <summary>
    /// 수동 추가 버튼 클릭
    /// </summary>
    private async void OnAddManualClick(object sender, RoutedEventArgs e)
    {
        if (CBoxYear.SelectedItem == null)
        {
            await MessageBox.ShowAsync("학년도를 선택하세요.", "오류");
            return;
        }

        var year = (int)CBoxYear.SelectedItem;

        // 새 일정 생성
        var newSchedule = new SchoolSchedule
        {
            No = 0,
            SCHUL_NM = Settings.SchoolName.Value,
            ATPT_OFCDC_SC_CODE = Settings.ProvinceCode.Value,
            ATPT_OFCDC_SC_NM = Settings.ProvinceName.Value,
            SD_SCHUL_CODE = Settings.SchoolCode.Value,
            AY = year,
            AA_YMD = DateTime.Today,
            EVENT_NM = "새 일정",
            EVENT_CNTNT = "",
            SBTR_DD_SC_NM = "해당없음",
            IsManual = true,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };

        var viewModel = new SchoolScheduleViewModel(newSchedule);
        viewModel.IsSelected = true; // 자동 선택
        
        // 즉시 초기화 완료 (수동 추가는 바로 편집 가능)
        viewModel.CompleteInitialization();

        _schedules.Insert(0, viewModel);
        UpdateUI();

        Debug.WriteLine("[SchoolSchedule] 새 일정 추가");
    }

    #endregion

    #region 저장

    /// <summary>
    /// 선택 항목 저장 버튼 클릭
    /// </summary>
    private async void OnSaveSelectedClick(object sender, RoutedEventArgs e)
    {
        await SaveSelectedAsync();
    }

    /// <summary>
    /// 선택된 항목 저장
    /// </summary>
    private async Task SaveSelectedAsync()
    {
        var selected = _schedules.Where(s => s.IsSelected).ToList();

        if (selected.Count == 0)
        {
            await MessageBox.ShowAsync("저장할 항목을 선택하세요.", "알림");
            return;
        }

        try
        {
            LoadingRing.IsActive = true;
            TxtStatus.Text = $"저장 중... ({selected.Count}개)";

            int savedCount = 0;
            foreach (var viewModel in selected)
            {
                var schedule = viewModel.Schedule;
                schedule.UpdatedAt = DateTime.Now;

                if (schedule.No == 0)
                {
                    // 신규 저장
                    schedule.CreatedAt = DateTime.Now;
                    await _scheduleservice.CreateScheduleAsync(schedule);
                    savedCount++;
                }
                else
                {
                    // 업데이트
                    if ((await _scheduleservice.UpdateScheduleAsync(schedule)).Success)
                        savedCount++;
                }

                viewModel.IsSelected = false; // 체크 해제
            }

            await MessageBox.ShowAsync($"{savedCount}개 항목이 저장되었습니다.", "알림");
            
            // 수정 상태 초기화 (새로고침 대신)
            foreach (var item in selected)
            {
                item.ResetModified();
            }
            
            UpdateUI(); // UI 업데이트
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SchoolSchedule] 저장 실패: {ex.Message}");
            await MessageBox.ShowAsync($"저장 중 오류가 발생했습니다.\n{ex.Message}", "오류");
        }
        finally
        {
            LoadingRing.IsActive = false;
            TxtStatus.Text = "";
        }
    }

    #endregion

    #region 삭제

    /// <summary>
    /// 선택 항목 삭제 버튼 클릭
    /// </summary>
    private async void OnDeleteSelectedClick(object sender, RoutedEventArgs e)
    {
        await DeleteSelectedAsync();
    }

    /// <summary>
    /// 선택된 항목 삭제
    /// </summary>
    private async Task DeleteSelectedAsync()
    {
        var selected = _schedules.Where(s => s.IsSelected).ToList();

        if (selected.Count == 0)
        {
            await MessageBox.ShowAsync("삭제할 항목을 선택하세요.", "알림");
            return;
        }

        // 확인 다이얼로그
        var confirmed = await MessageBox.ShowConfirmAsync(
            $"선택한 {selected.Count}개 항목을 삭제하시겠습니까?",
            "삭제 확인", "삭제", "취소");
        if (!confirmed)
            return;

        try
        {
            LoadingRing.IsActive = true;
            TxtStatus.Text = $"삭제 중... ({selected.Count}개)";

            var deleteList = selected
                .Where(s => s.No > 0) // DB에 저장된 항목만
                .Select(s => s.No)
                .ToList();

            _ = await _scheduleservice.DeleteBulkScheduleAsync(deleteList);
            // UI에서도 제거 (No가 0인 항목 포함)
            foreach (var item in selected)
            {
                _schedules.Remove(item);
            }

            await MessageBox.ShowAsync($"{selected.Count}개 항목이 삭제되었습니다.", "알림");
            UpdateUI();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SchoolSchedule] 삭제 실패: {ex.Message}");
            await MessageBox.ShowAsync($"삭제 중 오류가 발생했습니다.\n{ex.Message}", "오류");
        }
        finally
        {
            LoadingRing.IsActive = false;
            TxtStatus.Text = "";
        }
    }

    #endregion

    #region Excel 내보내기

    /// <summary>
    /// Excel 내보내기 버튼 클릭
    /// </summary>
    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        await ExportToExcelAsync();
    }

    /// <summary>
    /// Excel 파일로 내보내기
    /// </summary>
    private async Task ExportToExcelAsync()
    {
        if (_schedules.Count == 0)
        {
            await MessageBox.ShowAsync("내보낼 데이터가 없습니다.", "알림");
            return;
        }

        try
        {
            // 파일 저장 다이얼로그
            var savePicker = new FileSavePicker();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hwnd);

            savePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            savePicker.FileTypeChoices.Add("Excel 파일", new List<string> { ".xlsx" });
            savePicker.SuggestedFileName = $"학사일정_{DateTime.Now:yyyyMMdd}";

            var file = await savePicker.PickSaveFileAsync();
            if (file == null)
                return;

            LoadingRing.IsActive = true;
            TxtStatus.Text = "Excel 내보내기 중...";

            // 내보내기용 데이터 변환 (Native AOT 호환: Dictionary + 문자열 값)
            var exportData = _schedules.Select(s => new Dictionary<string, object>
            {
                ["학년도"] = s.AY.ToString(),
                ["날짜"] = s.DisplayDate,
                ["요일"] = s.DisplayDayOfWeek,
                ["행사명"] = s.EVENT_NM,
                ["행사내용"] = s.EVENT_CNTNT,
                ["수업공제일"] = s.SBTR_DD_SC_NM,
                ["대상학년"] = s.GradeTargetText,
                ["구분"] = s.IsManual ? "수동입력" : "NEIS"
            }).ToList();

            // 임시 파일에 MiniExcel로 저장 후 StorageFile로 복사
            string tempPath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"schedule_export_{Guid.NewGuid():N}.xlsx");
            try
            {
                await Task.Run(() => MiniExcel.SaveAs(tempPath, exportData));

                var tempFile = await Windows.Storage.StorageFile.GetFileFromPathAsync(tempPath);
                await tempFile.CopyAndReplaceAsync(file);

                await MessageBox.ShowAsync($"Excel 파일로 내보내기 완료!\n{file.Path}", "알림");
            }
            finally
            {
                if (System.IO.File.Exists(tempPath))
                    System.IO.File.Delete(tempPath);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SchoolSchedule] Excel 내보내기 실패: {ex.Message}");
            await MessageBox.ShowAsync($"Excel 내보내기 중 오류가 발생했습니다.\n{ex.Message}", "오류");
        }
        finally
        {
            LoadingRing.IsActive = false;
            TxtStatus.Text = "";
        }
    }

    #endregion

    #region UI 업데이트

    /// <summary>
    /// 전체 선택/해제
    /// </summary>
    private void OnSelectAllClick(object sender, RoutedEventArgs e)
    {
        var isChecked = ChkSelectAll.IsChecked ?? false;
        foreach (var schedule in _schedules)
        {
            schedule.IsSelected = isChecked;
        }
        UpdateSelectedCount();
    }

    /// <summary>
    /// UI 상태 업데이트
    /// </summary>
    private void UpdateUI()
    {
        // 빈 상태 표시
        EmptyState.Visibility = _schedules.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ScheduleListContainer.Visibility = _schedules.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // 항목 개수
        TxtItemCount.Text = $"총 {_schedules.Count}개 항목";
        UpdateSelectedCount();
    }

    /// <summary>
    /// 선택 개수 업데이트
    /// </summary>
    private void UpdateSelectedCount()
    {
        var selectedCount = _schedules.Count(s => s.IsSelected);
        TxtSelectedCount.Text = $"선택: {selectedCount}개";
    }

    #endregion

    #region 다이얼로그

    //private async Task ShowInfoDialogAsync(string message)
    //{
    //    var dialog = new ContentDialog
    //    {
    //        Title = "알림",
    //        Content = message,
    //        CloseButtonText = "확인",
    //        XamlRoot = this.XamlRoot
    //    };
    //    await dialog.ShowAsync();
    //}

    //private async Task ShowErrorDialogAsync(string message)
    //{
    //    var dialog = new ContentDialog
    //    {
    //        Title = "오류",
    //        Content = message,
    //        CloseButtonText = "확인",
    //        XamlRoot = this.XamlRoot
    //    };
    //    await dialog.ShowAsync();
    //}

    #endregion
}

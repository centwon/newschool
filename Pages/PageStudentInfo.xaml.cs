using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MiniExcelLibs;
using NewSchool.Controls;
using NewSchool.Dialogs;
using NewSchool.Helpers;
using NewSchool.Models;
using NewSchool.Services;
using NewSchool.ViewModels;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace NewSchool.Pages;

/// <summary>
/// 학생 정보 관리 페이지
/// 학생 목록 + StudentCard + 누가기록 통합
/// </summary>
public sealed partial class PageStudentInfo : Page
{
    #region Fields

    private int _currentYear = Settings.WorkYear.Value;
    private int _currentGrade = Settings.HomeGrade.Value;
    private int _currentClass = Settings.HomeRoom.Value;
    private string? _currentStudentId;

    // Services
    private EnrollmentService? _enrollmentService;
    private StudentService? _studentService;
    private StudentLogService? _studentLogService;

    #endregion

    #region Constructor

    public PageStudentInfo()
    {
        this.InitializeComponent();
        this.Loaded += PageStudentInfo_Loaded;
        this.Unloaded += PageStudentInfo_Unloaded;
    }

    #endregion

    #region Event Handlers - Lifecycle

    private async void PageStudentInfo_Loaded(object sender, RoutedEventArgs e)
    {
        InitializeServices();
        SetupStudentContextMenu();
        // SchoolFilterPicker가 자동으로 초기화함

        // ListStudent 이벤트 구독
        StudentList.StudentSelected += StudentList_StudentSelected;
        
        // StudentCard 이벤트 구독
        SCard.StudentChanged += SCard_StudentChanged;
        
        // FilterPicker 초기화 후 학생 목록 로드 (서비스 초기화 후)
        if (FilterPicker.SelectedYear > 0 && 
            FilterPicker.SelectedGrade > 0 && 
            FilterPicker.SelectedClass > 0)
        {
            _currentYear = FilterPicker.SelectedYear;
            _currentGrade = FilterPicker.SelectedGrade;
            _currentClass = FilterPicker.SelectedClass;
            await LoadStudentListAsync();
        }
    }

    private void PageStudentInfo_Unloaded(object sender, RoutedEventArgs e)
    {
        // 자동 저장
        _ = SaveChangedAsync();

        // 이벤트 구독 해제
        StudentList.StudentSelected -= StudentList_StudentSelected;
        SCard.StudentChanged -= SCard_StudentChanged;

        // Service Dispose
        _enrollmentService?.Dispose();
        _studentService?.Dispose();
        _studentLogService?.Dispose();

        // ViewModel Dispose (StudentCard가 내부적으로 ViewModel을 관리)
        SCard?.ViewModel?.Dispose();
    }

    #endregion

    #region Initialization

    private void InitializeServices()
    {
        try
        {
            // 이전 인스턴스 정리 (Loaded 재호출 대비)
            _enrollmentService?.Dispose();
            _studentService?.Dispose();
            _studentLogService?.Dispose();

            _enrollmentService = new EnrollmentService();
            _studentService = new StudentService(SchoolDatabase.DbPath);
            _studentLogService = new StudentLogService();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PageStudentInfo] InitializeServices 오류: {ex.Message}");
        }
    }

    #endregion

    #region Event Handlers - FilterPicker

    private async void FilterPicker_SelectionChanged(object? sender, SchoolFilterChangedEventArgs e)
    {
        _currentYear = e.Year;
        _currentGrade = e.Grade;
        _currentClass = e.Class;

        // 유효성 검사: 모든 값이 설정되어야 함
        if (_currentYear > 0 && _currentGrade > 0 && _currentClass > 0)
        {
            await LoadStudentListAsync();
        }
    }

    #endregion

    #region Event Handlers - StudentList

    private async void StudentList_StudentSelected(object? sender, Enrollment e)
    {
        System.Diagnostics.Debug.WriteLine($"[PageStudentInfo] StudentList_StudentSelected: StudentID={e.StudentID}");

        // 기존 학생 정보 저장
        await SaveChangedAsync();

        // 새 학생 로드
        _currentStudentId = e.StudentID;
        System.Diagnostics.Debug.WriteLine($"[PageStudentInfo] _currentStudentId 설정됨: {_currentStudentId}");

        await LoadStudentInfoAsync(e.StudentID);

        // 버튼 활성화
        EnableButtons(true);
    }

    #endregion

    #region Event Handlers - StudentCard

    private void SCard_StudentChanged(object? sender, EventArgs e)
    {
        // 변경사항 있을 때 저장 버튼 강조
        BtnSave.IsEnabled = SCard.IsChanged;
    }

    #endregion

    #region Event Handlers - Buttons

    private async void BtnAddPhoto_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentStudentId))
            return;

        // StudentCard에서 사진 추가 (내부적으로 ViewModel 사용)
        bool success = await SCard.ViewModel.AddPhotoAsync();
        
        if (success)
        {
            await MessageBox.ShowAsync("사진이 등록되었습니다.", "사진 등록");
        }
        else
        {
            await MessageBox.ShowAsync("사진 등록이 취소되었습니다.", "알림");
        }
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        await SaveStudentInfoAsync();
    }

    private async void BtnReset_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentStudentId))
            return;

        // 확인 대화상자
        var result = await MessageBox.ShowYesNoAsync(
            $"{SCard.ViewModel.Name} 학생의 정보를 모두 삭제하고 초기화합니다.\n" +
            "되돌릴 수 없습니다. 계속할까요?",
            "학생 정보 삭제");

        if (result != ContentDialogResult.Primary)
            return;

        bool success = await SCard.ViewModel.ResetAllInfoAsync();
        
        if (success)
        {
            await SCard.ViewModel.SaveAsync();
            await MessageBox.ShowAsync("초기화되었습니다.", "초기화");
            
            // 학생 목록 새로고침
            await LoadStudentListAsync();
        }
        else
        {
            await MessageBox.ShowAsync("초기화에 실패했습니다.", "오류");
        }
    }

    private async void BtnPrint_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentStudentId))
            return;
        var window = App.MainWindow;
        if (window == null) { return; }

        try
        {
            // 인쇄 옵션 다이얼로그 표시
            var optionsDialog = new Dialogs.StudentPrintOptionsDialog
            {
                XamlRoot = this.XamlRoot
            };

            var result = await optionsDialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
                return; // 취소됨

            // 선택된 옵션 가져오기
            bool includeDetailInfo = optionsDialog.IncludeDetailInfo;
            bool includeStudentLogs = optionsDialog.IncludeStudentLogs;

            List<StudentLogViewModel>? logsToInclude = null;

            // 학생 생활 기록 포함 시
            if (includeStudentLogs)
            {
                if (optionsDialog.AllLogs)
                {
                    // 전체 기록 (최대 개수 제한)
                    int maxCount = optionsDialog.MaxLogCountValue;
                    logsToInclude = LogList.Logs.Take(maxCount).ToList();
                }
                else
                {
                    // 선택한 기록만
                    logsToInclude = LogList.SelectedLogs.ToList();

                    if (logsToInclude.Count == 0)
                    {
                        await MessageBox.ShowAsync("선택된 생활 기록이 없습니다.", "알림");
                        return;
                    }
                }
            }

            // PDF 생성
            var printService = new StudentCardPrintService();
            var pdfPath = await printService.GenerateStudentCardPdfAsync(
                SCard.ViewModel,
                window,
                includeDetailInfo,
                logsToInclude);

            // pdfPath가 null인 경우 처리
            if (pdfPath == null)
            {
                return; // 사용자가 취소함
            }

            // PDF를 시스템 기본 뷰어로 열기
            var uri = new Uri($"file:///{pdfPath.Replace("\\", "/")}");
            var success = await Windows.System.Launcher.LaunchUriAsync(uri);

            if (!success)
            {
                await MessageBox.ShowAsync($"PDF 파일을 열 수 없습니다.\n경로: {pdfPath}", "오류");
            }
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"PDF 생성 오류: {ex.Message}", "오류");
        }
    }

    #endregion

    #region Event Handlers - 누가기록

    private async void BtnNewLog_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentStudentId))
        {
            _ = MessageBox.ShowAsync("학생이 선택되지 않았습니다.", "경고");
            return;
        }

        // 새 누가기록 추가
        var newLog = new StudentLog
        {
            StudentID = _currentStudentId,
            TeacherID = Settings.User.Value,
            Year = _currentYear,
            Semester = Settings.WorkSemester.Value,
            Date = DateTime.Now,
            Category = LogCategory.기타
        };

        await LogList.AddLog(newLog);
    }

    private async void BtnSaveLog_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentStudentId))
            return;

        try
        {
            // LogListViewer에서 변경된 로그들 저장
            await LogList.SaveChangedLogsAsync();
            await MessageBox.ShowAsync("누가기록이 저장되었습니다.", "저장");
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"저장 오류: {ex.Message}", "오류");
        }
    }

    private async void BtnDeleteLog_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentStudentId))
            return;

        try
        {
            // LogListViewer에서 선택된 로그들 삭제
            await LogList.DeleteSelectedLogsAsync();
            await MessageBox.ShowAsync("선택된 누가기록이 삭제되었습니다.", "삭제");
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"삭제 오류: {ex.Message}", "오류");
        }
    }

    private async void BtnPrintLog_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentStudentId))
            return;

        // 선택된 로그가 있는지 확인
        var selectedLogs = LogList.SelectedLogs.ToList();
        if (selectedLogs.Count == 0)
        {
            await MessageBox.ShowAsync("인쇄할 누가기록을 선택하세요.", "선택 필요");
            return;
        }

        try
        {
            var printService = new StudentLogPrintService();
            var pdfPath = printService.GenerateStudentLogPdf(SCard.ViewModel, selectedLogs);

            // PDF를 시스템 기본 뷰어로 열기
            var uri = new Uri($"file:///{pdfPath.Replace("\\", "/")}");
            var success = await Windows.System.Launcher.LaunchUriAsync(uri);
            
            if (!success)
            {
                await MessageBox.ShowAsync($"PDF 파일을 열 수 없습니다.\n경로: {pdfPath}", "오류");
            }
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"PDF 생성 오류: {ex.Message}", "오류");
        }
    }

    private async void BtnExportLog_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentStudentId))
            return;

        // 누가기록이 있는지 확인
        if (LogList.Logs.Count == 0)
        {
            await MessageBox.ShowAsync("내보낼 누가기록이 없습니다.", "데이터 없음");
            return;
        }

        try
        {
            var exportService = new StudentLogExportService();
            var excelPath = exportService.ExportStudentLogsToExcel(
                SCard.ViewModel, 
                LogList.Logs.ToList());

            // 엑셀 파일을 시스템 기본 프로그램으로 열기
            var uri = new Uri($"file:///{excelPath.Replace("\\", "/")}");
            var success = await Windows.System.Launcher.LaunchUriAsync(uri);
            
            if (!success)
            {
                await MessageBox.ShowAsync($"엑셀 파일을 열 수 없습니다.\n경로: {excelPath}", "오류");
            }
            else
            {
                await MessageBox.ShowAsync($"엑셀로 내보내기 완료\n경로: {excelPath}", "성공");
            }
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"엑셀 내보내기 오류: {ex.Message}", "오류");
        }
    }

    #endregion

    #region Data Loading

    // LoadClassListAsync 제거 - SchoolFilterPicker가 자동으로 처리

    /// <summary>
    /// 학생 목록 로드
    /// </summary>
    private async Task LoadStudentListAsync()
    {
        using var enrollmentService = new EnrollmentService();
        try
        {
            // EnrollmentService를 통해 학급 명부 조회
            var roster = await enrollmentService.GetClassRosterAsync(
                Settings.SchoolCode.Value,
                _currentYear,
                _currentGrade,
                _currentClass);

            //var studentViewModels = roster.Select(r => new StudentListItemViewModel
            //{
            //    StudentID = r.StudentID,
            //    Name = r.Name,
            //    Number = r.Number,
            //    Grade = r.Grade,
            //    Class = r.Class
            //}).ToList();

            // ListStudent 컨트롤에 로드
            StudentList.LoadStudents(roster);

            // 학생 정보 초기화
            SCard.ViewModel.Clear();
            LogList.Clear();
            EnableButtons(false);
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"학생 목록 로드 오류: {ex.Message}", "오류");
        }
    }

    /// <summary>
    /// 학생 상세 정보 로드
    /// </summary>
    private async Task LoadStudentInfoAsync(string studentId)
    {
        System.Diagnostics.Debug.WriteLine($"[PageStudentInfo] LoadStudentInfoAsync 시작: studentId={studentId}");

        if (_studentService == null || _studentLogService == null)
        {
            System.Diagnostics.Debug.WriteLine($"[PageStudentInfo] 서비스가 null입니다. _studentService={_studentService}, _studentLogService={_studentLogService}");
            return;
        }

        try
        {
            // 1. StudentCard에 학생 정보 로드
            System.Diagnostics.Debug.WriteLine($"[PageStudentInfo] StudentCard 로드 시작");
            await SCard.LoadStudentAsync(studentId);
            System.Diagnostics.Debug.WriteLine($"[PageStudentInfo] StudentCard 로드 완료");

            // 2. 학생 정보 헤더 업데이트
            UpdateStudentInfoHeader();

            // 3. 누가기록 로드
            System.Diagnostics.Debug.WriteLine($"[PageStudentInfo] 누가기록 로드 시작");
            await LoadStudentLogsAsync(studentId);
            System.Diagnostics.Debug.WriteLine($"[PageStudentInfo] 누가기록 로드 완료");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PageStudentInfo] LoadStudentInfoAsync 예외: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[PageStudentInfo] StackTrace: {ex.StackTrace}");
            await MessageBox.ShowAsync($"학생 정보 로드 오류: {ex.Message}", "오류");
        }
    }

    /// <summary>
    /// 누가기록 로드
    /// </summary>
    private async Task LoadStudentLogsAsync(string studentId)
    {
        if (_studentLogService == null) return;

        try
        {
            System.Diagnostics.Debug.WriteLine($"[PageStudentInfo] LoadStudentLogsAsync: studentId={studentId}, year={_currentYear}");

            // 해당 학년도의 모든 기록 조회 (semester=0이면 전체)
            var logs = await _studentLogService.GetStudentLogsAsync(studentId, _currentYear);

            System.Diagnostics.Debug.WriteLine($"[PageStudentInfo] 조회된 로그: {logs.Count}건");

            // ViewModel 변환
            var logViewModels = new List<StudentLogViewModel>();
            foreach (var log in logs)
            {
                var logVm = await StudentLogViewModel.CreateAsync(log);
                logViewModels.Add(logVm);
            }

            LogList.LoadLogs(logViewModels);

            // 학생 개인별 보기이므로 학생 정보 모두 숨김, 카테고리/과목 표시
            LogList.StudentInfoMode = Models.StudentInfoMode.HideAll;
            LogList.Category = Models.LogCategory.전체;

            System.Diagnostics.Debug.WriteLine($"[PageStudentInfo] LogList에 로드 완료: {logViewModels.Count}건");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PageStudentInfo] LoadStudentLogsAsync 오류: {ex.Message}");
        }
    }

    #endregion

    #region Data Saving

    /// <summary>
    /// 학생 정보 저장
    /// </summary>
    private async Task<bool> SaveStudentInfoAsync()
    {
        if (string.IsNullOrEmpty(_currentStudentId))
            return false;

        try
        {
            bool success = await SCard.SaveAsync();
            
            if (success)
            {
                // 학생 목록 새로고침 (이름이 변경되었을 수 있음)
                await LoadStudentListAsync();
                
                // 같은 학생 다시 선택
                StudentList.SelectStudent(_currentStudentId);
            }

            return success;
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"저장 오류: {ex.Message}", "오류");
            return false;
        }
    }

    /// <summary>
    /// 변경사항 자동 저장
    /// </summary>
    private async Task<bool> SaveChangedAsync()
    {
        if (string.IsNullOrEmpty(_currentStudentId))
            return true;

        if (!SCard.IsChanged)
            return true;

        try
        {
            // 리팩토링 후: ViewModel의 SaveAsync 메서드 사용
            return await SCard.ViewModel.SaveAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PageStudentInfo] SaveChangedAsync 오류: {ex.Message}");
            return false;
        }
    }

    #endregion

    #region Event Handlers — 엑셀 일괄 입력

    /// <summary>
    /// 양식 다운로드 클릭
    /// </summary>
    private async void BtnDownloadTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (_currentYear <= 0 || _currentGrade <= 0 || _currentClass <= 0)
        {
            await MessageBox.ShowAsync("학년/반을 먼저 선택하세요.", "알림");
            return;
        }

        try
        {
            await GenerateStudentInfoTemplateAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PageStudentInfo] 양식 다운로드 오류: {ex.Message}");
            await MessageBox.ShowAsync($"양식 다운로드 오류: {ex.Message}", "오류");
        }
    }

    /// <summary>
    /// 일괄 입력 클릭
    /// </summary>
    private async void BtnBulkImport_Click(object sender, RoutedEventArgs e)
    {
        if (_currentYear <= 0 || _currentGrade <= 0 || _currentClass <= 0)
        {
            await MessageBox.ShowAsync("학년/반을 먼저 선택하세요.", "알림");
            return;
        }

        try
        {
            // 1. 엑셀 파일 선택
            var window = App.MainWindow;
            if (window == null) return;

            var file = await ExcelHelpers.PickExcelFileAsync(window);
            if (file == null) return;

            // 2. 엑셀 파싱 + 학생 매칭
            var importData = await ProcessStudentInfoExcelAsync(file.Path);
            if (importData == null || importData.Count == 0)
            {
                await MessageBox.ShowAsync("엑셀 파일에서 학생 데이터를 찾을 수 없습니다.", "알림");
                return;
            }

            // 3. 미리보기 다이얼로그
            var dialog = new BulkStudentInfoPreviewDialog
            {
                XamlRoot = this.XamlRoot
            };
            dialog.SetPreviewData(importData);

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
                return;

            // 4. 일괄 저장
            await SaveBulkStudentInfoAsync(importData);

            // 5. 학급 목록 새로고침
            await LoadStudentListAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PageStudentInfo] 일괄 입력 오류: {ex.Message}");
            await MessageBox.ShowAsync($"일괄 입력 오류: {ex.Message}", "오류");
        }
    }

    #endregion

    #region 엑셀 템플릿 생성

    /// <summary>
    /// 학생 정보 엑셀 양식 생성 (기존 데이터 포함)
    /// </summary>
    private async Task GenerateStudentInfoTemplateAsync()
    {
        var window = App.MainWindow;
        if (window == null) return;

        // 1. 학급 명부 로드
        using var enrollmentService = new EnrollmentService();
        var roster = await enrollmentService.GetClassRosterAsync(
            Settings.SchoolCode.Value, _currentYear, _currentGrade, _currentClass);

        if (roster.Count == 0)
        {
            await MessageBox.ShowAsync("학급에 등록된 학생이 없습니다.", "알림");
            return;
        }

        // 2. 각 학생의 기존 데이터 일괄 로드 (N+1 쿼리 방지)
        using var studentService = new StudentService(SchoolDatabase.DbPath);
        using var detailService = new StudentDetailService(SchoolDatabase.DbPath);

        var studentIds = roster.Select(r => r.StudentID).ToList();
        var allStudents = await studentService.GetStudentsByIdsAsync(studentIds);
        var studentDict = allStudents.ToDictionary(s => s.StudentID, s => s);
        var allDetails = await detailService.GetByStudentIdsAsync(studentIds);
        var detailDict = allDetails.ToDictionary(d => d.StudentID, d => d);

        var rows = new List<Dictionary<string, object>>();

        foreach (var enrollment in roster.OrderBy(r => r.Number))
        {
            studentDict.TryGetValue(enrollment.StudentID, out var student);
            detailDict.TryGetValue(enrollment.StudentID, out var detail);

            var row = new Dictionary<string, object>
            {
                ["번호"] = enrollment.Number,
                ["이름"] = enrollment.Name ?? string.Empty,
                ["성별"] = student?.Sex ?? string.Empty,
                ["생년월일"] = student?.BirthDate?.ToString("yyyy-MM-dd") ?? string.Empty,
                ["전화번호"] = student?.Phone ?? string.Empty,
                ["이메일"] = student?.Email ?? string.Empty,
                ["주소"] = student?.Address ?? string.Empty,
                ["메모"] = student?.Memo ?? string.Empty,
                ["보호자이름"] = detail?.GuardianName ?? string.Empty,
                ["보호자관계"] = detail?.GuardianRelation ?? string.Empty,
                ["보호자전화"] = detail?.GuardianPhone ?? string.Empty,
                ["아버지이름"] = detail?.FatherName ?? string.Empty,
                ["아버지전화"] = detail?.FatherPhone ?? string.Empty,
                ["아버지직업"] = detail?.FatherJob ?? string.Empty,
                ["어머니이름"] = detail?.MotherName ?? string.Empty,
                ["어머니전화"] = detail?.MotherPhone ?? string.Empty,
                ["어머니직업"] = detail?.MotherJob ?? string.Empty,
                ["진로희망"] = detail?.CareerGoal ?? string.Empty,
                ["특기재능"] = detail?.Talents ?? string.Empty,
                ["흥미관심"] = detail?.Interests ?? string.Empty,
                ["건강상태"] = detail?.HealthInfo ?? string.Empty,
                ["알레르기"] = detail?.Allergies ?? string.Empty,
                ["특이사항"] = detail?.SpecialNeeds ?? string.Empty
            };

            rows.Add(row);
        }

        // 3. FileSavePicker로 저장 위치 선택
        string defaultFileName = $"학생정보_{_currentGrade}학년{_currentClass}반_{DateTime.Now:yyyyMMdd}.xlsx";
        var saveFile = await ExcelHelpers.SaveExcelFileAsync(window, defaultFileName);
        if (saveFile == null) return;

        // 4. 임시 파일에 MiniExcel로 저장 후 복사
        string tempPath = Path.Combine(Path.GetTempPath(), $"studentinfo_template_{Guid.NewGuid():N}.xlsx");
        try
        {
            await Task.Run(() => MiniExcel.SaveAs(tempPath, rows));

            var tempFile = await StorageFile.GetFileFromPathAsync(tempPath);
            await tempFile.CopyAndReplaceAsync(saveFile);

            // 파일 열기
            await Windows.System.Launcher.LaunchFileAsync(saveFile);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    #endregion

    #region 엑셀 파싱 + 학생 매칭

    // 컬럼명 → 필드 매핑 (유연한 매칭)
    private static readonly Dictionary<string, string> StudentFieldMap = new()
    {
        ["성별"] = "Sex",
        ["생년월일"] = "BirthDate", ["생일"] = "BirthDate",
        ["전화번호"] = "Phone", ["전화"] = "Phone", ["연락처"] = "Phone",
        ["이메일"] = "Email", ["메일"] = "Email",
        ["주소"] = "Address",
        ["메모"] = "Memo"
    };

    private static readonly Dictionary<string, string> DetailFieldMap = new()
    {
        ["보호자이름"] = "GuardianName", ["보호자"] = "GuardianName",
        ["보호자관계"] = "GuardianRelation", ["관계"] = "GuardianRelation",
        ["보호자전화"] = "GuardianPhone", ["보호자연락처"] = "GuardianPhone",
        ["아버지이름"] = "FatherName", ["아버지"] = "FatherName", ["부"] = "FatherName",
        ["아버지전화"] = "FatherPhone", ["부전화"] = "FatherPhone",
        ["아버지직업"] = "FatherJob", ["부직업"] = "FatherJob",
        ["어머니이름"] = "MotherName", ["어머니"] = "MotherName", ["모"] = "MotherName",
        ["어머니전화"] = "MotherPhone", ["모전화"] = "MotherPhone",
        ["어머니직업"] = "MotherJob", ["모직업"] = "MotherJob",
        ["진로희망"] = "CareerGoal", ["진로"] = "CareerGoal",
        ["특기재능"] = "Talents", ["특기"] = "Talents", ["재능"] = "Talents",
        ["흥미관심"] = "Interests", ["흥미"] = "Interests", ["관심"] = "Interests", ["관심분야"] = "Interests",
        ["건강상태"] = "HealthInfo", ["건강"] = "HealthInfo",
        ["알레르기"] = "Allergies",
        ["특이사항"] = "SpecialNeeds"
    };

    /// <summary>
    /// 엑셀 파일 파싱 + 학급 명부와 매칭
    /// </summary>
    private async Task<List<StudentImportPreviewItem>?> ProcessStudentInfoExcelAsync(string filePath)
    {
        // 1. 엑셀 데이터 읽기
        var sheets = await ExcelHelper.DataToTextAsync(filePath);
        if (sheets == null || sheets.Count == 0) return null;

        var data = sheets[0]; // 첫 번째 시트
        int rows = data.GetLength(0);
        int cols = data.GetLength(1);

        if (rows < 2 || cols < 2) return null;

        // 2. 헤더 행 찾기 ("번호" + "이름"/"성명" 컬럼 찾기)
        int headerRow = -1;
        int numberCol = -1;
        int nameCol = -1;

        for (int r = 1; r < Math.Min(rows, 11); r++)
        {
            for (int c = 1; c < cols; c++)
            {
                string val = (data[r, c] ?? string.Empty).Trim();
                if (val == "번호") numberCol = c;
                if (val == "이름" || val == "성명") nameCol = c;
            }

            if (numberCol > 0 && nameCol > 0)
            {
                headerRow = r;
                break;
            }

            numberCol = -1;
            nameCol = -1;
        }

        if (headerRow < 0)
        {
            await MessageBox.ShowAsync("엑셀 파일에서 '번호'와 '이름' 열을 찾을 수 없습니다.\n첫 행에 '번호', '이름' 열 헤더가 있어야 합니다.", "형식 오류");
            return null;
        }

        // 3. 컬럼 매핑 구성
        var studentColMap = new Dictionary<int, string>(); // col → Student field
        var detailColMap = new Dictionary<int, string>();   // col → Detail field
        var colNameMap = new Dictionary<int, string>();     // col → 원래 컬럼명

        for (int c = 1; c < cols; c++)
        {
            string header = (data[headerRow, c] ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(header)) continue;

            colNameMap[c] = header;

            if (StudentFieldMap.TryGetValue(header, out var studentField))
                studentColMap[c] = studentField;
            else if (DetailFieldMap.TryGetValue(header, out var detailField))
                detailColMap[c] = detailField;
        }

        // 4. 학급 명부 로드 (번호 → Enrollment 매핑)
        using var enrollmentService = new EnrollmentService();
        var roster = await enrollmentService.GetClassRosterAsync(
            Settings.SchoolCode.Value, _currentYear, _currentGrade, _currentClass);

        var numberToEnrollment = roster.ToDictionary(r => r.Number, r => r);

        // 5. 기존 데이터 일괄 로드 (N+1 쿼리 방지)
        using var studentService = new StudentService(SchoolDatabase.DbPath);
        using var detailService = new StudentDetailService(SchoolDatabase.DbPath);

        var rosterStudentIds = roster.Select(r => r.StudentID).ToList();
        var allStudents = await studentService.GetStudentsByIdsAsync(rosterStudentIds);
        var studentDict = allStudents.ToDictionary(s => s.StudentID, s => s);
        var allDetails = await detailService.GetByStudentIdsAsync(rosterStudentIds);
        var detailDict = allDetails.ToDictionary(d => d.StudentID, d => d);

        // 6. 각 행 파싱
        var result = new List<StudentImportPreviewItem>();

        for (int r = headerRow + 1; r < rows; r++)
        {
            string numStr = (data[r, numberCol] ?? string.Empty).Trim();
            string name = (data[r, nameCol] ?? string.Empty).Trim();

            if (string.IsNullOrEmpty(numStr) && string.IsNullOrEmpty(name))
                continue; // 빈 행 스킵

            if (!int.TryParse(numStr, out int number))
                continue; // 번호가 숫자가 아닌 행 스킵

            var item = new StudentImportPreviewItem
            {
                Number = number,
                Name = name
            };

            // 매칭 시도
            if (numberToEnrollment.TryGetValue(number, out var enrollment))
            {
                item.MatchedStudentID = enrollment.StudentID;

                // 기존 데이터 조회 (Dictionary에서 O(1))
                studentDict.TryGetValue(enrollment.StudentID, out var existingStudent);
                detailDict.TryGetValue(enrollment.StudentID, out var existingDetail);

                // Student 필드 비교
                foreach (var (col, field) in studentColMap)
                {
                    string newValue = (data[r, col] ?? string.Empty).Trim();
                    if (string.IsNullOrEmpty(newValue)) continue;

                    string? oldValue = GetStudentFieldValue(existingStudent, field);
                    if (newValue != (oldValue ?? string.Empty))
                    {
                        item.StudentFields[field] = newValue;
                        item.Changes.Add(colNameMap[col]);
                    }
                }

                // Detail 필드 비교
                foreach (var (col, field) in detailColMap)
                {
                    string newValue = (data[r, col] ?? string.Empty).Trim();
                    if (string.IsNullOrEmpty(newValue)) continue;

                    string? oldValue = GetDetailFieldValue(existingDetail, field);
                    if (newValue != (oldValue ?? string.Empty))
                    {
                        item.DetailFields[field] = newValue;
                        item.Changes.Add(colNameMap[col]);
                    }
                }
            }

            result.Add(item);
        }

        return result;
    }

    /// <summary>
    /// Student 모델에서 필드 값 읽기
    /// </summary>
    private static string? GetStudentFieldValue(Student? student, string fieldName)
    {
        if (student == null) return null;
        return fieldName switch
        {
            "Sex" => student.Sex,
            "BirthDate" => student.BirthDate?.ToString("yyyy-MM-dd"),
            "Phone" => student.Phone,
            "Email" => student.Email,
            "Address" => student.Address,
            "Memo" => student.Memo,
            _ => null
        };
    }

    /// <summary>
    /// StudentDetail 모델에서 필드 값 읽기
    /// </summary>
    private static string? GetDetailFieldValue(StudentDetail? detail, string fieldName)
    {
        if (detail == null) return null;
        return fieldName switch
        {
            "GuardianName" => detail.GuardianName,
            "GuardianRelation" => detail.GuardianRelation,
            "GuardianPhone" => detail.GuardianPhone,
            "FatherName" => detail.FatherName,
            "FatherPhone" => detail.FatherPhone,
            "FatherJob" => detail.FatherJob,
            "MotherName" => detail.MotherName,
            "MotherPhone" => detail.MotherPhone,
            "MotherJob" => detail.MotherJob,
            "CareerGoal" => detail.CareerGoal,
            "Talents" => detail.Talents,
            "Interests" => detail.Interests,
            "HealthInfo" => detail.HealthInfo,
            "Allergies" => detail.Allergies,
            "SpecialNeeds" => detail.SpecialNeeds,
            _ => null
        };
    }

    /// <summary>
    /// Student 모델에 필드 값 설정
    /// </summary>
    private static void SetStudentFieldValue(Student student, string fieldName, string value)
    {
        switch (fieldName)
        {
            case "Sex": student.Sex = value; break;
            case "BirthDate":
                if (DateTime.TryParseExact(value, new[] { "yyyy-MM-dd", "yyyy.MM.dd", "yyyy/MM/dd" },
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                    student.BirthDate = dt;
                break;
            case "Phone": student.Phone = value; break;
            case "Email": student.Email = value; break;
            case "Address": student.Address = value; break;
            case "Memo": student.Memo = value; break;
        }
    }

    /// <summary>
    /// StudentDetail 모델에 필드 값 설정
    /// </summary>
    private static void SetDetailFieldValue(StudentDetail detail, string fieldName, string value)
    {
        switch (fieldName)
        {
            case "GuardianName": detail.GuardianName = value; break;
            case "GuardianRelation": detail.GuardianRelation = value; break;
            case "GuardianPhone": detail.GuardianPhone = value; break;
            case "FatherName": detail.FatherName = value; break;
            case "FatherPhone": detail.FatherPhone = value; break;
            case "FatherJob": detail.FatherJob = value; break;
            case "MotherName": detail.MotherName = value; break;
            case "MotherPhone": detail.MotherPhone = value; break;
            case "MotherJob": detail.MotherJob = value; break;
            case "CareerGoal": detail.CareerGoal = value; break;
            case "Talents": detail.Talents = value; break;
            case "Interests": detail.Interests = value; break;
            case "HealthInfo": detail.HealthInfo = value; break;
            case "Allergies": detail.Allergies = value; break;
            case "SpecialNeeds": detail.SpecialNeeds = value; break;
        }
    }

    #endregion

    #region 일괄 저장

    /// <summary>
    /// 매칭된 학생들의 정보를 일괄 저장
    /// </summary>
    private async Task SaveBulkStudentInfoAsync(List<StudentImportPreviewItem> importData)
    {
        var toSave = importData.Where(i => i.IsMatched && i.Changes.Count > 0).ToList();
        if (toSave.Count == 0)
        {
            await MessageBox.ShowAsync("저장할 변경 사항이 없습니다.", "알림");
            return;
        }

        int successCount = 0;
        int failCount = 0;
        var failedStudents = new List<string>();

        foreach (var item in toSave)
        {
            try
            {
                using var studentService = new StudentService(SchoolDatabase.DbPath);
                using var detailService = new StudentDetailService(SchoolDatabase.DbPath);

                // Student 필드 업데이트
                if (item.StudentFields.Count > 0)
                {
                    var student = await studentService.GetBasicInfoAsync(item.MatchedStudentID!);
                    if (student != null)
                    {
                        foreach (var (field, value) in item.StudentFields)
                        {
                            SetStudentFieldValue(student, field, value!);
                        }
                        await studentService.UpdateBasicInfoAsync(student);
                    }
                }

                // StudentDetail 필드 업데이트
                if (item.DetailFields.Count > 0)
                {
                    var detail = await detailService.GetByStudentIdAsync(item.MatchedStudentID!);
                    if (detail == null)
                    {
                        detail = new StudentDetail { StudentID = item.MatchedStudentID! };
                    }

                    foreach (var (field, value) in item.DetailFields)
                    {
                        SetDetailFieldValue(detail, field, value!);
                    }

                    await detailService.CreateOrUpdateAsync(detail);
                }

                successCount++;
            }
            catch (Exception ex)
            {
                failCount++;
                failedStudents.Add($"{item.Number}번 {item.Name}");
                Debug.WriteLine($"[PageStudentInfo] 학생 저장 실패 ({item.Number}번 {item.Name}): {ex.Message}");
            }
        }

        // 결과 표시
        string message = $"{successCount}명의 학생 정보가 저장되었습니다.";
        if (failCount > 0)
        {
            message += $"\n\n{failCount}명 실패:\n" + string.Join("\n", failedStudents);
        }

        await MessageBox.ShowAsync(message, failCount > 0 ? "일부 실패" : "완료");
    }

    #endregion

    #region 컨텍스트 메뉴

    /// <summary>학생 목록 우클릭 컨텍스트 메뉴 설정</summary>
    private void SetupStudentContextMenu()
    {
        var menu = new MenuFlyout();

        var miAddLog = new MenuFlyoutItem
        {
            Text = "누가기록 작성",
            Icon = new FontIcon { Glyph = "\uE70F" }
        };
        miAddLog.Click += ContextMenu_AddLog_Click;

        menu.Items.Add(miAddLog);
        StudentList.ItemContextFlyout = menu;
    }

    private async void ContextMenu_AddLog_Click(object sender, RoutedEventArgs e)
    {
        var student = StudentList.SelectedStudent;
        if (student == null) return;

        if (_currentYear == 0)
        {
            await MessageBox.ShowAsync("학년도를 먼저 선택해주세요.", "알림");
            return;
        }

        var logDialog = new StudentLogDialog(
            student,
            _currentYear,
            Settings.WorkSemester.Value);
        logDialog.Closed += async (s, args) =>
        {
            // 현재 학생의 누가기록 새로고침
            if (_currentStudentId != null)
                await LoadStudentLogsAsync(_currentStudentId);
        };
        logDialog.Activate();
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// 학생 정보 헤더 업데이트
    /// </summary>
    private void UpdateStudentInfoHeader()
    {
        // 헤더 TextBlock 제거됨 — 학생 정보는 StudentCard에서 직접 표시
    }

    /// <summary>
    /// 버튼 활성화/비활성화
    /// </summary>
    private void EnableButtons(bool enabled)
    {
        BtnSave.IsEnabled = enabled && SCard.IsChanged;
        BtnReset.IsEnabled = enabled;
        BtnPrint.IsEnabled = enabled;
    }

    #endregion
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Models;
using NewSchool.Services;
using NewSchool.ViewModels;

namespace NewSchool.Controls;

/// <summary>
/// 학생 생활 기록 추가/수정 다이얼로그
/// </summary>
public sealed partial class StudentLogWin : Window
{
    #region Fields

    private readonly TaskCompletionSource<bool> _dialogResult = new();
    private readonly EnrollmentService _enrollmentService;
    private List<StudentListItemViewModel> _students = new();
    private StudentLogViewModel? _logViewModel;

    #endregion

    #region Properties

    /// <summary>다이얼로그 결과 (true: 저장, false: 취소)</summary>
    public bool Result { get; private set; }

    /// <summary>편집 중인 로그 ViewModel</summary>
    public StudentLogViewModel? LogViewModel => _logViewModel;

    #endregion

    #region Constructors

    /// <summary>
    /// 새 로그 작성용 생성자
    /// </summary>
    public StudentLogWin(int year, int semester, int grade, int classNumber, DateTime date)
    {
        this.InitializeComponent();
        
        _enrollmentService = new EnrollmentService();
        
        // 초기화
        InitializeAsync(year, semester, grade, classNumber, date, null);
        
        this.Closed += OnWindowClosed;
    }

    /// <summary>
    /// 기존 로그 수정용 생성자
    /// </summary>
    public StudentLogWin(StudentLogViewModel logViewModel)
    {
        this.InitializeComponent();
        
        _enrollmentService = new EnrollmentService();
        _logViewModel = logViewModel;
        
        // 초기화
        InitializeAsync(
            logViewModel.Year, 
            logViewModel.Semester, 
            logViewModel.Grade, 
            logViewModel.Class, 
            logViewModel.Date.DateTime, 
            logViewModel);
        
        this.Closed += OnWindowClosed;
    }

    /// <summary>
    /// 학생 드롭용 생성자 (학생 미리 선택)
    /// </summary>
    public StudentLogWin(int year, int semester, int grade, int classNumber, DateTime date, string studentId)
    {
        this.InitializeComponent();
        
        _enrollmentService = new EnrollmentService();
        
        // 초기화
        InitializeAsync(year, semester, grade, classNumber, date, null, studentId);
        
        this.Closed += OnWindowClosed;
    }

    #endregion

    #region Initialization

    private async void InitializeAsync(
        int year, int semester, int grade, int classNumber, 
        DateTime date, StudentLogViewModel? existingLog = null, 
        string? preSelectedStudentId = null)
    {
        try
        {
            // 학생 목록 로드
            await LoadStudentsAsync(year, grade, classNumber);
            
            // 카테고리 로드
            LoadCategories();
            
            if (existingLog != null)
            {
                // 기존 로그 수정 모드
                Title = "학생 생활 기록 수정";
                LoadExistingLog(existingLog);
            }
            else
            {
                // 새 로그 작성 모드
                Title = "학생 생활 기록 추가";
                
                // 빈 로그 생성
                var newLog = new StudentLog
                {
                    Year = year,
                    Semester = semester,
                    Date = date,
                    TeacherID = Settings.User,
                    Category = LogCategory.전체
                };
                
                _logViewModel = await StudentLogViewModel.CreateAsync(newLog);
                
                // 날짜 설정
                DatePickerLog.Date = date;
                
                // 학생 미리 선택 (드롭된 경우)
                if (!string.IsNullOrEmpty(preSelectedStudentId))
                {
                    var student = _students.FirstOrDefault(s => s.StudentID == preSelectedStudentId);
                    if (student != null)
                    {
                        CboStudent.SelectedItem = student;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"초기화 실패: {ex.Message}", "오류");
        }
    }

    /// <summary>
    /// 학생 목록 로드
    /// </summary>
    private async Task LoadStudentsAsync(int year, int grade, int classNumber)
    {
        var enrollments = await _enrollmentService.GetClassRosterAsync(
            Settings.SchoolCode, year, grade, classNumber);
        
        // Enrollment를 StudentListItemViewModel로 변환
        _students = enrollments.Select(e => new StudentListItemViewModel
        {
            EnrollmentNo = e.No,
            StudentID = e.StudentID,
            SchoolCode = e.SchoolCode,
            Year = e.Year,
            Semester = e.Semester,
            Grade = e.Grade,
            Class = e.Class,
            Number = e.Number,
            Name = e.Name,
            Sex = e.Sex,
            Status = e.Status
        }).ToList();
        
        CboStudent.ItemsSource = _students;
        CboStudent.DisplayMemberPath = nameof(StudentListItemViewModel.NumberAndName);
    }

    /// <summary>
    /// 카테고리 로드
    /// </summary>
    private void LoadCategories()
    {
        var categories = Enum.GetValues<LogCategory>().ToList();
        CboCategory.ItemsSource = categories;
        // DisplayMemberPath 제거 - enum의 ToString()이 자동으로 호출됨
    }

    /// <summary>
    /// 기존 로그 데이터 로드
    /// </summary>
    private void LoadExistingLog(StudentLogViewModel log)
    {
        // 학생 선택
        var student = _students.FirstOrDefault(s => s.StudentID == log.StudentID);
        if (student != null)
        {
            CboStudent.SelectedItem = student;
        }
        
        // 기본 정보
        DatePickerLog.Date = log.Date;
        CboCategory.SelectedItem = log.Category;
        
        // 활동 정보
        TxtActivityName.Text = log.ActivityName;
        TxtTopic.Text = log.Topic;
        TxtLog.Text = log.Log;
        
        // 추가 정보
        TxtDescription.Text = log.Description;
        TxtRole.Text = log.Role;
        TxtSkillDeveloped.Text = log.SkillDeveloped;
        TxtStrengthShown.Text = log.StrengthShown;
        TxtResultOrOutcome.Text = log.ResultOrOutcome;
        TxtTag.Text = log.Tag;
        ChkIsImportant.IsChecked = log.IsImportant;
        
        // 추가 정보가 있으면 펼치기
        if (!string.IsNullOrWhiteSpace(log.Description) ||
            !string.IsNullOrWhiteSpace(log.Role) ||
            !string.IsNullOrWhiteSpace(log.SkillDeveloped) ||
            !string.IsNullOrWhiteSpace(log.StrengthShown) ||
            !string.IsNullOrWhiteSpace(log.ResultOrOutcome) ||
            !string.IsNullOrWhiteSpace(log.Tag) ||
            log.IsImportant)
        {
            ExpanderAdditional.IsExpanded = true;
        }
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// 학생 선택 변경
    /// </summary>
    private void CboStudent_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CboStudent.SelectedItem is StudentListItemViewModel student)
        {
            TxtStudentInfo.Text = $"{student.Grade}학년 {student.Class}반 {student.Number}번";
            
            if (_logViewModel != null)
            {
                _logViewModel.StudentID = student.StudentID;
            }
        }
    }

    /// <summary>
    /// 카테고리 변경
    /// </summary>
    private void CboCategory_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CboCategory.SelectedItem is LogCategory category)
        {
            // 교과활동일 때만 과목 선택 표시
            if (category == LogCategory.교과활동)
            {
                TxtSubject.Visibility = Visibility.Visible;
                CboSubject.Visibility = Visibility.Visible;
                
                // TODO: 과목 목록 로드
                // LoadSubjectsAsync();
            }
            else
            {
                TxtSubject.Visibility = Visibility.Collapsed;
                CboSubject.Visibility = Visibility.Collapsed;
            }
            
            if (_logViewModel != null)
            {
                _logViewModel.Category = category;
            }
        }
    }

    /// <summary>
    /// 기록 내용 변경 시 바이트 수 계산
    /// </summary>
    private void TxtLog_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateByteCount();
    }

    /// <summary>
    /// 바이트 수 업데이트
    /// </summary>
    private void UpdateByteCount()
    {
        string text = TxtLog.Text ?? string.Empty;
        int byteCount = CalculateNeisByte(text);
        int charCount = text.Length;
        
        TxtByteCount.Text = $"{byteCount} Byte / {charCount} 자";
    }

    /// <summary>
    /// NEIS 바이트 계산
    /// </summary>
    private int CalculateNeisByte(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        int byteCount = 0;
        foreach (char c in text)
        {
            if (c >= 0xAC00 && c <= 0xD7A3) // 한글
            {
                byteCount += 3;
            }
            else if (c >= 0x3000) // 한자 및 기타
            {
                byteCount += 3;
            }
            else // ASCII
            {
                byteCount += 1;
            }
        }
        return byteCount;
    }

    /// <summary>
    /// 저장 버튼
    /// </summary>
    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        // 유효성 검사
        if (CboStudent.SelectedItem == null)
        {
            await MessageBox.ShowAsync("학생을 선택해주세요.", "알림");
            return;
        }
        
        if (CboCategory.SelectedItem == null)
        {
            await MessageBox.ShowAsync("영역을 선택해주세요.", "알림");
            return;
        }
        
        if (string.IsNullOrWhiteSpace(TxtLog.Text))
        {
            await MessageBox.ShowAsync("기록 내용을 입력해주세요.", "알림");
            return;
        }
        
        // ViewModel에 값 설정
        if (_logViewModel != null)
        {
            _logViewModel.Date = DatePickerLog.Date;
            _logViewModel.ActivityName = TxtActivityName.Text ?? string.Empty;
            _logViewModel.Topic = TxtTopic.Text ?? string.Empty;
            _logViewModel.Log = TxtLog.Text ?? string.Empty;
            _logViewModel.Description = TxtDescription.Text ?? string.Empty;
            _logViewModel.Role = TxtRole.Text ?? string.Empty;
            _logViewModel.SkillDeveloped = TxtSkillDeveloped.Text ?? string.Empty;
            _logViewModel.StrengthShown = TxtStrengthShown.Text ?? string.Empty;
            _logViewModel.ResultOrOutcome = TxtResultOrOutcome.Text ?? string.Empty;
            _logViewModel.Tag = TxtTag.Text ?? string.Empty;
            _logViewModel.IsImportant = ChkIsImportant.IsChecked ?? false;
        }
        
        Result = true;
        _dialogResult.TrySetResult(true);
        Close();
    }

    /// <summary>
    /// 취소 버튼
    /// </summary>
    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        Result = false;
        _dialogResult.TrySetResult(false);
        Close();
    }

    /// <summary>
    /// 윈도우 닫힘
    /// </summary>
    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _dialogResult.TrySetResult(false);
        _enrollmentService?.Dispose();
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// 다이얼로그 표시 (비동기)
    /// </summary>
    public async Task<bool> ShowDialogAsync()
    {
        this.Activate();
        return await _dialogResult.Task;
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

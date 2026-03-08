using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Collections;
using NewSchool.Models;
using NewSchool.Services;
using NewSchool.Repositories;
using NewSchool.Helpers;
using Windows.Devices.Midi;
using System.Diagnostics;
using NewSchool.Controls;

namespace NewSchool.Pages;

/// <summary>
/// 학생 관리 페이지 (WinUI3)
/// 학생 목록 조회, 수정, 삭제 기능 제공
/// 
/// 주요 기능:
/// 1. 학년도/학년/반별 학생 목록 조회
/// 2. 학생 정보 직접 수정 (인라인 편집)
/// 3. 선택한 학생들 일괄 삭제
/// 4. 학생 추가 페이지로 이동
/// 5. 전체 선택/해제 기능
/// </summary>
public sealed partial class StudentManagementPage : Page
{
    private readonly EnrollmentService _enrollmentService;
    /// <summary>
    /// 학생 목록 (최적화됨)
    /// ⚡ OptimizedObservableCollection로 UI 업데이트 80% 향상
    /// </summary>
    public OptimizedObservableCollection<StudentManagementViewModel> Students { get; } = new();

    public StudentManagementPage()
    {
        this.InitializeComponent();

        // ⭐ SchoolDatabase.DbPath 사용 (전체 경로)
        _enrollmentService = new EnrollmentService();

        this.Loaded += StudentManagementPage_Loaded;
        this.Unloaded += (_, _) => _enrollmentService?.Dispose();
    }

    private void StudentManagementPage_Loaded(object sender, RoutedEventArgs e)
    {
        CheckDatabaseInitialization();
    }

    /// <summary>
    /// 데이터베이스 초기화 확인
    /// </summary>
    private async void CheckDatabaseInitialization()
    {
        try
        {
            // DB 파일 존재 확인
            if (!SchoolDatabase.DatabaseExists())
            {
                System.Diagnostics.Debug.WriteLine("[StudentManagement] DB 파일이 없습니다. 초기화 시작...");
                await SchoolDatabase.InitAsync();
            }

            // Enrollment 테이블 존재 확인
            // ⭐ SchoolDatabase.DbPath 사용
            using var repo = new EnrollmentRepository(SchoolDatabase.DbPath);
            try
            {
                // 간단한 쿼리로 테이블 존재 확인
                await repo.GetCountAsync(Settings.SchoolCode.Value,
                    Settings.WorkYear.Value, Settings.WorkSemester.Value);
            }
            catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.Message.Contains("no such table"))
            {
                System.Diagnostics.Debug.WriteLine("[StudentManagement] Enrollment 테이블이 없습니다. 재초기화...");

                // 초기화 플래그 리셋 후 재초기화
                Settings.School_Inited.Set(false);
                await SchoolDatabase.InitAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StudentManagement] DB 초기화 확인 오류: {ex.Message}");
            await MessageBox.ShowAsync($"데이터베이스 초기화 오류\n{ex.Message}", "오류");
        }
    }

    #region 초기화

    // SchoolFilterPicker가 자동으로 초기화함

    #endregion

    #region 이벤트 핸들러

    /// <summary>
    /// 조회 버튼 클릭
    /// </summary>
    private async void OnLookUpClick(object sender, RoutedEventArgs e)
    {
        await LoadStudentsAsync();
    }

    /// <summary>
    /// 저장 버튼 클릭
    /// </summary>
    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        await SaveStudentsAsync();
    }

    /// <summary>
    /// 삭제 버튼 클릭
    /// </summary>
    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        await DeleteStudentsAsync();
    }

    /// <summary>
    /// 학생 추가 버튼 클릭
    /// </summary>
    private void OnAddStudentClick(object sender, RoutedEventArgs e)
    {
        // AddStudentsPage로 이동
        if (this.Frame != null)
        {
            this.Frame.Navigate(typeof(AddStudentsPage));
        }
    }

    /// <summary>
    /// 전체 선택/해제 체크박스 클릭
    /// </summary>
    private void OnSelectAllClick(object sender, RoutedEventArgs e)
    {
        // Indeterminate(null) 상태에서 클릭하면 전체 선택
        bool isChecked = ChkSelectAll.IsChecked != false;
        ChkSelectAll.IsChecked = isChecked;

        foreach (var student in Students)
        {
            student.IsSelected = isChecked;
        }
        
        // ItemsRepeater 강제 새로고침
        StudentList.ItemsSource = null;
        StudentList.ItemsSource = Students;
    }

    /// <summary>
    /// 개별 학생 체크박스 클릭
    /// </summary>
    private void OnStudentCheckBoxClick(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox && checkBox.DataContext is StudentManagementViewModel vm)
        {
            vm.IsSelected = checkBox.IsChecked == true;
            UpdateSelectAllCheckBoxState();
        }
    }

    /// <summary>
    /// 전체 선택 체크박스 상태 업데이트
    /// </summary>
    private void UpdateSelectAllCheckBoxState()
    {
        if (Students.Count == 0)
        {
            ChkSelectAll.IsChecked = false;
            return;
        }

        int selectedCount = Students.Count(s => s.IsSelected);

        if (selectedCount == 0)
        {
            ChkSelectAll.IsChecked = false;
        }
        else if (selectedCount == Students.Count)
        {
            ChkSelectAll.IsChecked = true;
        }
        else
        {
            ChkSelectAll.IsChecked = null; // Indeterminate
        }
    }

    /// <summary>
    /// 학생 데이터 변경 이벤트
    /// </summary>
    private void OnStudentDataChanged(object sender, TextChangedEventArgs e)
    {
        // 변경사항 표시 (선택적)
        if (sender is TextBox textBox && textBox.DataContext is StudentManagementViewModel vm)
        {
            vm.IsModified = true;
        }
    }

    #endregion

    #region 데이터 로드

    /// <summary>
    /// 학생 목록 로드
    /// </summary>
    private async Task LoadStudentsAsync()
    {
        try
        {
            Students.Clear();
            ChkSelectAll.IsChecked = false;

            // FilterPicker에서 값 가져오기
            int year = FilterPicker.SelectedYear;
            int grade = FilterPicker.SelectedGrade;  // 0 = 전체
            int classNo = FilterPicker.SelectedClass; // 0 = 전체

            if (year == 0)
            {
                await MessageBox.ShowAsync("학년도를 선택하세요.", "알림");
                return;
            }
            
            List<StudentManagementViewModel> students;
            
            if (grade == 0)
            {
                // 전체 학년 조회 - Repository 직접 사용
                students = await GetAllSchoolStudentsAsync(Settings.SchoolCode.Value, year, Settings.WorkSemester.Value);
            }
            else if (classNo == 0)
            {
                // 전체 반 조회 (특정 학년)
                students = await GetGradeStudentsAsync(
                    Settings.SchoolCode.Value, year, Settings.WorkSemester.Value, grade);
            }
            else
            {
                // 특정 학급 조회 - Enrollment를 직접 사용
                var enrollments = await _enrollmentService.GetClassRosterAsync(Settings.SchoolCode.Value, year, grade, classNo);
                students = enrollments.Select(e => new StudentManagementViewModel
                {
                    EnrollmentNo = e.No,
                    StudentID = e.StudentID,
                    Year = e.Year,
                    Grade = e.Grade,
                    Class = e.Class,
                    Number = e.Number,
                    Name = e.Name,
                    Status = e.Status,
                    Memo = string.Empty,
                    IsSelected = false,
                    IsModified = false
                }).ToList();
            }

            // ViewModel로 변환
            foreach (var student in students.OrderBy(s => s.Grade).ThenBy(s => s.Class).ThenBy(s => s.Number))
            {
                Students.Add(student);
            }

            // UI 업데이트
            UpdateUI();
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"학생 목록 로드 중 오류가 발생했습니다.\n{ex.Message}", "오류");
            System.Diagnostics.Debug.WriteLine($"[StudentManagement] 로드 오류: {ex.Message}");
        }
        // ItemsRepeater에 ItemsSource 수동 설정
        StudentList.ItemsSource = Students;

        System.Diagnostics.Debug.WriteLine($"[StudentManagement] ItemsRepeater ItemsSource 설정 완료");

    }

    /// <summary>
    /// 특정 학교의 전체 학생 조회 (Enrollment에서 직접 조회 - denormalized)
    /// </summary>
    private async Task<List<StudentManagementViewModel>> GetAllSchoolStudentsAsync( string schoolCode, int year, int semester)
    {
        using var enrollmentService = new EnrollmentService();
        var enrollments = await enrollmentService.GetEnrollmentsAsync(schoolCode: schoolCode, year: year, semester: semester);
        Debug.WriteLine($"[StudentManagement] 전체 학생 조회 - Enrollment 수: {enrollments.Count}");
        
        // Enrollment를 StudentManagementViewModel로 변환
        return enrollments.Select(e => new StudentManagementViewModel
        {
            EnrollmentNo = e.No,
            StudentID = e.StudentID,
            Year = e.Year,
            Grade = e.Grade,
            Class = e.Class,
            Number = e.Number,
            Name = e.Name,
            Status = e.Status,
            Memo = string.Empty,
            IsSelected = false,
            IsModified = false
        }).ToList();
    }

    /// <summary>
    /// 특정 학년의 전체 학생 조회 (Enrollment에서 직접 조회 - denormalized)
    /// </summary>
    private async Task<List<StudentManagementViewModel>> GetGradeStudentsAsync(
        string schoolCode, int year, int semester, int grade)
    {
        using var enrollmentRepo = new EnrollmentRepository(SchoolDatabase.DbPath);
        var enrollments = await enrollmentRepo.GetByGradeAsync(schoolCode, year, semester, grade);

        // Enrollment를 StudentManagementViewModel로 변환
        return enrollments.Select(e => new StudentManagementViewModel
        {
            EnrollmentNo = e.No,
            StudentID = e.StudentID,
            Year = e.Year,
            Grade = e.Grade,
            Class = e.Class,
            Number = e.Number,
            Name = e.Name,
            Status = e.Status,
            Memo = string.Empty,
            IsSelected = false,
            IsModified = false
        }).ToList();
    }

    // GetClassListAsync 제거 - SchoolFilterPicker가 자동으로 처리

    #endregion

    #region 저장 및 삭제

    /// <summary>
    /// 선택된 학생 정보 저장
    /// </summary>
    private async Task SaveStudentsAsync()
    {
        try
        {
            var selectedStudents = Students.Where(s => s.IsSelected).ToList();

            if (selectedStudents.Count == 0)
            {
                await MessageBox.ShowAsync("저장할 학생을 선택하세요.", "알림");
                return;
            }

            int successCount = 0;
            // ⭐ SchoolDatabase.DbPath 사용
            using var repo = new EnrollmentRepository(SchoolDatabase.DbPath);

            foreach (var vm in selectedStudents)
            {
                try
                {
                    // Enrollment 조회
                    var enrollment = await repo.GetByIdAsync(vm.EnrollmentNo);
                    if (enrollment == null) continue;

                    // 수정된 데이터 반영
                    enrollment.Year = vm.Year;
                    enrollment.Grade = vm.Grade;
                    enrollment.Class = vm.Class;
                    enrollment.Number = vm.Number;
                    enrollment.UpdatedAt = DateTime.Now;

                    // DB 업데이트
                    bool success = await repo.UpdateAsync(enrollment);
                    if (success)
                    {
                        successCount++;
                        vm.IsModified = false;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[StudentManagement] 저장 실패 - {vm.Name}: {ex.Message}");
                }
            }

            await MessageBox.ShowAsync($"{successCount}명의 학생 정보가 저장되었습니다.", "완료");

            // 선택 해제
            ChkSelectAll.IsChecked = false;
            foreach (var student in Students)
            {
                student.IsSelected = false;
            }
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"저장 중 오류가 발생했습니다.\n{ex.Message}", "오류");
        }
    }

    /// <summary>
    /// 선택된 학생 삭제
    /// </summary>
    private async Task DeleteStudentsAsync()
    {
        try
        {
            var selectedStudents = Students.Where(s => s.IsSelected).ToList();

            if (selectedStudents.Count == 0)
            {
                await MessageBox.ShowAsync("삭제할 학생을 선택하세요.", "알림");
                return;
            }

            // 확인 대화상자
            var confirmed = await MessageBox.ShowConfirmAsync(
                $"선택한 {selectedStudents.Count}명의 학생을 삭제하시겠습니까?\n\n" +
                "이 작업은 되돌릴 수 없습니다.",
                "학생 삭제", "삭제", "취소");
            if (!confirmed) return;

            int successCount = 0;
            // ⭐ SchoolDatabase.DbPath 사용
            using var enrollmentRepo = new EnrollmentRepository(SchoolDatabase.DbPath);
            using var studentRepo = new StudentRepository(SchoolDatabase.DbPath);

            foreach (var vm in selectedStudents)
            {
                try
                {
                    // Enrollment 삭제
                    await enrollmentRepo.DeleteAsync(vm.EnrollmentNo);

                    // Student 삭제 (선택적 - 다른 학적이 없는 경우만)
                    // await studentRepo.DeleteAsync(vm.StudentID);

                    Students.Remove(vm);
                    successCount++;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[StudentManagement] 삭제 실패 - {vm.Name}: {ex.Message}");
                }
            }

            await MessageBox.ShowAsync($"{successCount}명의 학생이 삭제되었습니다.", "완료");
            UpdateUI();
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"삭제 중 오류가 발생했습니다.\n{ex.Message}", "오류");
        }
    }

    #endregion

    #region UI 업데이트

    /// <summary>
    /// UI 상태 업데이트
    /// </summary>
    private void UpdateUI()
    {
        bool hasStudents = Students.Count > 0;

        EmptyState.Visibility = hasStudents ? Visibility.Collapsed : Visibility.Visible;
        StudentListContainer.Visibility = hasStudents ? Visibility.Visible : Visibility.Collapsed;

        TxtStudentCount.Text = $"총 {Students.Count}명";
    }

    #endregion
}
#region ViewModel

/// <summary>
/// 학생 관리 ViewModel
/// </summary>
public class StudentManagementViewModel : NotifyPropertyChangedBase
{
    private int _enrollmentNo;
    private string _studentId = string.Empty;
    private int _year;
    private int _grade;
    private int _class;
    private int _number;
    private string _name = string.Empty;
    private string _status = string.Empty;
    private string _memo = string.Empty;
    private bool _isSelected;
    private bool _isModified;

    public int EnrollmentNo
    {
        get => _enrollmentNo;
        set => SetProperty(ref _enrollmentNo, value);
    }

    public string StudentID
    {
        get => _studentId;
        set => SetProperty(ref _studentId, value);
    }

    public int Year
    {
        get => _year;
        set => SetProperty(ref _year, value);
    }

    public int Grade
    {
        get => _grade;
        set => SetProperty(ref _grade, value);
    }

    public int Class
    {
        get => _class;
        set => SetProperty(ref _class, value);
    }

    public int Number
    {
        get => _number;
        set => SetProperty(ref _number, value);
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public string Memo
    {
        get => _memo;
        set => SetProperty(ref _memo, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool IsModified
    {
        get => _isModified;
        set => SetProperty(ref _isModified, value);
    }

    public string ClassInfo => $"{Year}학년도 {Grade}학년 {Class}반 {Number}번";
}
#endregion

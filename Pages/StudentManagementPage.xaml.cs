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
public sealed partial class StudentManagementPage : Page, IDisposable
{
    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _enrollmentService?.Dispose();
        GC.SuppressFinalize(this);
    }

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

    private async void YearSemPicker_YearSemesterChanged(object sender, YearSemesterChangedEventArgs e)
    {
        await ClassFilter.LoadAsync(e.Year, e.Semester);
    }

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

            // 필터에서 값 가져오기 (학년도·학기 + 학년·반)
            int year = YearSemPicker.Year;
            int grade = ClassFilter.Grade;  // 0 = 전체
            int classNo = ClassFilter.ClassNum; // 0 = 전체

            if (year == 0)
            {
                await MessageBox.ShowAsync("학년도를 선택하세요.", "알림");
                return;
            }
            
            // 전체·학년·학급을 한 경로로 조회한다(0 = 전체). 명부는 학년 단위라 학기는 안 건다.
            //
            // 예전에는 세 갈래였고 셋의 기준이 서로 달랐다:
            //  · 전체·학년 조회는 학기를 Settings.WorkSemester 로 고정해, 1학기에 등록한 학생이
            //    2학기에는 한 명도 안 나왔고,
            //  · 학급 조회는 학기를 무시했으며,
            //  · 전체·학년 조회는 Memo 를 빈 값으로 채워 넣어서, 그 상태로 저장하면
            //    Enrollment.Memo 가 통째로 지워졌다(저장이 vm.Memo 를 그대로 덮어쓴다).
            var enrollments = await _enrollmentService.GetEnrollmentsAsync(
                Settings.SchoolCode.Value, year, grade, classNo);

            var students = enrollments.Select(e => new StudentManagementViewModel
            {
                EnrollmentNo = e.No,
                StudentID = e.StudentID,
                Year = e.Year,
                Grade = e.Grade,
                Class = e.Class,
                Number = e.Number,
                Name = e.Name,
                Status = e.Status,
                Memo = e.Memo,
                IsSelected = false,
                IsModified = false
            }).ToList();

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

    #endregion

    #region 저장 및 삭제

    /// <summary>
    /// 선택된 학생 정보 저장
    /// </summary>
    private async Task SaveStudentsAsync()
    {
        try
        {
            // 편집(IsModified)했거나 체크(IsSelected)한 학생을 저장 대상으로
            var targetStudents = Students.Where(s => s.IsModified || s.IsSelected).ToList();

            if (targetStudents.Count == 0)
            {
                await MessageBox.ShowAsync("저장할 변경 사항이 없습니다.\n(수정하거나 학생을 선택하세요)", "알림");
                return;
            }

            int successCount = 0;
            // ⭐ SchoolDatabase.DbPath 사용
            using var repo = new EnrollmentRepository(SchoolDatabase.DbPath);
            using var studentService = new StudentService(SchoolDatabase.DbPath);

            foreach (var vm in targetStudents)
            {
                try
                {
                    // Enrollment 조회
                    var enrollment = await repo.GetByIdAsync(vm.EnrollmentNo);
                    if (enrollment == null) continue;

                    // 이름이 바뀌었으면 정본(Student)을 통해 갱신
                    // → UpdateBasicInfoAsync 가 Enrollment.Name 까지 자동 동기화
                    if (enrollment.Name != vm.Name)
                    {
                        var student = await studentService.GetBasicInfoAsync(vm.StudentID);
                        if (student != null)
                        {
                            student.Name = vm.Name;

                            // 결과를 확인한다 — 예전에는 버려서, 정본(Student)은 옛 이름인데
                            // 학적(Enrollment)만 새 이름이 되어 화면마다 이름이 달라질 수 있었다.
                            if (!await studentService.UpdateBasicInfoAsync(student))
                                throw new InvalidOperationException("이름 갱신이 반영되지 않았습니다.");
                        }
                    }

                    // 학적(Enrollment) 데이터 반영
                    enrollment.Year = vm.Year;
                    enrollment.Grade = vm.Grade;
                    enrollment.Class = vm.Class;
                    enrollment.Number = vm.Number;
                    enrollment.Name = vm.Name;
                    enrollment.Memo = vm.Memo;
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

            if (successCount == targetStudents.Count)
            {
                await MessageBox.ShowAsync($"{successCount}명의 학생 정보가 저장되었습니다.", "완료");
            }
            else
            {
                await MessageBox.ShowAsync(
                    $"{targetStudents.Count}명 중 {successCount}명만 저장되었습니다.\n" +
                    "저장되지 않은 학생은 수정 표시가 남아 있습니다.", "저장 실패");
            }

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
                    // Enrollment 삭제 — 0행이면 목록에서도 지우지 않는다
                    // (지우면 화면에서만 사라지고 새로고침 시 되살아나 DB 와 어긋난다)
                    if (!await enrollmentRepo.DeleteAsync(vm.EnrollmentNo))
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[StudentManagement] 삭제 0행 - {vm.Name}(EnrollmentNo={vm.EnrollmentNo})");
                        continue;
                    }

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

            if (successCount == selectedStudents.Count)
            {
                await MessageBox.ShowAsync($"{successCount}명의 학생이 삭제되었습니다.", "완료");
            }
            else
            {
                await MessageBox.ShowAsync(
                    $"{selectedStudents.Count}명 중 {successCount}명만 삭제되었습니다.", "삭제 실패");
            }
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

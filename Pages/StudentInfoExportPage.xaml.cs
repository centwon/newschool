using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Controls;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.Helpers;
using NewSchool.Services; // ExcelHelpers용

namespace NewSchool.Pages;

// 필요한 using 추가

/// <summary>
/// 학생 정보 출력 페이지 (WinUI3 버전)
/// </summary>
public sealed partial class StudentInfoExportPage : Page
{
    #region Fields

    private DataTable? _data;
    private int _selectedYear;
    private int _selectedGrade;
    private int _selectedClass;

    private readonly EnrollmentRepository _enrollmentRepo;
    private readonly StudentRepository _studentRepo;
    private readonly StudentDetailRepository _detailRepo;

    #endregion

    #region Constructor

    public StudentInfoExportPage()
    {
        InitializeComponent();

        // Repository 초기화
        string dbPath = System.IO.Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            Settings.SchoolDB.Value
        );

        _enrollmentRepo = new EnrollmentRepository(dbPath);
        _studentRepo = new StudentRepository(dbPath);
        _detailRepo = new StudentDetailRepository(dbPath);

        Loaded += PageExport_Loaded;
        Unloaded += PageExport_Unloaded;
    }

    #endregion

    #region Lifecycle

    private async void PageExport_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadYearsAsync();
    }

    private void PageExport_Unloaded(object sender, RoutedEventArgs e)
    {
        _enrollmentRepo?.Dispose();
        _studentRepo?.Dispose();
        _detailRepo?.Dispose();
    }

    #endregion

    #region UI Initialization

    /// <summary>
    /// 학년도 목록 로드
    /// </summary>
    private async Task LoadYearsAsync()
    {
        try
        {
            using var EnrollmentService = new EnrollmentService();
            var years = await EnrollmentService.GetYearListAsync(Settings.SchoolCode.Value);

            if (!years.Any())
            {
                await MessageBox.ShowAsync("등록된 학년도가 없습니다.");
                return;
            }

            CBoxYear.ItemsSource = years;

            // 현재 작업 학년도 선택
            if (years.Contains(Settings.WorkYear.Value))
            {
                CBoxYear.SelectedItem = Settings.WorkYear.Value;
            }
            else
            {
                CBoxYear.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"학년도 로드 실패: {ex.Message}");
        }
    }

    #endregion

    #region ComboBox Events

    private async void CBoxYear_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CBoxYear.SelectedItem == null) return;

        _selectedYear = (int)CBoxYear.SelectedItem;
        await LoadGradesAsync();
    }

    private async void CBoxGrades_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CBoxGrades.SelectedItem == null) return;

        _selectedGrade = (int)CBoxGrades.SelectedItem;
        await LoadClassesAsync();
    }

    private void CBoxClasses_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CBoxClasses.SelectedItem == null) return;

        if (CBoxClasses.SelectedIndex == 0) // "전체" 선택
        {
            _selectedClass = 0;
            ChkShowClass.IsChecked = true;
        }
        else
        {
            _selectedClass = (int)CBoxClasses.SelectedItem;
        }
    }

    /// <summary>
    /// 학년 목록 로드
    /// </summary>
    private async Task LoadGradesAsync()
    {
        try
        {
            using var EnrollmentService = new EnrollmentService();
            var grades = await EnrollmentService.GetGradeListByYearAsync(year:_selectedYear, schoolCode:Settings.SchoolCode.Value);

            if (!grades.Any())
            {
                await MessageBox.ShowAsync("해당 학년도에 등록된 학년이 없습니다.");
                return;
            }

            CBoxGrades.ItemsSource = grades;

            if (_selectedYear == Settings.WorkYear.Value &&
                grades.Contains(Settings.HomeGrade.Value))
            {
                CBoxGrades.SelectedItem = Settings.HomeGrade.Value;
            }
            else
            {
                CBoxGrades.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"학년 로드 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 학급 목록 로드
    /// </summary>
    private async Task LoadClassesAsync()
    {
        try
        {
            using var EnrollmentService = new EnrollmentService();
            var classlist = await EnrollmentService.GetClassListAsync(
                schoolCode: Settings.SchoolCode.Value,
                year: _selectedYear,
                grade: _selectedGrade
            );


            if (!classlist.Any())
            {
                await MessageBox.ShowAsync("해당 학년에 등록된 학급이 없습니다.");
                return;
            }

            var classItems = new List<object> { "전체" };
            classItems.AddRange(classlist.Cast<object>());

            CBoxClasses.ItemsSource = classItems;

            if (_selectedYear == Settings.WorkYear.Value &&
                _selectedGrade == Settings.HomeGrade.Value &&
                classlist.Contains(Settings.HomeRoom.Value))
            {
                CBoxClasses.SelectedItem = Settings.HomeRoom.Value;
            }
            else
            {
                CBoxClasses.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"학급 로드 실패: {ex.Message}");
        }
    }

    #endregion

    #region Button Events

    private async void BtnPreview_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateSelection())
        {
            return;
        }

        try
        {
            await MakeDataAsync();
            MakePreview();
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"미리보기 생성 실패: {ex.Message}");
        }
    }

    private async void BtnExport_Click(object sender, RoutedEventArgs e)
    {
        if (_data == null)
        {
            await MessageBox.ShowAsync("먼저 미리보기를 생성해주세요.");
            return;
        }

        if (RbToExcel.IsChecked == true)
        {
            await ExportToExcelAsync();
        }
        else if (RbToPrinter.IsChecked == true)
        {
            await PrintAsync();
        }
        else
        {
            await MessageBox.ShowAsync("출력 대상을 선택해주세요.");
        }
    }

    #endregion

    #region Validation

    private bool ValidateSelection()
    {
        if (CBoxYear.SelectedItem == null)
        {
            _ = MessageBox.ShowAsync("학년도를 선택해주세요.");
            return false;
        }

        if (CBoxGrades.SelectedItem == null)
        {
            _ = MessageBox.ShowAsync("학년을 선택해주세요.");
            return false;
        }

        if (CBoxClasses.SelectedItem == null)
        {
            _ = MessageBox.ShowAsync("학급을 선택해주세요.");
            return false;
        }

        return true;
    }

    #endregion

    #region Data Generation

    /// <summary>
    /// DataTable 생성
    /// </summary>
    private async Task MakeDataAsync()
    {
        _data = new DataTable();

        // 연번
        if (ChkShowNo.IsChecked == true)
        {
            _data.Columns.Add("연번", typeof(int));
        }

        // 학급
        if (ChkShowClass.IsChecked == true)
        {
            _data.Columns.Add("학급", typeof(int));
        }

        // 번호, 이름 (필수)
        _data.Columns.Add("번호", typeof(int));
        _data.Columns.Add("이름", typeof(string));

        // 선택된 항목들 (CheckBox Tag 기준)
        var selectedItems = GetAllCheckBoxes(PanItem)
            .Where(chk => chk.IsChecked == true)
            .Select(chk => new
            {
                Tag = chk.Tag as string ?? "",
                Content = chk.Content as string ?? ""
            })
            .ToList();

        // Native AOT 호환 - 직접 타입 지정으로 열 추가
        foreach (var item in selectedItems)
        {
            switch (item.Tag)
            {
                case "Birth":
                    _data.Columns.Add(item.Content, typeof(DateTime));
                    break;
                default:
                    _data.Columns.Add(item.Content, typeof(string));
                    break;
            }
        }

        // 사용자 정의 항목
        if (ExpanderUserItem.IsExpanded)
        {
            var userItems = new[]
            {
                TBoxUser1, TBoxUser2, TBoxUser3, TBoxUser4, TBoxUser5,
                TBoxUser6, TBoxUser7, TBoxUser8, TBoxUser9, TBoxUser10
            }
            .Where(tb => !string.IsNullOrWhiteSpace(tb.Text))
            .Select(tb => tb.Text)
            .ToList();

            foreach (var item in userItems)
            {
                _data.Columns.Add(item, typeof(string));
            }
        }

        // 비고
        if (ChkShowEtc.IsChecked == true)
        {
            _data.Columns.Add("비고", typeof(string));
        }

        // 데이터 로드
        await LoadStudentDataAsync(selectedItems.Select(i => i.Tag).ToList());
    }

    /// <summary>
    /// 학생 데이터 로드
    /// </summary>
    private async Task LoadStudentDataAsync(List<string> selectedTags)
    {
        if (_data == null)
            throw new InvalidOperationException("_data가 null입니다. MakeDataAsync에서 초기화되었는지 확인하세요.");

        string schoolCode = Settings.SchoolCode.Value;

        // Enrollment 조회
        List<Enrollment> enrollments;
        using var EnrollmentService = new EnrollmentService();
        enrollments = await EnrollmentService.GetEnrollmentsAsync(schoolCode: schoolCode, year: _selectedYear, classnum: _selectedGrade);

        // 번호순 정렬
        enrollments = enrollments.OrderBy(e => e.Number).ToList();

        // 각 학생별로 데이터 생성
        for (int i = 0; i < enrollments.Count; i++)
        {
            var enrollment = enrollments[i];

            // Student 정보 조회
            using var StudentService = new StudentService(SchoolDatabase.DbPath);
            var student = await StudentService.GetBasicInfoAsync(enrollment.StudentID);
            if (student == null) continue;

            // StudentDetail 정보 조회 (선택적)
            StudentDetail? detail = null;
            if (selectedTags.Any(tag => IsDetailProperty(tag)))
            {
                using var service = new StudentDetailService(SchoolDatabase.DbPath);
                detail = await service.GetByStudentIdAsync(enrollment.StudentID);
            }

            // DataRow 생성
            DataRow row = _data.NewRow();
            int colIndex = 0;

            // 연번
            if (ChkShowNo.IsChecked == true)
            {
                row[colIndex++] = i + 1;
            }

            // 학급
            if (ChkShowClass.IsChecked == true)
            {
                row[colIndex++] = enrollment.Class;
            }

            // 번호, 이름
            row[colIndex++] = enrollment.Number;
            row[colIndex++] = student.Name;

            // 선택된 항목들
            foreach (var tag in selectedTags)
            {
                object? value = GetPropertyValue(tag, student, detail);
                if (value != null)
                {
                    row[colIndex] = value;
                }
                colIndex++;
            }

            _data.Rows.Add(row);
        }
    }

    /// <summary>
    /// Native AOT 호환 - Reflection 대신 switch 사용
    /// 속성값 가져오기
    /// </summary>
    private object? GetPropertyValue(string tag, Student student, StudentDetail? detail)
    {
        return tag switch
        {
            // Student 속성
            "Sex" => student.Sex,
            "Birth" => student.BirthDate,
            "Email" => student.Email,
            "Phone" => student.Phone,
            "Address" => student.Address,
            "Remark" => student.Memo,

            // StudentDetail 속성
            "FatherName" => detail?.FatherName,
            "FatherPhone" => detail?.FatherPhone,
            "MotherName" => detail?.MotherName,
            "MotherPhone" => detail?.MotherPhone,
            "Family" => detail?.FamilyInfo,
            "Friends" => detail?.Friends,
            "Interest" => detail?.Interests,
            "CareerHope" => detail?.CareerGoal,


            _ => null
        };
    }

    /// <summary>
    /// StudentDetail 속성인지 확인
    /// </summary>
    private bool IsDetailProperty(string tag)
    {
        return tag switch
        {
            "FatherName" or "FatherPhone" or "MotherName" or "MotherPhone"
            or "Family" or "Friends" or "Interest" or "CareerHope" => true,
            _ => false
        };
    }

    #endregion

    #region HTML Generation & Preview

    /// <summary>
    /// 미리보기 생성
    /// </summary>
    private void MakePreview()
    {
        if (_data == null) return;

        string html = GenerateHtml();
        PreviewEditor.Text = html;
    }

    /// <summary>
    /// HTML 생성
    /// </summary>
    private string GenerateHtml()
    {
        if (_data == null) return string.Empty;

        var sb = new StringBuilder();

        // 제목
        string title = string.IsNullOrWhiteSpace(TboxTitle.Text)
            ? "학생 정보"
            : TboxTitle.Text;
        sb.Append($"<h1 style='text-align:center; font-size:16pt; margin-bottom:10px;'>{HtmlEncode(title)}</h1>");

        // 학급 정보
        string classInfo = ChkShowClass.IsChecked != true
            ? $"{_selectedGrade}학년 {_selectedClass}반"
            : $"{_selectedGrade}학년";
        sb.Append($"<p style='text-align:right; font-size:12pt; margin-bottom:20px;'>{HtmlEncode(classInfo)}</p>");

        // 프린트 스타일
        sb.Append(@"
<style>
    body { 
        font-family: 'Malgun Gothic', 'Noto Sans KR', sans-serif;
        margin: 0;
        padding: 20px;
    }
    table { 
        width: 100%; 
        border-collapse: collapse; 
        font-size: 10pt; 
        margin-top: 10px;
    }
    th, td { 
        border: 1px solid #000; 
        padding: 6px 4px;
        text-align: center; 
    }
    th { 
        background-color: #f0f0f0; 
        font-weight: bold;
    }
    @media print {
        @page { 
            size: A4; 
            margin: 20mm; 
        }
        body {
            margin: 0;
            padding: 0;
        }
    }
</style>");

        // 테이블 헤더
        sb.Append("<table><thead><tr>");

        foreach (DataColumn column in _data.Columns)
        {
            sb.Append($"<th>{HtmlEncode(column.ColumnName)}</th>");
        }

        sb.Append("</tr></thead><tbody>");

        // 테이블 데이터
        foreach (DataRow row in _data.Rows)
        {
            sb.Append("<tr>");

            foreach (var item in row.ItemArray)
            {
                string value = string.Empty;

                if (item is DateTime dt && !dt.Equals(default(DateTime)))
                {
                    value = dt.ToString("yyyy.M.d.");
                }
                else
                {
                    value = item?.ToString() ?? string.Empty;
                }

                sb.Append($"<td>{HtmlEncode(value)}</td>");
            }

            sb.Append("</tr>");
        }

        sb.Append("</tbody></table>");

        return sb.ToString();
    }

    /// <summary>
    /// HTML 인코딩
    /// </summary>
    private string HtmlEncode(string text)
    {
        return System.Net.WebUtility.HtmlEncode(text);
    }

    #endregion

    #region Export

    /// <summary>
    /// 프린터 출력
    /// </summary>
    private async Task PrintAsync()
    {
        try
        {
            if (!PreviewEditor.IsInitialized)
            {
                await MessageBox.ShowAsync("미리보기를 먼저 생성해주세요.");
                return;
            }

            // JoditEditor의 Public 메서드를 통한 프린트
            await PreviewEditor.PrintAsync();
        }
        catch (InvalidOperationException ex)
        {
            await MessageBox.ShowAsync($"프린트 오류: {ex.Message}");
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"프린트 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 엑셀 파일로 내보내기 (ExcelHelpers 사용)
    /// </summary>
    private async Task ExportToExcelAsync()
    {
        if (_data == null) return;

        try
        {
            string title = string.IsNullOrWhiteSpace(TboxTitle.Text)
                ? "학생정보"
                : TboxTitle.Text;

            string subtitle = _selectedClass == 0
                ? $"{_selectedYear}학년도 {_selectedGrade}학년 전체"
                : $"{_selectedYear}학년도 {_selectedGrade}학년 {_selectedClass}반";

            var window = App.MainWindow;
            if (window == null)
            {
                await MessageBox.ShowAsync("메인 윈도우를 찾을 수 없습니다.");
                return;
            }

            // ExcelHelpers를 사용하여 Excel 저장
            bool success = await ExcelHelpers.SaveDataTableToExcelAsync(
                window,
                _data,
                title: title,
                subtitle: subtitle,
                openAfterSave: true
            );

            if (!success)
            {
                await MessageBox.ShowAsync("엑셀 저장이 취소되었거나 실패했습니다.");
            }
        }
        catch (Exception ex)
        {
            await MessageBox.ShowAsync($"엑셀 내보내기 실패: {ex.Message}");
        }
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// StackPanel의 모든 자식 CheckBox 가져오기 (재귀적으로)
    /// </summary>
    private List<CheckBox> GetAllCheckBoxes(Panel panel)
    {
        var checkBoxes = new List<CheckBox>();

        foreach (var child in panel.Children)
        {
            if (child is CheckBox checkBox)
            {
                checkBoxes.Add(checkBox);
            }
            else if (child is Panel childPanel)
            {
                checkBoxes.AddRange(GetAllCheckBoxes(childPanel));
            }
        }

        return checkBoxes;
    }

    /// <summary>
    /// 메시지 다이얼로그 표시
    /// </summary>

    #endregion
}

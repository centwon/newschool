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
using NewSchool.Services;

namespace NewSchool.Pages;

/// <summary>
/// 학생 정보 출력 페이지 (WinUI3 버전)
/// SchoolFilterPicker 사용, Student/StudentDetail 모델 기반 출력항목
/// </summary>
public sealed partial class StudentInfoExportPage : Page
{
    #region Fields

    private DataTable? _data;

    #endregion

    #region Constructor

    public StudentInfoExportPage()
    {
        InitializeComponent();
    }

    #endregion

    #region SchoolFilterPicker Event

    private void SchoolFilter_SelectionChanged(object sender, SchoolFilterChangedEventArgs e)
    {
        // 학급 변경 시 "학급 표시" 체크박스 자동 설정
        if (e.IsAllClass)
        {
            ChkShowClass.IsChecked = true;
        }
    }

    #endregion

    #region Validation

    private bool ValidateSelection()
    {
        if (SchoolFilter.SelectedYear == 0 || SchoolFilter.SelectedGrade == 0)
        {
            _ = MessageBox.ShowAsync("학년도와 학년을 선택해주세요.");
            return false;
        }
        return true;
    }

    #endregion

    #region Button Events

    private async void BtnPreview_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateSelection()) return;

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
                TBoxUser1, TBoxUser2, TBoxUser3, TBoxUser4, TBoxUser5
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
            throw new InvalidOperationException("_data가 null입니다.");

        string schoolCode = Settings.SchoolCode.Value;
        int year = SchoolFilter.SelectedYear;
        int grade = SchoolFilter.SelectedGrade;
        int classNo = SchoolFilter.SelectedClass; // 0이면 전체

        // Enrollment 조회 (grade 파라미터 사용)
        using var enrollmentService = new EnrollmentService();
        var enrollments = await enrollmentService.GetEnrollmentsAsync(
            schoolCode: schoolCode,
            year: year,
            grade: grade,
            classnum: classNo);

        // 번호순 정렬 (학급별일 때는 학급→번호 순)
        enrollments = classNo == 0
            ? enrollments.OrderBy(e => e.Class).ThenBy(e => e.Number).ToList()
            : enrollments.OrderBy(e => e.Number).ToList();

        // StudentDetail 필요 여부 확인
        bool needDetail = selectedTags.Any(tag => IsDetailProperty(tag));

        // 학생 정보 일괄 조회 (N+1 쿼리 방지)
        var studentIds = enrollments.Select(e => e.StudentID).ToList();

        using var studentService = new StudentService(SchoolDatabase.DbPath);
        var allStudents = await studentService.GetStudentsByIdsAsync(studentIds);
        var studentDict = allStudents.ToDictionary(s => s.StudentID, s => s);

        Dictionary<string, StudentDetail> detailDict = [];
        if (needDetail)
        {
            using var detailService = new StudentDetailService(SchoolDatabase.DbPath);
            var allDetails = await detailService.GetByStudentIdsAsync(studentIds);
            detailDict = allDetails.ToDictionary(d => d.StudentID, d => d);
        }

        // 각 학생별로 데이터 생성
        for (int i = 0; i < enrollments.Count; i++)
        {
            var enrollment = enrollments[i];

            // Student 정보 조회 (Dictionary에서 O(1))
            if (!studentDict.TryGetValue(enrollment.StudentID, out var student)) continue;

            // StudentDetail 정보 조회 (Dictionary에서 O(1))
            StudentDetail? detail = null;
            if (needDetail)
            {
                detailDict.TryGetValue(enrollment.StudentID, out detail);
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
            "Phone" => student.Phone,
            "Email" => student.Email,
            "Address" => student.Address,
            "Memo" => student.Memo,

            // StudentDetail - 보호자
            "GuardianName" => detail?.GuardianName,
            "GuardianRelation" => detail?.GuardianRelation,
            "GuardianPhone" => detail?.GuardianPhone,
            "FatherName" => detail?.FatherName,
            "FatherPhone" => detail?.FatherPhone,
            "FatherJob" => detail?.FatherJob,
            "MotherName" => detail?.MotherName,
            "MotherPhone" => detail?.MotherPhone,
            "MotherJob" => detail?.MotherJob,

            // StudentDetail - 학생 특성
            "Interest" => detail?.Interests,
            "Talents" => detail?.Talents,
            "CareerHope" => detail?.CareerGoal,
            "Family" => detail?.FamilyInfo,
            "Friends" => detail?.Friends,

            // StudentDetail - 건강/특이
            "HealthInfo" => detail?.HealthInfo,
            "Allergies" => detail?.Allergies,
            "SpecialNeeds" => detail?.SpecialNeeds,

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
            "GuardianName" or "GuardianRelation" or "GuardianPhone"
            or "FatherName" or "FatherPhone" or "FatherJob"
            or "MotherName" or "MotherPhone" or "MotherJob"
            or "Family" or "Friends" or "Interest" or "Talents"
            or "CareerHope" or "HealthInfo" or "Allergies"
            or "SpecialNeeds" => true,
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
    /// HTML 생성 (출력 방향 지원)
    /// </summary>
    private string GenerateHtml()
    {
        if (_data == null) return string.Empty;

        bool isLandscape = RbLandscape.IsChecked == true;
        string pageSize = isLandscape ? "A4 landscape" : "A4";
        string fontSize = isLandscape ? "9pt" : "10pt";
        string cellPadding = isLandscape ? "4px 3px" : "6px 4px";

        var sb = new StringBuilder();

        // 제목
        string title = string.IsNullOrWhiteSpace(TboxTitle.Text)
            ? "학생 정보"
            : TboxTitle.Text;
        sb.Append($"<h1 style='text-align:center; font-size:16pt; margin-bottom:10px;'>{HtmlEncode(title)}</h1>");

        // 학급 정보
        int year = SchoolFilter.SelectedYear;
        int grade = SchoolFilter.SelectedGrade;
        int classNo = SchoolFilter.SelectedClass;

        string classInfo = classNo == 0
            ? $"{year}학년도 {grade}학년"
            : $"{year}학년도 {grade}학년 {classNo}반";
        sb.Append($"<p style='text-align:right; font-size:12pt; margin-bottom:20px;'>{HtmlEncode(classInfo)}</p>");

        // 프린트 스타일
        sb.Append($@"
<style>
    body {{
        font-family: 'Malgun Gothic', 'Noto Sans KR', sans-serif;
        margin: 0;
        padding: 20px;
    }}
    table {{
        width: 100%;
        border-collapse: collapse;
        font-size: {fontSize};
        margin-top: 10px;
    }}
    th, td {{
        border: 1px solid #000;
        padding: {cellPadding};
        text-align: center;
    }}
    th {{
        background-color: #f0f0f0;
        font-weight: bold;
    }}
    @media print {{
        @page {{
            size: {pageSize};
            margin: 15mm;
        }}
        body {{
            margin: 0;
            padding: 0;
        }}
    }}
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

            int year = SchoolFilter.SelectedYear;
            int grade = SchoolFilter.SelectedGrade;
            int classNo = SchoolFilter.SelectedClass;

            string subtitle = classNo == 0
                ? $"{year}학년도 {grade}학년 전체"
                : $"{year}학년도 {grade}학년 {classNo}반";

            var window = App.MainWindow;
            if (window == null)
            {
                await MessageBox.ShowAsync("메인 윈도우를 찾을 수 없습니다.");
                return;
            }

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

    #endregion
}

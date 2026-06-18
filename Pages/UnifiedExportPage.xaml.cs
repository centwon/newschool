using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Controls;
using NewSchool.Services;

namespace NewSchool.Pages;

/// <summary>
/// 통합 내보내기 페이지 — 누가기록·학생부를 Excel/PDF/HTML로 일괄 출력.
/// </summary>
public sealed partial class UnifiedExportPage : Page
{
    private string? _lastFilePath;

    public UnifiedExportPage()
    {
        InitializeComponent();
    }

    private async void BtnPreview_Click(object sender, RoutedEventArgs e)
    {
        int year = SchoolFilter.SelectedYear;
        int grade = SchoolFilter.SelectedGrade;
        int classNo = SchoolFilter.SelectedClass;

        if (year == 0 || grade == 0 || classNo == 0)
        {
            await MessageBox.ShowAsync("학년도, 학년, 반을 모두 선택해주세요.");
            return;
        }

        var dataType = GetSelectedDataType();

        SetBusy(true, "미리보기 생성 중...");
        try
        {
            var service = new UnifiedExportService();
            var html = await service.PreviewClassAsync(dataType, year, grade, classNo);

            if (string.IsNullOrEmpty(html))
            {
                PreviewEditor.Text = string.Empty;
                SetBusy(false, "해당 조건에 맞는 데이터가 없습니다.");
                return;
            }

            PreviewEditor.Text = html;
            SetBusy(false, "미리보기 준비 완료");
        }
        catch (Exception ex)
        {
            SetBusy(false, "");
            await MessageBox.ShowAsync($"미리보기 실패: {ex.Message}");
        }
    }

    private async void BtnExport_Click(object sender, RoutedEventArgs e)
    {
        int year = SchoolFilter.SelectedYear;
        int grade = SchoolFilter.SelectedGrade;
        int classNo = SchoolFilter.SelectedClass;

        if (year == 0 || grade == 0 || classNo == 0)
        {
            await MessageBox.ShowAsync("학년도, 학년, 반을 모두 선택해주세요.");
            return;
        }

        var dataType = GetSelectedDataType();

        UnifiedExportService.ExportFormat format;
        if (RbFmtExcel.IsChecked == true) format = UnifiedExportService.ExportFormat.Excel;
        else if (RbFmtPdf.IsChecked == true) format = UnifiedExportService.ExportFormat.Pdf;
        else if (RbFmtCsv?.IsChecked == true) format = UnifiedExportService.ExportFormat.Csv;
        else format = UnifiedExportService.ExportFormat.Html;

        await ExportAsync(dataType, format, year, grade, classNo);
    }

    /// <summary>
    /// 누가기록/학생부 데이터를 CSV(TSV 대용)로 빌드해 클립보드에 복사한다.
    /// 엑셀/구글 시트에 바로 붙여넣기 가능.
    /// </summary>
    private async void BtnCopyClipboard_Click(object sender, RoutedEventArgs e)
    {
        int year = SchoolFilter.SelectedYear;
        int grade = SchoolFilter.SelectedGrade;
        int classNo = SchoolFilter.SelectedClass;

        if (year == 0 || grade == 0 || classNo == 0)
        {
            await MessageBox.ShowAsync("학년도, 학년, 반을 모두 선택해주세요.");
            return;
        }

        var dataType = GetSelectedDataType();
        if (dataType == UnifiedExportService.DataType.Seats ||
            dataType == UnifiedExportService.DataType.StudentCard)
        {
            await MessageBox.ShowAsync(
                "좌석배정/학생카드는 표 형태가 아니라 클립보드 복사를 지원하지 않습니다.\n" +
                "누가기록 또는 학생부 특기사항을 선택해 주세요.",
                "복사 불가");
            return;
        }

        SetBusy(true, "데이터를 불러오는 중...");
        try
        {
            var service = new UnifiedExportService();
            string? csv = dataType == UnifiedExportService.DataType.StudentSpec
                ? await service.BuildClassSpecsCsvAsync(year, grade, classNo)
                : await service.BuildClassLogsCsvAsync(year, grade, classNo);

            if (string.IsNullOrEmpty(csv))
            {
                SetBusy(false, "해당 조건에 맞는 데이터가 없습니다.");
                return;
            }

            var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dp.SetText(csv);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);

            int rowCount = 0;
            foreach (var ch in csv) if (ch == '\n') rowCount++; // 헤더 포함
            SetBusy(false, $"클립보드에 복사됨 ({rowCount}행)");
        }
        catch (Exception ex)
        {
            SetBusy(false, "");
            await Controls.UserErrorReporter.ReportAsync("클립보드 복사", ex);
        }
    }

    private async Task ExportAsync(
        UnifiedExportService.DataType dataType,
        UnifiedExportService.ExportFormat format,
        int year, int grade, int classNo)
    {
        SetBusy(true, "데이터를 불러오는 중...");
        ResultPanel.Visibility = Visibility.Collapsed;

        try
        {
            var service = new UnifiedExportService();
            var path = await service.ExportClassAsync(dataType, format, year, grade, classNo);

            if (string.IsNullOrEmpty(path))
            {
                SetBusy(false, "해당 조건에 맞는 데이터가 없습니다.");
                return;
            }

            _lastFilePath = path;
            TxtResultPath.Text = path;
            ResultPanel.Visibility = Visibility.Visible;
            SetBusy(false, $"{Path.GetFileName(path)} 생성 완료");

            // 자동으로 파일 열기
            TryOpen(path);
        }
        catch (Exception ex)
        {
            SetBusy(false, "");
            await MessageBox.ShowAsync($"내보내기 실패: {ex.Message}");
        }
    }

    private void SetBusy(bool busy, string status)
    {
        Busy.IsActive = busy;
        BtnExport.IsEnabled = !busy;
        BtnPreview.IsEnabled = !busy;
        TxtStatus.Text = status;
    }

    private void BtnOpenFile_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_lastFilePath))
            TryOpen(_lastFilePath);
    }

    private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_lastFilePath)) return;
        var dir = Path.GetDirectoryName(_lastFilePath);
        if (!string.IsNullOrEmpty(dir))
            TryOpen(dir);
    }

    private UnifiedExportService.DataType GetSelectedDataType()
    {
        if (RbStudentSpec.IsChecked == true) return UnifiedExportService.DataType.StudentSpec;
        if (RbSeats.IsChecked == true) return UnifiedExportService.DataType.Seats;
        if (RbStudentCard?.IsChecked == true) return UnifiedExportService.DataType.StudentCard;
        return UnifiedExportService.DataType.StudentLog;
    }

    /// <summary>
    /// 데이터 타입 변경 시 형식 옵션을 타입별로 제한한다.
    /// 좌석배정 = PDF/HTML 전용, 나머지는 Excel/PDF/HTML.
    /// </summary>
    private void DataType_Checked(object sender, RoutedEventArgs e)
    {
        if (RbFmtExcel == null) return;

        bool isSeats = RbSeats?.IsChecked == true;
        bool isCard = RbStudentCard?.IsChecked == true;
        bool tabularDisabled = isSeats || isCard;  // Excel/CSV는 표 데이터에만 해당

        RbFmtExcel.IsEnabled = !tabularDisabled;
        if (RbFmtCsv != null) RbFmtCsv.IsEnabled = !tabularDisabled;
        if (BtnCopyClipboard != null) BtnCopyClipboard.IsEnabled = !tabularDisabled;

        if (tabularDisabled)
        {
            if (RbFmtExcel.IsChecked == true) { RbFmtExcel.IsChecked = false; RbFmtPdf.IsChecked = true; }
            if (RbFmtCsv?.IsChecked == true)  { RbFmtCsv.IsChecked  = false; RbFmtPdf.IsChecked = true; }
        }
    }

    private static void TryOpen(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
            // 무시 — 경로는 UI에 표시됨
        }
    }
}

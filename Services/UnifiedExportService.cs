using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.ViewModels;

namespace NewSchool.Services;

/// <summary>
/// 통합 내보내기 서비스 — 데이터 타입 × 형식(Excel/PDF/HTML) 조합을
/// 기존 서비스로 위임해 단일 파일을 생성한다.
/// </summary>
public class UnifiedExportService
{
    public enum DataType
    {
        StudentLog,   // 누가기록
        StudentSpec,  // 학생부 특기사항
        Seats,        // 좌석배정 (Excel/PDF/HTML)
        StudentCard,  // 학생카드 (Excel/PDF/HTML)
        StudentInfo   // 학생정보 명렬 (Excel/PDF/HTML/CSV)
    }

    public enum ExportFormat
    {
        Excel,
        Pdf,
        Html,
        Csv   // ⭐ 표 형태 데이터 전용 (누가기록·학생부)
    }

    /// <summary>
    /// 학급 단위 일괄 내보내기. 파일 경로(없으면 null) 반환.
    /// </summary>
    public async Task<string?> ExportClassAsync(
        DataType dataType,
        ExportFormat format,
        int year, int grade, int classNo)
    {
        return dataType switch
        {
            DataType.StudentLog => await ExportClassLogsAsync(format, year, grade, classNo),
            DataType.StudentSpec => await ExportClassSpecsAsync(format, year, grade, classNo),
            DataType.Seats => await ExportClassSeatsAsync(format, year, grade, classNo),
            DataType.StudentCard => await ExportClassCardsAsync(format, year, grade, classNo),
            DataType.StudentInfo => await ExportClassInfoAsync(format, year, grade, classNo),
            _ => null
        };
    }

    /// <summary>
    /// 학급 단위 미리보기(HTML 문자열) 생성. 데이터 없으면 null.
    /// </summary>
    public async Task<string?> PreviewClassAsync(
        DataType dataType,
        int year, int grade, int classNo)
    {
        switch (dataType)
        {
            case DataType.StudentLog:
            {
                var data = await LoadClassLogsAsync(year, grade, classNo);
                if (data.Count == 0) return null;
                return new HtmlExportService()
                    .BuildClassLogsHtml(year, grade, classNo, data);
            }
            case DataType.StudentSpec:
            {
                var data = await LoadClassSpecsAsync(year, grade, classNo);
                if (data.Count == 0) return null;
                return new HtmlExportService()
                    .BuildClassSpecsHtml(year, grade, classNo, data);
            }
            case DataType.Seats:
            {
                return await new SeatsPrintService()
                    .BuildSeatsHtmlFromDbAsync(year, grade, classNo);
            }
            case DataType.StudentCard:
            {
                var data = await StudentCardPrintService.LoadClassStudentsAsync(year, grade, classNo);
                if (data.Count == 0) return null;
                return new HtmlExportService()
                    .BuildClassCardsHtml(year, grade, classNo, data);
            }
            case DataType.StudentInfo:
            {
                var data = await StudentCardPrintService.LoadClassStudentsAsync(year, grade, classNo);
                if (data.Count == 0) return null;
                return new HtmlExportService()
                    .BuildClassInfoHtml(year, grade, classNo, data);
            }
            default:
                return null;
        }
    }

    #region 좌석배정 (Excel/PDF/HTML)

    private static async Task<string?> ExportClassSeatsAsync(
        ExportFormat format, int year, int grade, int classNo)
    {
        var service = new SeatsPrintService();
        return format switch
        {
            ExportFormat.Excel => await service.GenerateSeatsExcelFromDbAsync(year, grade, classNo),
            ExportFormat.Pdf   => await service.GenerateSeatsPdfFromDbAsync(year, grade, classNo),
            ExportFormat.Html  => await service.GenerateSeatsHtmlFromDbAsync(year, grade, classNo),
            _ => null
        };
    }

    #endregion

    #region 학생카드 (Excel/PDF/HTML)

    private static async Task<string?> ExportClassCardsAsync(
        ExportFormat format, int year, int grade, int classNo)
    {
        if (format == ExportFormat.Excel)
        {
            return await new StudentCardPrintService()
                .GenerateClassCardsExcelFromDbAsync(year, grade, classNo);
        }
        if (format == ExportFormat.Pdf)
        {
            return await new StudentCardPrintService()
                .GenerateClassCardsPdfFromDbAsync(year, grade, classNo);
        }
        if (format == ExportFormat.Html)
        {
            var data = await StudentCardPrintService.LoadClassStudentsAsync(year, grade, classNo);
            if (data.Count == 0) return null;
            return new HtmlExportService()
                .ExportClassCardsToHtml(year, grade, classNo, data);
        }
        return null;
    }

    #endregion

    #region 학생정보 명렬 (Excel/PDF/HTML/CSV)

    private static async Task<string?> ExportClassInfoAsync(
        ExportFormat format, int year, int grade, int classNo)
    {
        if (format == ExportFormat.Pdf)
        {
            return await new StudentCardPrintService()
                .GenerateClassInfoPdfFromDbAsync(year, grade, classNo);
        }

        var data = await StudentCardPrintService.LoadClassStudentsAsync(year, grade, classNo);
        if (data.Count == 0) return null;

        return format switch
        {
            ExportFormat.Excel => await ExportClassInfoToExcelAsync(year, grade, classNo, data),
            ExportFormat.Html  => new HtmlExportService().ExportClassInfoToHtml(year, grade, classNo, data),
            ExportFormat.Csv   => new CsvExportService().ExportClassInfoToCsv(year, grade, classNo, data),
            _ => null
        };
    }

    /// <summary>학생정보 명렬을 CSV 문자열로 빌드 (클립보드 복사용).</summary>
    public async Task<string?> BuildClassInfoCsvAsync(int year, int grade, int classNo)
    {
        var data = await StudentCardPrintService.LoadClassStudentsAsync(year, grade, classNo);
        if (data.Count == 0) return null;
        return new CsvExportService().BuildClassInfoCsv(grade, classNo, data);
    }

    private static async Task<string?> ExportClassInfoToExcelAsync(
        int year, int grade, int classNo, List<StudentCardViewModel> students)
    {
        var rows = students.Select(vm => new InfoExportDto
        {
            학년 = vm.Enrollment?.Grade ?? grade,
            반 = vm.Enrollment?.Class ?? classNo,
            번호 = vm.Enrollment?.Number ?? 0,
            이름 = vm.Student?.Name ?? string.Empty,
            성별 = vm.Student?.Sex ?? string.Empty,
            생년월일 = vm.Student?.BirthDate?.ToString("yyyy-MM-dd") ?? string.Empty,
            연락처 = vm.Student?.Phone ?? string.Empty,
            이메일 = vm.Student?.Email ?? string.Empty,
            주소 = vm.Student?.Address ?? string.Empty,
            보호자 = vm.Detail?.GetPrimaryGuardianName() ?? string.Empty,
            보호자연락처 = vm.Detail?.GetPrimaryContact() ?? string.Empty,
        }).ToList();

        // 저장 자리는 Helpers.ExportPaths 가 정한다 — 확장자가 폴더를 고른다.
        var fileName = $"학생정보_{grade}학년{classNo}반_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
        var filePath = Helpers.ExportPaths.Resolve(fileName);

        await Task.Run(() => MiniExcelLibs.MiniExcel.SaveAs(filePath, rows));
        return filePath;
    }

    private record InfoExportDto
    {
        public int 학년 { get; init; }
        public int 반 { get; init; }
        public int 번호 { get; init; }
        public string 이름 { get; init; } = string.Empty;
        public string 성별 { get; init; } = string.Empty;
        public string 생년월일 { get; init; } = string.Empty;
        public string 연락처 { get; init; } = string.Empty;
        public string 이메일 { get; init; } = string.Empty;
        public string 주소 { get; init; } = string.Empty;
        public string 보호자 { get; init; } = string.Empty;
        public string 보호자연락처 { get; init; } = string.Empty;
    }

    #endregion

    #region 누가기록

    private async Task<string?> ExportClassLogsAsync(
        ExportFormat format, int year, int grade, int classNo)
    {
        var data = await LoadClassLogsAsync(year, grade, classNo);
        if (data.Count == 0) return null;

        return format switch
        {
            ExportFormat.Excel => new StudentLogExportService()
                .ExportClassLogsToExcel(year, grade, classNo, data),
            ExportFormat.Pdf => new StudentLogPrintService()
                .GenerateClassLogPdf(year, grade, classNo, data),
            ExportFormat.Html => new HtmlExportService()
                .ExportClassLogsToHtml(year, grade, classNo, data),
            ExportFormat.Csv => new CsvExportService()
                .ExportClassLogsToCsv(year, grade, classNo, data),
            _ => null
        };
    }

    /// <summary>누가기록을 CSV 문자열로 빌드 (클립보드 복사용).</summary>
    public async Task<string?> BuildClassLogsCsvAsync(int year, int grade, int classNo)
    {
        var data = await LoadClassLogsAsync(year, grade, classNo);
        if (data.Count == 0) return null;
        return new CsvExportService().BuildClassLogsCsv(data);
    }

    /// <summary>학생부 특기사항을 CSV 문자열로 빌드 (클립보드 복사용).</summary>
    public async Task<string?> BuildClassSpecsCsvAsync(int year, int grade, int classNo)
    {
        var data = await LoadClassSpecsAsync(year, grade, classNo);
        if (data.Count == 0) return null;
        return new CsvExportService().BuildClassSpecsCsv(grade, classNo, data);
    }

    private static async Task<List<(StudentCardViewModel Student, List<StudentLogViewModel> Logs)>>
        LoadClassLogsAsync(int year, int grade, int classNo)
    {
        string schoolCode = Settings.SchoolCode.Value;
        var result = new List<(StudentCardViewModel, List<StudentLogViewModel>)>();

        using var enrollmentService = new EnrollmentService();
        var enrollments = await enrollmentService.GetClassRosterAsync(schoolCode, year, grade, classNo);

        using var logService = new StudentLogService();

        // 학년도 전체를 쿼리 한 번으로 가져온다(semester=0 = 전체).
        //   예전에는 1학기·2학기를 따로 불러 합쳤는데, 그러면 학기가 0 으로 저장된 기록
        //   (학년 단위·방학 중 기록)이 어느 쪽에도 안 걸려 내보내기에서 조용히 빠졌다.
        //   화면(누가기록 페이지)도 학년도 전체를 보여주므로 기준을 맞춘다. 쿼리도 2회 → 1회.
        var studentIds = enrollments.Select(e => e.StudentID).ToList();
        var logsByStudent = await logService.GetStudentLogsBatchAsync(studentIds, year, semester: 0);

        foreach (var enrollment in enrollments.OrderBy(e => e.Number))
        {
            logsByStudent.TryGetValue(enrollment.StudentID, out var studentLogs);
            var logs = (studentLogs ?? Enumerable.Empty<StudentLog>())
                       .OrderByDescending(l => l.Date)
                       .ToList();

            if (logs.Count == 0) continue;

            var logVms = logs.Select(l => new StudentLogViewModel(l)).ToList();
            var studentVm = new StudentCardViewModel();
            studentVm.LoadFromEnrollment(enrollment);

            result.Add((studentVm, logVms));
        }

        return result;
    }

    #endregion

    #region 학생부 특기사항

    private async Task<string?> ExportClassSpecsAsync(
        ExportFormat format, int year, int grade, int classNo)
    {
        var data = await LoadClassSpecsAsync(year, grade, classNo);
        if (data.Count == 0) return null;

        return format switch
        {
            ExportFormat.Excel => new StudentSpecExportService()
                .ExportClassSpecsToExcel(year, grade, classNo, data),
            ExportFormat.Pdf => new StudentSpecPrintService()
                .GenerateClassSpecPdf(year, grade, classNo, data),
            ExportFormat.Html => new HtmlExportService()
                .ExportClassSpecsToHtml(year, grade, classNo, data),
            ExportFormat.Csv => new CsvExportService()
                .ExportClassSpecsToCsv(year, grade, classNo, data),
            _ => null
        };
    }

    private static async Task<List<(int Number, string Name, List<StudentSpecial> Specs)>>
        LoadClassSpecsAsync(int year, int grade, int classNo)
    {
        string schoolCode = Settings.SchoolCode.Value;
        var result = new List<(int, string, List<StudentSpecial>)>();

        using var enrollmentService = new EnrollmentService();
        var enrollments = await enrollmentService.GetClassRosterAsync(schoolCode, year, grade, classNo);

        using var specService = new StudentSpecialService();

        // N+1 해소: 학급 전체 학생의 학생부 기록을 단일 쿼리로 일괄 조회
        var studentIds = enrollments.Select(e => e.StudentID).ToList();
        var specMap = await specService.GetByStudentIdsAsync(studentIds, year);

        foreach (var enrollment in enrollments.OrderBy(e => e.Number))
        {
            if (!specMap.TryGetValue(enrollment.StudentID, out var specs) || specs.Count == 0)
                continue;

            result.Add((enrollment.Number, enrollment.Name, specs));
        }

        return result;
    }

    #endregion
}

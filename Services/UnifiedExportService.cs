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
        Seats,        // 좌석배정 (PDF/HTML 전용)
        StudentCard   // 학생카드 (PDF/HTML 전용)
    }

    public enum ExportFormat
    {
        Excel,
        Pdf,
        Html
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
            default:
                return null;
        }
    }

    #region 좌석배정 (PDF/HTML 전용)

    private static async Task<string?> ExportClassSeatsAsync(
        ExportFormat format, int year, int grade, int classNo)
    {
        var service = new SeatsPrintService();
        return format switch
        {
            ExportFormat.Pdf  => await service.GenerateSeatsPdfFromDbAsync(year, grade, classNo),
            ExportFormat.Html => await service.GenerateSeatsHtmlFromDbAsync(year, grade, classNo),
            _ => null
        };
    }

    #endregion

    #region 학생카드 (PDF/HTML 전용)

    private static async Task<string?> ExportClassCardsAsync(
        ExportFormat format, int year, int grade, int classNo)
    {
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
            _ => null
        };
    }

    private static async Task<List<(StudentCardViewModel Student, List<StudentLogViewModel> Logs)>>
        LoadClassLogsAsync(int year, int grade, int classNo)
    {
        string schoolCode = Settings.SchoolCode.Value;
        var result = new List<(StudentCardViewModel, List<StudentLogViewModel>)>();

        using var enrollmentService = new EnrollmentService();
        var enrollments = await enrollmentService.GetClassRosterAsync(schoolCode, year, grade, classNo);

        using var logService = new StudentLogService();
        foreach (var enrollment in enrollments.OrderBy(e => e.Number))
        {
            var logs1 = await logService.GetStudentLogsAsync(enrollment.StudentID, year, 1);
            var logs2 = await logService.GetStudentLogsAsync(enrollment.StudentID, year, 2);
            var logs = logs1.Concat(logs2)
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
        foreach (var enrollment in enrollments.OrderBy(e => e.Number))
        {
            var specs = await specService.GetByStudentAsync(enrollment.StudentID, year);
            if (specs.Count == 0) continue;

            result.Add((enrollment.Number, enrollment.Name, specs));
        }

        return result;
    }

    #endregion
}

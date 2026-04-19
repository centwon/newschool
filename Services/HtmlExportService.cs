using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using NewSchool.Helpers;
using NewSchool.Models;
using NewSchool.ViewModels;

namespace NewSchool.Services;

/// <summary>
/// HTML 내보내기 서비스 — 누가기록·학생부를 단일 HTML 파일로 저장.
/// 브라우저에서 그대로 열람 가능, Ctrl+P로 PDF 저장 가능.
/// </summary>
public class HtmlExportService
{
    private static string GetOutputDir()
    {
        var dir = Path.Combine(Settings.UserDataPath, "Exports");
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        return dir;
    }

    #region 공통 스타일

    /// <summary>공통 HTML 헤더(제목 + 스타일)</summary>
    private static string BuildHtmlHeader(string title, bool landscape = false)
    {
        var pageSize = landscape ? "A4 landscape" : "A4";
        return $@"<!DOCTYPE html>
<html lang=""ko"">
<head>
<meta charset=""UTF-8"">
<title>{WebUtility.HtmlEncode(title)}</title>
<style>
  body {{
    font-family: 'Malgun Gothic', 'Noto Sans KR', sans-serif;
    margin: 0;
    padding: 24px;
    color: #222;
    line-height: 1.5;
  }}
  h1 {{ font-size: 18pt; margin: 0 0 6px 0; color: #1a3d7a; }}
  h2 {{ font-size: 14pt; margin: 24px 0 8px 0; color: #1a3d7a; border-bottom: 2px solid #1a3d7a; padding-bottom: 4px; }}
  .meta {{ color: #666; font-size: 10pt; margin-bottom: 16px; }}
  table {{ width: 100%; border-collapse: collapse; font-size: 10pt; margin-top: 6px; }}
  th, td {{ border: 1px solid #888; padding: 5px 6px; text-align: left; vertical-align: top; }}
  th {{ background: #e8eef7; font-weight: 600; text-align: center; }}
  td.center {{ text-align: center; }}
  td.num {{ text-align: right; }}
  .badge {{ display: inline-block; padding: 1px 6px; border-radius: 3px; background: #1a3d7a; color: #fff; font-size: 9pt; }}
  .over {{ color: #c0392b; font-weight: 600; }}
  .footer {{ margin-top: 24px; font-size: 9pt; color: #888; text-align: right; }}
  @media print {{
    @page {{ size: {pageSize}; margin: 15mm; }}
    body {{ padding: 0; }}
    h2 {{ page-break-after: avoid; }}
    tr {{ page-break-inside: avoid; }}
  }}
</style>
</head>
<body>
";
    }

    private static string BuildHtmlFooter()
    {
        return $@"
<div class=""footer"">출력일시: {DateTime.Now:yyyy년 M월 d일 HH:mm}</div>
</body>
</html>";
    }

    private static string E(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);

    #endregion

    #region 누가기록

    /// <summary>개인 누가기록 HTML 문자열 생성</summary>
    public string BuildStudentLogsHtml(
        StudentCardViewModel studentVm,
        List<StudentLogViewModel> logs)
    {
        var year = studentVm.Enrollment?.Year ?? Settings.WorkYear.Value;
        var grade = studentVm.Enrollment?.Grade ?? 0;
        var classNo = studentVm.Enrollment?.Class ?? 0;
        var number = studentVm.Enrollment?.Number ?? 0;
        var name = studentVm.Name ?? string.Empty;

        var sb = new StringBuilder(BuildHtmlHeader($"누가기록 - {name}"));
        sb.AppendLine($"<h1>{year}학년도 누가 기록</h1>");
        sb.AppendLine($"<div class=\"meta\">{grade}학년 {classNo}반 {number}번 · {E(name)} · 총 {logs.Count}건</div>");

        if (logs.Count == 0)
        {
            sb.AppendLine("<p>기록이 없습니다.</p>");
        }
        else
        {
            var grouped = logs.GroupBy(l => l.Category).OrderBy(g => g.Key);
            foreach (var group in grouped)
            {
                sb.AppendLine($"<h2>{E(group.Key.ToString())} <span class=\"badge\">{group.Count()}건</span></h2>");
                AppendLogTable(sb, group.ToList());
            }
        }

        sb.Append(BuildHtmlFooter());
        return sb.ToString();
    }

    /// <summary>개인 누가기록 HTML 파일 저장</summary>
    public string ExportStudentLogsToHtml(
        StudentCardViewModel studentVm,
        List<StudentLogViewModel> logs)
    {
        var grade = studentVm.Enrollment?.Grade ?? 0;
        var classNo = studentVm.Enrollment?.Class ?? 0;
        var number = studentVm.Enrollment?.Number ?? 0;
        var name = studentVm.Name ?? string.Empty;

        var fileName = $"누가기록_{grade}학년{classNo}반_{number}번_{name}_{DateTime.Now:yyyyMMdd_HHmmss}.html";
        var filePath = Path.Combine(GetOutputDir(), fileName);

        var html = BuildStudentLogsHtml(studentVm, logs);
        File.WriteAllText(filePath, html, Encoding.UTF8);
        return filePath;
    }

    /// <summary>학급 전체 누가기록 HTML 문자열 생성</summary>
    public string BuildClassLogsHtml(
        int year, int grade, int classNo,
        List<(StudentCardViewModel Student, List<StudentLogViewModel> Logs)> studentLogs)
    {
        var sb = new StringBuilder(BuildHtmlHeader($"누가기록 - {grade}학년 {classNo}반", landscape: true));
        sb.AppendLine($"<h1>{year}학년도 누가 기록</h1>");
        sb.AppendLine($"<div class=\"meta\">{grade}학년 {classNo}반 · 총 {studentLogs.Count}명</div>");

        sb.AppendLine("<table>");
        sb.AppendLine("<thead><tr>");
        sb.AppendLine("<th style=\"width:40px\">번호</th>");
        sb.AppendLine("<th style=\"width:70px\">이름</th>");
        sb.AppendLine("<th style=\"width:80px\">날짜</th>");
        sb.AppendLine("<th style=\"width:70px\">영역</th>");
        sb.AppendLine("<th style=\"width:70px\">과목</th>");
        sb.AppendLine("<th style=\"width:100px\">활동명</th>");
        sb.AppendLine("<th>기록/내용</th>");
        sb.AppendLine("<th>학생부초안</th>");
        sb.AppendLine("</tr></thead><tbody>");

        foreach (var (studentVm, logs) in studentLogs)
        {
            if (logs.Count == 0) continue;

            var num = studentVm.Enrollment?.Number ?? 0;
            var sName = studentVm.Name ?? string.Empty;
            bool first = true;

            foreach (var logVm in logs)
            {
                var model = logVm.StudentLog;
                sb.Append("<tr>");
                if (first)
                {
                    sb.Append($"<td class=\"center\" rowspan=\"{logs.Count}\">{num}</td>");
                    sb.Append($"<td class=\"center\" rowspan=\"{logs.Count}\">{E(sName)}</td>");
                    first = false;
                }
                sb.Append($"<td class=\"center\">{logVm.Date:yyyy-MM-dd}</td>");
                sb.Append($"<td class=\"center\">{E(logVm.Category.ToString())}</td>");
                sb.Append($"<td class=\"center\">{E(logVm.SubjectName)}</td>");
                sb.Append($"<td>{E(logVm.ActivityName)}</td>");
                var content = !string.IsNullOrWhiteSpace(logVm.Description)
                    ? logVm.Description
                    : (logVm.Log ?? string.Empty);
                sb.Append($"<td>{E(content)}</td>");
                sb.Append($"<td>{E(model.HasStructuredData() ? model.DraftSummary : string.Empty)}</td>");
                sb.AppendLine("</tr>");
            }
        }
        sb.AppendLine("</tbody></table>");

        sb.Append(BuildHtmlFooter());
        return sb.ToString();
    }

    /// <summary>학급 전체 누가기록 HTML 파일 저장</summary>
    public string ExportClassLogsToHtml(
        int year, int grade, int classNo,
        List<(StudentCardViewModel Student, List<StudentLogViewModel> Logs)> studentLogs)
    {
        var fileName = $"누가기록_{grade}학년{classNo}반_일괄_{DateTime.Now:yyyyMMdd_HHmmss}.html";
        var filePath = Path.Combine(GetOutputDir(), fileName);
        var html = BuildClassLogsHtml(year, grade, classNo, studentLogs);
        File.WriteAllText(filePath, html, Encoding.UTF8);
        return filePath;
    }

    /// <summary>누가기록 표(개인용)</summary>
    private static void AppendLogTable(StringBuilder sb, List<StudentLogViewModel> logs)
    {
        sb.AppendLine("<table><thead><tr>");
        sb.AppendLine("<th style=\"width:80px\">날짜</th>");
        sb.AppendLine("<th style=\"width:70px\">과목/분야</th>");
        sb.AppendLine("<th style=\"width:120px\">활동명</th>");
        sb.AppendLine("<th>내용</th>");
        sb.AppendLine("<th>학생부초안</th>");
        sb.AppendLine("</tr></thead><tbody>");

        foreach (var logVm in logs)
        {
            var model = logVm.StudentLog;
            var content = !string.IsNullOrWhiteSpace(logVm.Description)
                ? logVm.Description
                : (logVm.Log ?? string.Empty);
            sb.Append("<tr>");
            sb.Append($"<td class=\"center\">{logVm.Date:yyyy-MM-dd}</td>");
            sb.Append($"<td class=\"center\">{E(logVm.SubjectName)}</td>");
            sb.Append($"<td>{E(logVm.ActivityName)}</td>");
            sb.Append($"<td>{E(content)}</td>");
            sb.Append($"<td>{E(model.HasStructuredData() ? model.DraftSummary : string.Empty)}</td>");
            sb.AppendLine("</tr>");
        }
        sb.AppendLine("</tbody></table>");
    }

    #endregion

    #region 학생부 특기사항

    /// <summary>개인 학생부 HTML 문자열 생성</summary>
    public string BuildStudentSpecHtml(
        int year, int grade, int classNo, int number, string name,
        List<StudentSpecial> specs)
    {
        var sb = new StringBuilder(BuildHtmlHeader($"학생부 - {name}"));
        sb.AppendLine($"<h1>{year}학년도 학생부 특기사항</h1>");
        sb.AppendLine($"<div class=\"meta\">{grade}학년 {classNo}반 {number}번 · {E(name)}</div>");

        AppendSpecTable(sb, specs);

        sb.Append(BuildHtmlFooter());
        return sb.ToString();
    }

    /// <summary>개인 학생부 HTML 파일 저장</summary>
    public string ExportStudentSpecToHtml(
        int year, int grade, int classNo, int number, string name,
        List<StudentSpecial> specs)
    {
        var fileName = $"학생부_{grade}학년{classNo}반_{number}번_{name}_{DateTime.Now:yyyyMMdd_HHmmss}.html";
        var filePath = Path.Combine(GetOutputDir(), fileName);
        var html = BuildStudentSpecHtml(year, grade, classNo, number, name, specs);
        File.WriteAllText(filePath, html, Encoding.UTF8);
        return filePath;
    }

    /// <summary>학급 전체 학생부 HTML 문자열 생성</summary>
    public string BuildClassSpecsHtml(
        int year, int grade, int classNo,
        List<(int Number, string Name, List<StudentSpecial> Specs)> studentSpecs)
    {
        var sb = new StringBuilder(BuildHtmlHeader($"학생부 - {grade}학년 {classNo}반", landscape: true));
        sb.AppendLine($"<h1>{year}학년도 학생부 특기사항</h1>");
        sb.AppendLine($"<div class=\"meta\">{grade}학년 {classNo}반 · 총 {studentSpecs.Count}명</div>");

        sb.AppendLine("<table>");
        sb.AppendLine("<thead><tr>");
        sb.AppendLine("<th style=\"width:40px\">번호</th>");
        sb.AppendLine("<th style=\"width:70px\">이름</th>");
        sb.AppendLine("<th style=\"width:90px\">영역</th>");
        sb.AppendLine("<th style=\"width:80px\">과목</th>");
        sb.AppendLine("<th>특기사항</th>");
        sb.AppendLine("<th style=\"width:70px\">바이트</th>");
        sb.AppendLine("</tr></thead><tbody>");

        foreach (var (number, name, specs) in studentSpecs)
        {
            if (specs.Count == 0) continue;
            bool first = true;

            foreach (var spec in specs)
            {
                var byteCount = NeisHelper.CountByte(spec.Content ?? string.Empty);
                var maxBytes = NeisHelper.GetMaxBytes(spec.Type);
                var over = byteCount > maxBytes;

                sb.Append("<tr>");
                if (first)
                {
                    sb.Append($"<td class=\"center\" rowspan=\"{specs.Count}\">{number}</td>");
                    sb.Append($"<td class=\"center\" rowspan=\"{specs.Count}\">{E(name)}</td>");
                    first = false;
                }
                sb.Append($"<td class=\"center\">{E(spec.Type)}</td>");
                sb.Append($"<td class=\"center\">{E(spec.SubjectName)}</td>");
                sb.Append($"<td>{E(spec.Content)}</td>");
                sb.Append($"<td class=\"center{(over ? " over" : string.Empty)}\">{byteCount}/{maxBytes}</td>");
                sb.AppendLine("</tr>");
            }
        }
        sb.AppendLine("</tbody></table>");

        sb.Append(BuildHtmlFooter());
        return sb.ToString();
    }

    /// <summary>학급 전체 학생부 HTML 파일 저장</summary>
    public string ExportClassSpecsToHtml(
        int year, int grade, int classNo,
        List<(int Number, string Name, List<StudentSpecial> Specs)> studentSpecs)
    {
        var fileName = $"학생부_{grade}학년{classNo}반_일괄_{DateTime.Now:yyyyMMdd_HHmmss}.html";
        var filePath = Path.Combine(GetOutputDir(), fileName);
        var html = BuildClassSpecsHtml(year, grade, classNo, studentSpecs);
        File.WriteAllText(filePath, html, Encoding.UTF8);
        return filePath;
    }

    private static void AppendSpecTable(StringBuilder sb, List<StudentSpecial> specs)
    {
        sb.AppendLine("<table><thead><tr>");
        sb.AppendLine("<th style=\"width:100px\">영역</th>");
        sb.AppendLine("<th style=\"width:90px\">과목</th>");
        sb.AppendLine("<th>특기사항</th>");
        sb.AppendLine("<th style=\"width:80px\">바이트</th>");
        sb.AppendLine("</tr></thead><tbody>");

        foreach (var spec in specs)
        {
            var byteCount = NeisHelper.CountByte(spec.Content ?? string.Empty);
            var maxBytes = NeisHelper.GetMaxBytes(spec.Type);
            var over = byteCount > maxBytes;

            sb.Append("<tr>");
            sb.Append($"<td class=\"center\">{E(spec.Type)}</td>");
            sb.Append($"<td class=\"center\">{E(spec.SubjectName)}</td>");
            sb.Append($"<td>{E(spec.Content)}</td>");
            sb.Append($"<td class=\"center{(over ? " over" : string.Empty)}\">{byteCount}/{maxBytes}</td>");
            sb.AppendLine("</tr>");
        }
        sb.AppendLine("</tbody></table>");
    }

    #endregion
}

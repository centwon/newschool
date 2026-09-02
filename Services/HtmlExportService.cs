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
    // 저장 자리를 정하던 GetOutputDir 은 Helpers.ExportPaths 로 올렸다(45차).

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
        sb.AppendLine("<th style=\"width:34px\">중요</th>");
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
                // 동아리활동이면 동아리명 — 화면 목록·인쇄·엑셀과 같은 기준
                sb.Append($"<td class=\"center\">{E(logVm.SubjectOrClubDisplay)}</td>");
                sb.Append($"<td>{E(logVm.ActivityName)}</td>");
                // 한 칸에 담는 내용 규칙은 StudentLog.ContentDigest 한 곳에만 있다.
                // 예전에는 여기서 활동 내용이 있으면 그것만 쓰고 기록 칸을 버렸다 —
                // 열 이름은 "기록/내용" 인데 둘 다 적은 기록은 하나가 사라졌다.
                sb.Append($"<td>{E(logVm.ContentDigest)}</td>");
                sb.Append($"<td>{E(model.HasStructuredData() ? model.DraftSummary : string.Empty)}</td>");
                // 교사가 직접 켠 표시다. 한 글자짜리 열이라 지면 부담이 없는데도
                // 종이·HTML 로 뽑으면 사라지고 있었다.
                sb.Append($"<td class=\"center\">{(logVm.IsImportant ? "★" : string.Empty)}</td>");
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
        var filePath = Helpers.ExportPaths.Resolve(fileName);
        var html = BuildClassLogsHtml(year, grade, classNo, studentLogs);
        File.WriteAllText(filePath, html, Encoding.UTF8);
        return filePath;
    }

    #endregion

    #region 학생부 특기사항

    // 개인 단위 HTML(BuildStudentLogsHtml·BuildStudentSpecHtml 과 그 저장 함수들)은 호출부가
    // 없어 지웠다(39차). HTML 내보내기는 학급 단위 한 벌만 남는다.

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
        sb.AppendLine("<th style=\"width:80px\">과목/분야</th>");
        sb.AppendLine("<th>특기사항</th>");
        sb.AppendLine("<th style=\"width:70px\">바이트</th>");
        sb.AppendLine("</tr></thead><tbody>");

        foreach (var (number, name, specs) in studentSpecs)
        {
            if (specs.Count == 0) continue;
            bool first = true;

            foreach (var spec in specs)
            {
                var byteCount = NeisHelper.CountSpecBytes(spec.Type, spec.Title, spec.Content);
                var maxBytes = Settings.GetSpecMaxBytes(spec.Type, spec.Year);   // 설정 오버라이드 반영(입력 화면과 동일 기준)
                var over = NeisHelper.IsOverLimit(byteCount, maxBytes);

                sb.Append("<tr>");
                if (first)
                {
                    sb.Append($"<td class=\"center\" rowspan=\"{specs.Count}\">{number}</td>");
                    sb.Append($"<td class=\"center\" rowspan=\"{specs.Count}\">{E(name)}</td>");
                    first = false;
                }
                sb.Append($"<td class=\"center\">{E(spec.Type)}</td>");
                // 진로활동은 희망분야, 교과활동은 "과목 (N학기)" — 화면 목록과 같은 기준
                sb.Append($"<td class=\"center\">{E(NeisHelper.BuildSubjectDisplay(spec.Type, spec.SubjectName, spec.Title, spec.Semester))}</td>");
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
        var filePath = Helpers.ExportPaths.Resolve(fileName);
        var html = BuildClassSpecsHtml(year, grade, classNo, studentSpecs);
        File.WriteAllText(filePath, html, Encoding.UTF8);
        return filePath;
    }

    /// <summary>학급 명렬표(학생정보 요약) HTML 문자열 — 번호·이름·성별·생년월일·연락처·주소·보호자 연락처.</summary>
    public string BuildClassInfoHtml(
        int year, int grade, int classNo,
        List<NewSchool.ViewModels.StudentCardViewModel> students)
    {
        var sb = new StringBuilder(BuildHtmlHeader($"학생정보 - {grade}학년 {classNo}반", landscape: true));
        sb.AppendLine($"<h1>{year}학년도 {grade}학년 {classNo}반 학생정보</h1>");
        sb.AppendLine($"<div class=\"meta\">총 {students.Count}명</div>");

        sb.AppendLine("<table>");
        sb.AppendLine("<thead><tr>");
        sb.AppendLine("<th style=\"width:40px\">번호</th>");
        sb.AppendLine("<th style=\"width:70px\">이름</th>");
        sb.AppendLine("<th style=\"width:40px\">성별</th>");
        sb.AppendLine("<th style=\"width:90px\">생년월일</th>");
        sb.AppendLine("<th style=\"width:110px\">연락처</th>");
        sb.AppendLine("<th>주소</th>");
        sb.AppendLine("<th style=\"width:70px\">보호자</th>");
        sb.AppendLine("<th style=\"width:50px\">관계</th>");
        sb.AppendLine("<th style=\"width:110px\">보호자 연락처</th>");
        sb.AppendLine("</tr></thead><tbody>");

        foreach (var vm in students)
        {
            sb.Append("<tr>");
            sb.Append($"<td class=\"center\">{vm.Enrollment?.Number ?? 0}</td>");
            sb.Append($"<td class=\"center\">{E(vm.Student?.Name)}</td>");
            sb.Append($"<td class=\"center\">{E(vm.Student?.Sex)}</td>");
            sb.Append($"<td class=\"center\">{vm.Student?.BirthDate?.ToString("yyyy-MM-dd") ?? ""}</td>");
            sb.Append($"<td>{E(vm.Student?.Phone)}</td>");
            sb.Append($"<td>{E(vm.Student?.Address)}</td>");
            // 이름·관계·연락처를 한 번에 고른다 — 따로 고르면 서로 다른 사람이 한 줄에 실린다.
            var guardian = vm.Detail?.ResolvePrimaryGuardian() ?? (string.Empty, string.Empty, string.Empty);
            sb.Append($"<td class=\"center\">{E(guardian.Name)}</td>");
            sb.Append($"<td class=\"center\">{E(guardian.Relation)}</td>");
            sb.Append($"<td>{E(guardian.Phone)}</td>");
            sb.AppendLine("</tr>");
        }
        sb.AppendLine("</tbody></table>");

        sb.Append(BuildHtmlFooter());
        return sb.ToString();
    }

    /// <summary>학급 명렬표 HTML 파일 저장</summary>
    public string ExportClassInfoToHtml(
        int year, int grade, int classNo,
        List<NewSchool.ViewModels.StudentCardViewModel> students)
    {
        var fileName = $"학생정보_{grade}학년{classNo}반_{DateTime.Now:yyyyMMdd_HHmmss}.html";
        var filePath = Helpers.ExportPaths.Resolve(fileName);
        File.WriteAllText(filePath, BuildClassInfoHtml(year, grade, classNo, students), Encoding.UTF8);
        return filePath;
    }
    #endregion

    #region 학생카드 (학급 일괄)

    /// <summary>학급 전체 학생카드 HTML 문자열 생성 (학생당 1 섹션).</summary>
    public string BuildClassCardsHtml(
        int year, int grade, int classNo,
        List<StudentCardViewModel> students)
    {
        var sb = new StringBuilder(BuildHtmlHeader($"학생카드 - {grade}학년 {classNo}반"));
        sb.AppendLine($"<h1>{year}학년도 학생 카드</h1>");
        sb.AppendLine($"<div class=\"meta\">{grade}학년 {classNo}반 · 총 {students.Count}명</div>");

        foreach (var vm in students)
        {
            AppendStudentCardSection(sb, vm, year, grade, classNo);
        }

        sb.Append(BuildHtmlFooter());
        return sb.ToString();
    }

    /// <summary>학급 전체 학생카드 HTML 파일 저장.</summary>
    public string ExportClassCardsToHtml(
        int year, int grade, int classNo,
        List<StudentCardViewModel> students)
    {
        var fileName = $"학생카드_{grade}학년{classNo}반_일괄_{DateTime.Now:yyyyMMdd_HHmmss}.html";
        var filePath = Helpers.ExportPaths.Resolve(fileName);
        var html = BuildClassCardsHtml(year, grade, classNo, students);
        File.WriteAllText(filePath, html, Encoding.UTF8);
        return filePath;
    }

    private static void AppendStudentCardSection(
        StringBuilder sb, StudentCardViewModel vm, int year, int grade, int classNo)
    {
        var s = vm.Student;
        var d = vm.Detail;
        var e = vm.Enrollment;
        var number = e?.Number ?? 0;
        var name = vm.Name;

        sb.AppendLine($"<h2>{number}번 · {E(name)}</h2>");
        sb.AppendLine("<table>");
        sb.AppendLine("<tbody>");
        Row(sb, "학번", s?.StudentID, "성별", s?.Sex);
        Row(sb, "생년월일", s?.BirthDate?.ToString("yyyy-MM-dd"), "연락처", s?.Phone);
        Row(sb, "주소", s?.Address, "이메일", s?.Email);
        if (d != null)
        {
            Row(sb, "아버지", $"{d.FatherName} {d.FatherPhone}".Trim(), "직업", d.FatherJob);
            Row(sb, "어머니", $"{d.MotherName} {d.MotherPhone}".Trim(), "직업", d.MotherJob);
            if (!string.IsNullOrWhiteSpace(d.GuardianName))
                Row(sb, "보호자", $"{d.GuardianName} {d.GuardianPhone} ({d.GuardianRelation})".Trim(), string.Empty, string.Empty);
            if (!string.IsNullOrWhiteSpace(d.HealthInfo) || !string.IsNullOrWhiteSpace(d.Allergies))
                Row(sb, "건강", d.HealthInfo, "알레르기", d.Allergies);
            if (!string.IsNullOrWhiteSpace(d.CareerGoal) || !string.IsNullOrWhiteSpace(d.Interests))
                Row(sb, "진로", d.CareerGoal, "관심사", d.Interests);
            if (!string.IsNullOrWhiteSpace(d.SpecialNeeds))
                FullRow(sb, "특이사항", d.SpecialNeeds);
        }
        if (!string.IsNullOrWhiteSpace(s?.Memo))
            FullRow(sb, "메모", s.Memo);
        sb.AppendLine("</tbody></table>");
    }

    private static void Row(StringBuilder sb, string l1, string? v1, string l2, string? v2)
    {
        sb.Append("<tr>");
        sb.Append($"<th style=\"width:80px\">{E(l1)}</th><td style=\"width:35%\">{E(v1)}</td>");
        sb.Append($"<th style=\"width:80px\">{E(l2)}</th><td>{E(v2)}</td>");
        sb.AppendLine("</tr>");
    }

    private static void FullRow(StringBuilder sb, string label, string? value)
    {
        sb.Append("<tr>");
        sb.Append($"<th style=\"width:80px\">{E(label)}</th>");
        sb.Append($"<td colspan=\"3\">{E(value)}</td>");
        sb.AppendLine("</tr>");
    }

    #endregion
}

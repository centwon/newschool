using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NewSchool.Models;
using NewSchool.ViewModels;

namespace NewSchool.Services;

/// <summary>
/// CSV 내보내기 서비스 — UTF-8 BOM + 엑셀 호환(쉼표 구분 + RFC 4180 인용).
/// 누가기록/학생부 특기사항 통합 내보내기에서 CSV 형식을 처리한다.
/// </summary>
public class CsvExportService
{
    private static string GetOutputDir()
    {
        var dir = Path.Combine(Settings.UserDataPath, "Exports");
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>학급 전체 누가기록을 CSV 파일로 내보낸다. 경로 반환.</summary>
    public string ExportClassLogsToCsv(
        int year, int grade, int classNo,
        List<(StudentCardViewModel Student, List<StudentLogViewModel> Logs)> studentLogs)
    {
        var fileName = $"누가기록_{grade}학년{classNo}반_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
        var filePath = Path.Combine(GetOutputDir(), fileName);
        File.WriteAllText(filePath, BuildClassLogsCsv(studentLogs), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return filePath;
    }

    /// <summary>누가기록 CSV 문자열 — 클립보드 복사용 공용 빌더.</summary>
    public string BuildClassLogsCsv(
        List<(StudentCardViewModel Student, List<StudentLogViewModel> Logs)> studentLogs)
    {
        var sb = new StringBuilder();
        AppendRow(sb,
            "학년", "반", "번호", "이름",
            "날짜", "학기", "카테고리", "과목",
            "활동명", "기록", "주제", "활동내용",
            "역할", "기른능력", "장점", "성취",
            "태그", "중요");

        foreach (var (studentVm, logs) in studentLogs)
        {
            var sGrade = studentVm.Enrollment?.Grade ?? 0;
            var sClass = studentVm.Enrollment?.Class ?? 0;
            var sNumber = studentVm.Enrollment?.Number ?? 0;
            var sName = studentVm.Name ?? string.Empty;

            foreach (var logVm in logs)
            {
                AppendRow(sb,
                    sGrade.ToString(),
                    sClass.ToString(),
                    sNumber.ToString(),
                    sName,
                    logVm.DateString,
                    logVm.Semester.ToString(),
                    logVm.Category.ToString(),
                    logVm.SubjectName ?? string.Empty,
                    logVm.ActivityName ?? string.Empty,
                    logVm.Log ?? string.Empty,
                    logVm.Topic ?? string.Empty,
                    logVm.Description ?? string.Empty,
                    logVm.Role ?? string.Empty,
                    logVm.SkillDeveloped ?? string.Empty,
                    logVm.StrengthShown ?? string.Empty,
                    logVm.ResultOrOutcome ?? string.Empty,
                    logVm.Tag ?? string.Empty,
                    logVm.IsImportant ? "★" : string.Empty);
            }
        }
        return sb.ToString();
    }

    /// <summary>학급 전체 학생부 특기사항을 CSV 파일로 내보낸다.</summary>
    public string ExportClassSpecsToCsv(
        int year, int grade, int classNo,
        List<(int Number, string Name, List<StudentSpecial> Specs)> classSpecs)
    {
        var fileName = $"학생부특기사항_{grade}학년{classNo}반_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
        var filePath = Path.Combine(GetOutputDir(), fileName);
        File.WriteAllText(filePath, BuildClassSpecsCsv(grade, classNo, classSpecs), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return filePath;
    }

    /// <summary>학생부 특기사항 CSV 문자열 — 클립보드 복사용 공용 빌더.</summary>
    public string BuildClassSpecsCsv(
        int grade, int classNo,
        List<(int Number, string Name, List<StudentSpecial> Specs)> classSpecs)
    {
        var sb = new StringBuilder();
        AppendRow(sb,
            "학년", "반", "번호", "이름",
            "영역", "제목", "내용", "과목",
            "날짜", "태그", "마감");

        foreach (var (number, name, specs) in classSpecs)
        {
            foreach (var spec in specs.OrderByDescending(s => s.Date))
            {
                AppendRow(sb,
                    grade.ToString(),
                    classNo.ToString(),
                    number.ToString(),
                    name,
                    spec.Type ?? string.Empty,
                    spec.Title ?? string.Empty,
                    spec.Content ?? string.Empty,
                    spec.SubjectName ?? string.Empty,
                    spec.Date ?? string.Empty,
                    spec.Tag ?? string.Empty,
                    spec.IsFinalized ? "마감" : "작성중");
            }
        }
        return sb.ToString();
    }

    #region CSV 쓰기 유틸

    private static void AppendRow(StringBuilder sb, params string[] fields)
    {
        for (int i = 0; i < fields.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(Escape(fields[i]));
        }
        sb.Append("\r\n");
    }

    /// <summary>
    /// RFC 4180: 쉼표/줄바꿈/따옴표 포함 시 전체를 따옴표로 감싸고 내부 따옴표는 두 개로.
    /// 선행 = - + @도 엑셀 CSV Injection 방지용으로 인용.
    /// </summary>
    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        bool needsQuote = value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
        char first = value[0];
        bool injection = first == '=' || first == '+' || first == '-' || first == '@';
        if (!needsQuote && !injection) return value;
        var escaped = value.Replace("\"", "\"\"");
        return $"\"{escaped}\"";
    }

    #endregion
}

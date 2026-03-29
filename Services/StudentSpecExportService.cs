using System;
using System.Collections.Generic;
using System.IO;
using MiniExcelLibs;
using NewSchool.Helpers;
using NewSchool.Models;

namespace NewSchool.Services;

/// <summary>
/// 학생부 특기사항 엑셀 내보내기 서비스
/// </summary>
public class StudentSpecExportService
{
    private static string GetOutputDir()
    {
        var dir = Path.Combine(Settings.UserDataPath, "Exports");
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// 학급 전체 학생부를 하나의 엑셀 파일로 내보내기
    /// </summary>
    public string ExportClassSpecsToExcel(
        int year, int grade, int classNo,
        List<(int Number, string Name, List<StudentSpecial> Specs)> studentSpecs)
    {
        var fileName = $"학생부_{grade}학년{classNo}반_일괄_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
        var filePath = Path.Combine(GetOutputDir(), fileName);

        var allDtos = new List<SpecExportDto>();

        foreach (var (number, name, specs) in studentSpecs)
        {
            foreach (var spec in specs)
            {
                var byteCount = NeisHelper.CountByte(spec.Content ?? string.Empty);
                var maxBytes = NeisHelper.GetMaxBytes(spec.Type);

                allDtos.Add(new SpecExportDto
                {
                    번호 = number,
                    이름 = name,
                    영역 = spec.Type,
                    과목 = spec.SubjectName ?? string.Empty,
                    특기사항 = spec.Content ?? string.Empty,
                    바이트 = $"{byteCount}/{maxBytes}",
                    마감 = spec.IsFinalized ? "Y" : string.Empty,
                    태그 = spec.Tag ?? string.Empty
                });
            }
        }

        var sheets = new Dictionary<string, object>
        {
            ["전체"] = allDtos
        };

        MiniExcel.SaveAs(filePath, sheets);
        return filePath;
    }

    #region DTO

    private class SpecExportDto
    {
        public int 번호 { get; set; }
        public string 이름 { get; set; } = string.Empty;
        public string 영역 { get; set; } = string.Empty;
        public string 과목 { get; set; } = string.Empty;
        public string 특기사항 { get; set; } = string.Empty;
        public string 바이트 { get; set; } = string.Empty;
        public string 마감 { get; set; } = string.Empty;
        public string 태그 { get; set; } = string.Empty;
    }

    #endregion
}

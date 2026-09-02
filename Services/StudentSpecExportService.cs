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
    // 저장 자리를 정하던 GetOutputDir 은 Helpers.ExportPaths 로 올렸다(45차).

    /// <summary>
    /// 학급 전체 학생부를 하나의 엑셀 파일로 내보내기
    /// </summary>
    public string ExportClassSpecsToExcel(
        int year, int grade, int classNo,
        List<(int Number, string Name, List<StudentSpecial> Specs)> studentSpecs)
    {
        var fileName = $"학생부_{grade}학년{classNo}반_일괄_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
        var filePath = Helpers.ExportPaths.Resolve(fileName);

        var allDtos = new List<SpecExportDto>();

        foreach (var (number, name, specs) in studentSpecs)
        {
            foreach (var spec in specs)
            {
                var byteCount = NeisHelper.CountSpecBytes(spec.Type, spec.Title, spec.Content);
                var maxBytes = Settings.GetSpecMaxBytes(spec.Type, spec.Year);   // 설정 오버라이드 반영(입력 화면과 동일 기준)

                allDtos.Add(new SpecExportDto
                {
                    번호 = number,
                    이름 = name,
                    영역 = spec.Type,
                    // 진로활동은 희망분야, 교과활동은 "과목 (N학기)" — 화면 목록과 같은 기준
                    과목 = NeisHelper.BuildSubjectDisplay(
                        spec.Type, spec.SubjectName, spec.Title, spec.Semester),
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

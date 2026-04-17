using MiniExcelLibs;
using NewSchool.Models;
using NewSchool.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NewSchool.Services;

/// <summary>
/// 누가기록 엑셀 내보내기 서비스
/// </summary>
public class StudentLogExportService
{
    private static string GetOutputDir()
    {
        var dir = Path.Combine(Settings.UserDataPath, "Exports");
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// 누가기록 엑셀 내보내기
    /// </summary>
    public string ExportStudentLogsToExcel(
        StudentCardViewModel studentVm,
        List<StudentLogViewModel> logs)
    {
        // 리팩토링 후: Enrollment를 통해 접근
        var grade = studentVm.Enrollment?.Grade ?? 0;
        var classNo = studentVm.Enrollment?.Class ?? 0;
        var number = studentVm.Enrollment?.Number ?? 0;

        // 엑셀 파일 경로 생성
        var fileName = $"누가기록_{grade}학년{classNo}반_{number}번_{studentVm.Name}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
        var filePath = Path.Combine(GetOutputDir(), fileName);

        // 카테고리별로 그룹화
        var groupedLogs = logs.GroupBy(l => l.Category)
                              .OrderBy(g => g.Key)
                              .ToList();

        // 시트별 데이터 생성
        var sheets = new Dictionary<string, object>();

        // 1. 요약 시트
        sheets.Add("요약", CreateSummarySheet(studentVm, logs));

        // 2. 전체 시트
        sheets.Add("전체", CreateAllLogsSheet(logs));

        // 3. 카테고리별 시트
        foreach (var group in groupedLogs)
        {
            string sheetName = GetSheetName(group.Key);
            sheets.Add(sheetName, CreateCategorySheet(group.ToList()));
        }

        // 엑셀 저장
        MiniExcel.SaveAs(filePath, sheets);

        return filePath;
    }

    /// <summary>
    /// 요약 시트 생성 (Native AOT 호환)
    /// </summary>
    private List<SummaryExportDto> CreateSummarySheet(StudentCardViewModel studentVm, List<StudentLogViewModel> logs)
    {
        // 리팩토링 후: Enrollment를 통해 접근
        var year = studentVm.Enrollment?.Year ?? Settings.WorkYear.Value;
        var grade = studentVm.Enrollment?.Grade ?? 0;
        var classNo = studentVm.Enrollment?.Class ?? 0;
        var number = studentVm.Enrollment?.Number ?? 0;

        // DTO 리스트를 사용하여 정적 타입으로 변경
        var summary = new List<SummaryExportDto>
{
    // 1. 학생 기본 정보
    new SummaryExportDto { 항목 = "학년도", 내용 = year.ToString() },
    new SummaryExportDto { 항목 = "학년", 내용 = grade.ToString() },
    new SummaryExportDto { 항목 = "반", 내용 = classNo.ToString() },
    new SummaryExportDto { 항목 = "번호", 내용 = number.ToString() },
    new SummaryExportDto { 항목 = "이름", 내용 = studentVm.Name ?? string.Empty },
    
    // 2. 구분선 및 총 기록 수
    new SummaryExportDto { 항목 = string.Empty, 내용 = string.Empty },
    new SummaryExportDto { 항목 = "총 기록 수", 내용 = logs.Count.ToString() }
};

        // 3. 카테고리별 통계
        // Note: Log.Category 속성은 StudentLogViewModel에 직접 없습니다.
        // StudnentLog 속성에 접근해야 합니다.
        var categoryStats = logs.GroupBy(l => l.Category) // StudnentLog 속성 사용
                                 .Select(g => new SummaryExportDto
                                 {
                                     // 카테고리 이름 앞에 공백을 넣어 들여쓰기 효과
                                     항목 = $"  {g.Key}",
                                     내용 = $"{g.Count()}건"
                                 })
                                 .ToList();

        summary.AddRange(categoryStats);

        // 4. 출력 정보
        summary.Add(new SummaryExportDto { 항목 = string.Empty, 내용 = string.Empty });
        summary.Add(new SummaryExportDto { 항목 = "출력일시", 내용 = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") });

        return summary;
    }
    /// <summary>
    /// StudentLogViewModel 리스트를 엑셀 내보내기용 DTO 리스트로 변환합니다.
    /// 개인 출력용 (학생 정보는 ViewModel의 InitializeAsync 호출 전제)
    /// </summary>
    public List<LogExportDto> CreateAllLogsSheet(List<StudentLogViewModel> logs)
    {
        return logs.Select(logVm =>
        {
            var model = logVm.StudentLog;

            return new LogExportDto
            {
                학년 = logVm.Grade.ToString(),
                반 = logVm.Class.ToString(),
                번호 = logVm.Number.ToString(),
                이름 = logVm.Name,
                날짜 = logVm.DateString,
                학기 = logVm.Semester,
                카테고리 = logVm.Category.ToString(),
                과목 = logVm.SubjectName ?? string.Empty,
                활동명 = logVm.ActivityName ?? string.Empty,
                기록 = logVm.Log ?? string.Empty,
                주제 = logVm.Topic ?? string.Empty,
                활동내용 = logVm.Description ?? string.Empty,
                역할 = logVm.Role ?? string.Empty,
                기른능력 = logVm.SkillDeveloped ?? string.Empty,
                장점 = logVm.StrengthShown ?? string.Empty,
                성취 = logVm.ResultOrOutcome ?? string.Empty,
                태그 = logVm.Tag ?? string.Empty,
                중요 = logVm.IsImportant ? "★" : string.Empty,
                학생부초안 = model.HasStructuredData() ? model.DraftSummary : string.Empty
            };
        }).ToList();
    }
    /// <summary>카테고리별 시트 생성 (Native AOT 호환)</summary>
    /// <returns>카테고리별 DTO 리스트 (List<object>로 반환하여 범용성 확보)</returns>
    private List<object> CreateCategorySheet(List<StudentLogViewModel> logs)
    {
        if (logs.Count == 0) return new List<object>();

        // 첫 번째 로그의 카테고리를 기준으로 시트 구성 결정
        var category = logs.First().Category;

        // 카테고리에 따라 다른 컬럼 구성을 가진 DTO를 생성하여 반환
        switch (category)
        {
            case LogCategory.교과활동:
            case LogCategory.개인별세특:
                return logs.Select(logVm =>
                {
                    var model = logVm.StudentLog;
                    return new SubjectActivityExportDto
                    {
                        날짜 = logVm.DateString, // ViewModel Computed Property 사용
                        학기 = model.Semester,
                        과목 = model.SubjectName ?? string.Empty,
                        활동명 = model.ActivityName ?? string.Empty,
                        주제 = model.Topic ?? string.Empty,
                        활동내용 = model.Description ?? string.Empty,
                        역할 = model.Role ?? string.Empty,
                        기른능력 = model.SkillDeveloped ?? string.Empty,
                        장점 = model.StrengthShown ?? string.Empty,
                        성취 = model.ResultOrOutcome ?? string.Empty,
                        학생부초안 = model.DraftSummary ?? string.Empty,
                        중요 = model.IsImportant ? "★" : string.Empty
                    };
                }).Cast<object>().ToList(); // List<SubjectActivityExportDto> -> List<object>

            case LogCategory.동아리활동:
                return logs.Select(logVm =>
                {
                    var model = logVm.StudentLog;
                    return new ClubActivityExportDto
                    {
                        날짜 = logVm.DateString,
                        학기 = model.Semester,
                        동아리 = model.SubjectName ?? string.Empty, // 과목명을 동아리명으로 사용
                        활동명 = model.ActivityName ?? string.Empty,
                        주제 = model.Topic ?? string.Empty,
                        활동내용 = model.Description ?? string.Empty,
                        역할 = model.Role ?? string.Empty,
                        학생부초안 = model.DraftSummary ?? string.Empty,
                        중요 = model.IsImportant ? "★" : string.Empty
                    };
                }).Cast<object>().ToList();

            case LogCategory.자율활동:
            case LogCategory.봉사활동:
            case LogCategory.진로활동:
                return logs.Select(logVm =>
                {
                    var model = logVm.StudentLog;
                    return new OtherActivityExportDto
                    {
                        날짜 = logVm.DateString,
                        학기 = model.Semester,
                        활동명 = model.ActivityName ?? string.Empty,
                        주제 = model.Topic ?? string.Empty,
                        활동내용 = model.Description ?? string.Empty,
                        역할 = model.Role ?? string.Empty,
                        장점 = model.StrengthShown ?? string.Empty,
                        성취 = model.ResultOrOutcome ?? string.Empty,
                        학생부초안 = model.DraftSummary ?? string.Empty,
                        중요 = model.IsImportant ? "★" : string.Empty
                    };
                }).Cast<object>().ToList();

            case LogCategory.상담기록:
                return logs.Select(logVm =>
                {
                    var model = logVm.StudentLog;
                    return new CounselingExportDto
                    {
                        날짜 = logVm.DateString,
                        학기 = model.Semester,
                        주제 = model.Topic ?? string.Empty,
                        // 원본 코드: Description이 없으면 Log 사용
                        상담내용 = model.Description ?? model.Log ?? string.Empty,
                        중요 = model.IsImportant ? "★" : string.Empty
                    };
                }).Cast<object>().ToList();

            case LogCategory.종합의견:
                return logs.Select(logVm =>
                {
                    var model = logVm.StudentLog;
                    return new GeneralExportDto
                    {
                        날짜 = logVm.DateString,
                        학기 = model.Semester,
                        // 원본 코드: Log가 없으면 Description 사용
                        의견 = model.Log ?? model.Description ?? string.Empty,
                        학생부초안 = model.DraftSummary ?? string.Empty,
                        중요 = model.IsImportant ? "★" : string.Empty
                    };
                }).Cast<object>().ToList();

            default: // LogCategory.전체 또는 기타
                return logs.Select(logVm =>
                {
                    var model = logVm.StudentLog;
                    return new GeneralExportDto
                    {
                        날짜 = logVm.DateString,
                        학기 = model.Semester,
                        //주제 = model.Topic ?? string.Empty,
                        // 원본 코드: Log가 없으면 Description 사용
                        의견 = model.Log ?? model.Description ?? string.Empty,
                        중요 = model.IsImportant ? "★" : string.Empty
                    };
                }).Cast<object>().ToList();
        }
    }
    /// <summary>
    /// 학급 전체 누가기록을 하나의 엑셀 파일로 내보내기
    /// </summary>
    public string ExportClassLogsToExcel(
        int year, int grade, int classNo,
        List<(StudentCardViewModel Student, List<StudentLogViewModel> Logs)> studentLogs)
    {
        var fileName = $"누가기록_{grade}학년{classNo}반_일괄_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
        var filePath = Path.Combine(GetOutputDir(), fileName);

        // 학생 정보를 포함한 DTO 리스트 생성
        var allDtos = new List<LogExportDto>();
        foreach (var (studentVm, logs) in studentLogs)
        {
            var sGrade = studentVm.Enrollment?.Grade ?? grade;
            var sClass = studentVm.Enrollment?.Class ?? classNo;
            var sNumber = studentVm.Enrollment?.Number ?? 0;
            var sName = studentVm.Name ?? string.Empty;

            foreach (var logVm in logs)
            {
                var model = logVm.StudentLog;
                allDtos.Add(new LogExportDto
                {
                    학년 = sGrade.ToString(),
                    반 = sClass.ToString(),
                    번호 = sNumber.ToString(),
                    이름 = sName,
                    날짜 = logVm.DateString,
                    학기 = logVm.Semester,
                    카테고리 = logVm.Category.ToString(),
                    과목 = logVm.SubjectName ?? string.Empty,
                    활동명 = logVm.ActivityName ?? string.Empty,
                    기록 = logVm.Log ?? string.Empty,
                    주제 = logVm.Topic ?? string.Empty,
                    활동내용 = logVm.Description ?? string.Empty,
                    역할 = logVm.Role ?? string.Empty,
                    기른능력 = logVm.SkillDeveloped ?? string.Empty,
                    장점 = logVm.StrengthShown ?? string.Empty,
                    성취 = logVm.ResultOrOutcome ?? string.Empty,
                    태그 = logVm.Tag ?? string.Empty,
                    중요 = logVm.IsImportant ? "★" : string.Empty,
                    학생부초안 = model.HasStructuredData() ? model.DraftSummary : string.Empty
                });
            }
        }

        var sheets = new Dictionary<string, object>();
        sheets.Add("전체", allDtos);

        MiniExcel.SaveAs(filePath, sheets);
        return filePath;
    }

    /// <summary>카테고리별 시트명</summary>
    private string GetSheetName(LogCategory category)
    {
        return category switch
        {
            LogCategory.교과활동 => "01_교과활동",
            LogCategory.개인별세특 => "02_개인별세특",
            LogCategory.자율활동 => "03_자율활동",
            LogCategory.동아리활동 => "04_동아리",
            LogCategory.봉사활동 => "05_봉사활동",
            LogCategory.진로활동 => "06_진로활동",
            LogCategory.종합의견 => "07_종합의견",
            LogCategory.상담기록 => "08_상담기록",
            _ => "09_기타"
        };
    }
}
public record LogExportDto
{
    // 학생 정보
    public string 학년 { get; init; } = string.Empty;
    public string 반 { get; init; } = string.Empty;
    public string 번호 { get; init; } = string.Empty;
    public string 이름 { get; init; } = string.Empty;

    // 기록 정보
    public string 날짜 { get; init; } = string.Empty;
    public int 학기 { get; init; }
    public string 카테고리 { get; init; } = string.Empty;
    public string 과목 { get; init; } = string.Empty;
    public string 활동명 { get; init; } = string.Empty;
    public string 기록 { get; init; } = string.Empty;
    public string 주제 { get; init; } = string.Empty;
    public string 활동내용 { get; init; } = string.Empty;
    public string 역할 { get; init; } = string.Empty;
    public string 기른능력 { get; init; } = string.Empty;
    public string 장점 { get; init; } = string.Empty;
    public string 성취 { get; init; } = string.Empty;
    public string 태그 { get; init; } = string.Empty;
    public string 중요 { get; init; } = string.Empty;

    // 구조화된 데이터가 있을 때만 생성
    public string 학생부초안 { get; init; } = string.Empty;
}
public record SummaryExportDto
{
    // 요약 시트는 항목(Key)과 내용(Value) 두 컬럼으로 구성됨
    public string 항목 { get; init; } = string.Empty;
    public string 내용 { get; init; } = string.Empty;
}
public record SubjectActivityExportDto
{
    public string 날짜 { get; init; } = string.Empty;
    public int 학기 { get; init; }
    public string 과목 { get; init; } = string.Empty; // 교과/세특 특화
    public string 활동명 { get; init; } = string.Empty;
    public string 주제 { get; init; } = string.Empty;
    public string 활동내용 { get; init; } = string.Empty;
    public string 역할 { get; init; } = string.Empty;
    public string 기른능력 { get; init; } = string.Empty;  // 교과/세특 특화
    public string 장점 { get; init; } = string.Empty;
    public string 성취 { get; init; } = string.Empty;
    public string 학생부초안 { get; init; } = string.Empty;
    public string 중요 { get; init; } = string.Empty;
}
public record ClubActivityExportDto
{
    public string 날짜 { get; init; } = string.Empty;
    public int 학기 { get; init; }
    public string 동아리 { get; init; } = string.Empty;  // 동아리 특화
    public string 활동명 { get; init; } = string.Empty;
    public string 주제 { get; init; } = string.Empty;
    public string 활동내용 { get; init; } = string.Empty;
    public string 역할 { get; init; } = string.Empty;
    // 기른능력, 장점, 성취 필드는 제외됨 (원본 동아리 case에 없음)
    public string 학생부초안 { get; init; } = string.Empty;
    public string 중요 { get; init; } = string.Empty;
}
public record OtherActivityExportDto
{
    public string 날짜 { get; init; } = string.Empty;
    public int 학기 { get; init; }
    public string 활동명 { get; init; } = string.Empty;
    public string 주제 { get; init; } = string.Empty;
    public string 활동내용 { get; init; } = string.Empty;
    public string 역할 { get; init; } = string.Empty;
    public string 장점 { get; init; } = string.Empty;
    public string 성취 { get; init; } = string.Empty;
    public string 학생부초안 { get; init; } = string.Empty;
    public string 중요 { get; init; } = string.Empty;

}
public record CounselingExportDto
{
    public string 날짜 { get; init; } = string.Empty;
    public int 학기 { get; init; }
    public string 주제 { get; init; } = string.Empty;
    public string 상담내용 { get; init; } = string.Empty;
    public string 중요 { get; init; } = string.Empty;
}
public record GeneralExportDto
{
    public string 날짜 { get; init; } = string.Empty;
    public int 학기 { get; init; }
    public string 의견 { get; init; } = string.Empty; // 또는 기록    
    public string 학생부초안 { get; init; } = string.Empty;
    public string 중요 { get; init; } = string.Empty;
}

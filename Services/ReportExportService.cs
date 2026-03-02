using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MiniExcelLibs;
using MiniExcelLibs.OpenXml;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.Services;

namespace NewSchool.Services;

/// <summary>
/// 보고서 내보내기 서비스
/// </summary>
public class ReportExportService
{
    private readonly CourseSectionRepository _sectionRepo;
    private readonly ScheduleRepository _scheduleRepo;
    private readonly ScheduleUnitMapRepository _mapRepo;
    private readonly LessonProgressRepository _progressRepo;

    public ReportExportService(
        CourseSectionRepository sectionRepo,
        ScheduleRepository scheduleRepo,
        ScheduleUnitMapRepository mapRepo,
        LessonProgressRepository progressRepo)
    {
        _sectionRepo = sectionRepo;
        _scheduleRepo = scheduleRepo;
        _mapRepo = mapRepo;
        _progressRepo = progressRepo;
    }

    #region 연간 수업 계획 내보내기

    /// <summary>
    /// 연간 수업 계획 엑셀 내보내기 (NEIS 양식)
    /// </summary>
    public async Task<ExportResult> ExportYearPlanToExcelAsync(
        Course course,
        string filePath,
        int year,
        int semester)
    {
        var result = new ExportResult();

        try
        {
            var sections = await _sectionRepo.GetByCourseAsync(course.No);

            if (sections.Count == 0)
            {
                result.Success = false;
                result.Message = "내보낼 단원이 없습니다.";
                return result;
            }

            // 데이터 준비
            var rows = new List<Dictionary<string, object>>();

            int order = 1;
            foreach (var section in sections.OrderBy(s => s.SortOrder))
            {
                rows.Add(new Dictionary<string, object>
                {
                    ["순번"] = order++,
                    ["단원명"] = section.SectionName,
                    ["유형"] = section.SectionTypeDisplay,
                    ["시수"] = section.EstimatedHours,
                    ["교과서"] = section.PageRangeDisplay,
                    ["학습목표"] = section.LearningObjective ?? "",
                    ["평가유형"] = section.IsEvaluation ? section.SectionTypeDisplay : "",
                    ["비고"] = section.Memo ?? ""
                });
            }

            // 요약 행 추가
            rows.Add(new Dictionary<string, object>
            {
                ["순번"] = "",
                ["단원명"] = "합계",
                ["유형"] = "",
                ["시수"] = sections.Sum(s => s.EstimatedHours),
                ["교과서"] = "",
                ["학습목표"] = "",
                ["평가유형"] = "",
                ["비고"] = ""
            });

            // 엑셀 저장
            await MiniExcel.SaveAsAsync(filePath, rows, overwriteFile: true);

            result.Success = true;
            result.FilePath = filePath;
            result.Message = $"연간 수업 계획 내보내기 완료 ({sections.Count}개 단원)";
            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"내보내기 실패: {ex.Message}";
            return result;
        }
    }

    #endregion

    #region 진도 현황 내보내기

    /// <summary>
    /// 진도 현황 매트릭스 엑셀 내보내기
    /// </summary>
    public async Task<ExportResult> ExportProgressMatrixToExcelAsync(
        Course course,
        List<string> rooms,
        string filePath)
    {
        var result = new ExportResult();

        try
        {
            var sections = await _sectionRepo.GetByCourseAsync(course.No);
            var allProgress = await _progressRepo.GetByCourseAsync(course.No);

            if (sections.Count == 0)
            {
                result.Success = false;
                result.Message = "내보낼 단원이 없습니다.";
                return result;
            }

            // 데이터 준비
            var rows = new List<Dictionary<string, object>>();

            foreach (var section in sections.OrderBy(s => s.SortOrder))
            {
                var row = new Dictionary<string, object>
                {
                    ["번호"] = section.SortOrder,
                    ["단원명"] = section.SectionName,
                    ["시수"] = section.EstimatedHours
                };

                // 학급별 진도 상태
                foreach (var room in rooms)
                {
                    var progress = allProgress.FirstOrDefault(p =>
                        p.CourseSectionId == section.No && p.Room == room);

                    string status = "";
                    if (progress != null)
                    {
                        status = progress.ProgressType switch
                        {
                            ProgressType.Normal when progress.IsCompleted => "✓",
                            ProgressType.Makeup => "보강",
                            ProgressType.Merged => "병합",
                            ProgressType.Skipped => "건너뜀",
                            ProgressType.Cancelled => "결강",
                            _ => ""
                        };
                    }

                    row[room] = status;
                }

                rows.Add(row);
            }

            // 합계 행
            var summaryRow = new Dictionary<string, object>
            {
                ["번호"] = "",
                ["단원명"] = "완료 수",
                ["시수"] = ""
            };

            foreach (var room in rooms)
            {
                int completedCount = allProgress.Count(p =>
                    p.Room == room && p.IsCompleted);
                summaryRow[room] = completedCount;
            }

            rows.Add(summaryRow);

            // 엑셀 저장
            await MiniExcel.SaveAsAsync(filePath, rows, overwriteFile: true);

            result.Success = true;
            result.FilePath = filePath;
            result.Message = $"진도 현황 내보내기 완료 ({sections.Count}개 단원, {rooms.Count}개 학급)";
            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"내보내기 실패: {ex.Message}";
            return result;
        }
    }

    #endregion

    #region 주간 수업 안내문 내보내기

    /// <summary>
    /// 주간 수업 안내문 엑셀 내보내기
    /// </summary>
    public async Task<ExportResult> ExportWeeklyGuideToExcelAsync(
        Course course,
        string room,
        DateTime weekStart,
        string filePath)
    {
        var result = new ExportResult();

        try
        {
            var weekEnd = weekStart.AddDays(6);

            // 해당 주의 일정 조회
            var allSchedules = await _scheduleRepo.GetByCourseAndRoomAsync(course.No, room);
            var schedules = allSchedules
                .Where(s => s.Date >= weekStart && s.Date <= weekEnd)
                .ToList();

            if (schedules.Count == 0)
            {
                result.Success = false;
                result.Message = "해당 주에 예정된 수업이 없습니다.";
                return result;
            }

            // 일정별 단원 정보 로드
            var rows = new List<Dictionary<string, object>>();

            foreach (var schedule in schedules.OrderBy(s => s.Date).ThenBy(s => s.Period))
            {
                var maps = await _mapRepo.GetByScheduleWithSectionAsync(schedule.No);

                string sectionNames = string.Join(", ", maps.Select(m => m.CourseSection?.SectionName ?? ""));
                string pages = string.Join(", ", maps
                    .Where(m => m.CourseSection != null)
                    .Select(m => m.CourseSection!.PageRangeDisplay)
                    .Where(p => !string.IsNullOrEmpty(p)));

                rows.Add(new Dictionary<string, object>
                {
                    ["날짜"] = schedule.Date.ToString("M/d (ddd)"),
                    ["교시"] = $"{schedule.Period}교시",
                    ["단원"] = sectionNames,
                    ["교과서"] = pages,
                    ["학습목표"] = maps.FirstOrDefault()?.CourseSection?.LearningObjective ?? "",
                    ["준비물"] = "",
                    ["비고"] = schedule.IsPinned ? "📌 고정" : ""
                });
            }

            // 엑셀 저장
            await MiniExcel.SaveAsAsync(filePath, rows, overwriteFile: true);

            result.Success = true;
            result.FilePath = filePath;
            result.Message = $"주간 수업 안내문 내보내기 완료 ({weekStart:M/d} ~ {weekEnd:M/d}, {schedules.Count}개 수업)";
            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"내보내기 실패: {ex.Message}";
            return result;
        }
    }

    #endregion

    #region 일정 내보내기

    /// <summary>
    /// 전체 일정 엑셀 내보내기
    /// </summary>
    public async Task<ExportResult> ExportSchedulesToExcelAsync(
        Course course,
        string room,
        DateTime startDate,
        DateTime endDate,
        string filePath)
    {
        var result = new ExportResult();

        try
        {
            var allSchedules = await _scheduleRepo.GetByCourseAndRoomAsync(course.No, room);
            var schedules = allSchedules
                .Where(s => s.Date >= startDate && s.Date <= endDate)
                .ToList();

            if (schedules.Count == 0)
            {
                result.Success = false;
                result.Message = "내보낼 일정이 없습니다.";
                return result;
            }

            var rows = new List<Dictionary<string, object>>();

            foreach (var schedule in schedules.OrderBy(s => s.Date).ThenBy(s => s.Period))
            {
                var maps = await _mapRepo.GetByScheduleWithSectionAsync(schedule.No);

                string sectionNames = string.Join(", ", maps.Select(m => m.CourseSection?.SectionName ?? ""));

                rows.Add(new Dictionary<string, object>
                {
                    ["날짜"] = schedule.Date.ToString("yyyy-MM-dd"),
                    ["요일"] = schedule.Date.ToString("ddd"),
                    ["교시"] = schedule.Period,
                    ["단원"] = sectionNames,
                    ["고정"] = schedule.IsPinned ? "Y" : "",
                    ["완료"] = schedule.IsCompleted ? "Y" : "",
                    ["결강"] = schedule.IsCancelled ? "Y" : ""
                });
            }

            // 엑셀 저장
            await MiniExcel.SaveAsAsync(filePath, rows, overwriteFile: true);

            result.Success = true;
            result.FilePath = filePath;
            result.Message = $"일정 내보내기 완료 ({schedules.Count}개)";
            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"내보내기 실패: {ex.Message}";
            return result;
        }
    }

    #endregion

    #region 격차 분석 보고서

    /// <summary>
    /// 격차 분석 보고서 엑셀 내보내기
    /// </summary>
    public async Task<ExportResult> ExportGapAnalysisToExcelAsync(
        Course course,
        List<string> rooms,
        string filePath)
    {
        var result = new ExportResult();

        try
        {
            var gaps = await _progressRepo.GetProgressGapsAsync(course.No, rooms);

            if (gaps.Count == 0)
            {
                result.Success = false;
                result.Message = "분석할 데이터가 없습니다.";
                return result;
            }

            var rows = gaps.OrderByDescending(g => g.CompletedCount).Select(g => new Dictionary<string, object>
            {
                ["학급"] = g.Room,
                ["완료"] = g.CompletedCount,
                ["전체"] = g.TotalCount,
                ["완료율"] = $"{g.CompletionRate}%",
                ["격차"] = g.GapFromMax,
                ["상태"] = g.StatusDisplay
            }).ToList();

            // 요약
            int maxCompleted = gaps.Max(g => g.CompletedCount);
            int minCompleted = gaps.Min(g => g.CompletedCount);
            double avgCompleted = gaps.Average(g => g.CompletedCount);

            rows.Add(new Dictionary<string, object>
            {
                ["학급"] = "---",
                ["완료"] = "---",
                ["전체"] = "---",
                ["완료율"] = "---",
                ["격차"] = "---",
                ["상태"] = "---"
            });

            rows.Add(new Dictionary<string, object>
            {
                ["학급"] = "최고",
                ["완료"] = maxCompleted,
                ["전체"] = "",
                ["완료율"] = "",
                ["격차"] = "",
                ["상태"] = ""
            });

            rows.Add(new Dictionary<string, object>
            {
                ["학급"] = "최저",
                ["완료"] = minCompleted,
                ["전체"] = "",
                ["완료율"] = "",
                ["격차"] = maxCompleted - minCompleted,
                ["상태"] = ""
            });

            rows.Add(new Dictionary<string, object>
            {
                ["학급"] = "평균",
                ["완료"] = Math.Round(avgCompleted, 1),
                ["전체"] = "",
                ["완료율"] = "",
                ["격차"] = "",
                ["상태"] = ""
            });

            // 엑셀 저장
            await MiniExcel.SaveAsAsync(filePath, rows, overwriteFile: true);

            result.Success = true;
            result.FilePath = filePath;
            result.Message = $"격차 분석 보고서 내보내기 완료 ({rooms.Count}개 학급)";
            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"내보내기 실패: {ex.Message}";
            return result;
        }
    }

    #endregion
}

/// <summary>
/// 내보내기 결과
/// </summary>
public class ExportResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Repositories;

namespace NewSchool.Services;

/// <summary>
/// 진도 동기화 서비스 (보강/병합/건너뛰기/격차 분석)
/// </summary>
public class ProgressSyncService
{
    private readonly LessonProgressRepository _progressRepo;
    private readonly CourseSectionRepository _sectionRepo;
    private readonly ScheduleRepository _scheduleRepo;
    private readonly ScheduleUnitMapRepository _mapRepo;

    public ProgressSyncService(
        LessonProgressRepository progressRepo,
        CourseSectionRepository sectionRepo,
        ScheduleRepository scheduleRepo,
        ScheduleUnitMapRepository mapRepo)
    {
        _progressRepo = progressRepo;
        _sectionRepo = sectionRepo;
        _scheduleRepo = scheduleRepo;
        _mapRepo = mapRepo;
    }

    #region 보강 처리

    /// <summary>
    /// 보강 수업 추가
    /// </summary>
    /// <param name="courseId">과목 번호</param>
    /// <param name="room">학급</param>
    /// <param name="sectionIds">보강할 단원 ID 목록</param>
    /// <param name="makeupDate">보강 날짜</param>
    /// <param name="memo">메모</param>
    public async Task<SyncResult> AddMakeupLessonAsync(
        int courseId,
        string room,
        List<int> sectionIds,
        DateTime makeupDate,
        string? memo = null)
    {
        var result = new SyncResult { ActionType = SyncActionType.Makeup };

        try
        {
            foreach (var sectionId in sectionIds)
            {
                await _progressRepo.MarkAsMakeupAsync(sectionId, room, makeupDate, memo);
                result.AffectedCount++;
            }

            result.Success = true;
            result.Message = $"{result.AffectedCount}개 단원 보강 처리 완료";
            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"보강 처리 실패: {ex.Message}";
            return result;
        }
    }

    #endregion

    #region 병합 처리

    /// <summary>
    /// 단원 병합 (여러 단원을 하나의 수업에서 처리)
    /// </summary>
    /// <param name="courseId">과목 번호</param>
    /// <param name="room">학급</param>
    /// <param name="sectionIds">병합할 단원 ID 목록</param>
    /// <param name="completedDate">완료 날짜</param>
    /// <param name="scheduleId">연결할 일정 ID (선택)</param>
    public async Task<SyncResult> MergeSectionsAsync(
        int courseId,
        string room,
        List<int> sectionIds,
        DateTime completedDate,
        int? scheduleId = null)
    {
        var result = new SyncResult { ActionType = SyncActionType.Merge };

        try
        {
            if (sectionIds.Count < 2)
            {
                result.Success = false;
                result.Message = "병합하려면 2개 이상의 단원이 필요합니다.";
                return result;
            }

            foreach (var sectionId in sectionIds)
            {
                var progress = await _progressRepo.GetOrCreateAsync(sectionId, room);
                progress.IsCompleted = true;
                progress.CompletedDate = completedDate;
                progress.ProgressType = ProgressType.Merged;
                progress.ScheduleId = scheduleId;
                progress.Memo = $"{sectionIds.Count}개 단원 병합";
                progress.UpdatedAt = DateTime.Now;

                await _progressRepo.UpdateAsync(progress);
                result.AffectedCount++;
            }

            result.Success = true;
            result.Message = $"{result.AffectedCount}개 단원 병합 완료";
            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"병합 처리 실패: {ex.Message}";
            return result;
        }
    }

    #endregion

    #region 건너뛰기 처리

    /// <summary>
    /// 단원 건너뛰기
    /// </summary>
    public async Task<SyncResult> SkipSectionAsync(
        int courseId,
        string room,
        int sectionId,
        string? reason = null)
    {
        var result = new SyncResult { ActionType = SyncActionType.Skip };

        try
        {
            await _progressRepo.MarkAsSkippedAsync(sectionId, room, reason);

            result.Success = true;
            result.AffectedCount = 1;
            result.Message = "단원 건너뛰기 완료";
            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"건너뛰기 처리 실패: {ex.Message}";
            return result;
        }
    }

    /// <summary>
    /// 여러 단원 건너뛰기
    /// </summary>
    public async Task<SyncResult> SkipSectionsAsync(
        int courseId,
        string room,
        List<int> sectionIds,
        string? reason = null)
    {
        var result = new SyncResult { ActionType = SyncActionType.Skip };

        try
        {
            foreach (var sectionId in sectionIds)
            {
                await _progressRepo.MarkAsSkippedAsync(sectionId, room, reason);
                result.AffectedCount++;
            }

            result.Success = true;
            result.Message = $"{result.AffectedCount}개 단원 건너뛰기 완료";
            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"건너뛰기 처리 실패: {ex.Message}";
            return result;
        }
    }

    #endregion

    #region 격차 분석

    /// <summary>
    /// 진도 격차 분석
    /// </summary>
    public async Task<GapAnalysisResult> AnalyzeProgressGapAsync(int courseId, List<string> rooms)
    {
        var result = new GapAnalysisResult();

        try
        {
            // 전체 단원 수
            var sections = await _sectionRepo.GetByCourseAsync(courseId);
            result.TotalSections = sections.Count;

            // 학급별 격차 조회
            result.Gaps = await _progressRepo.GetProgressGapsAsync(courseId, rooms);

            // 통계 계산
            if (result.Gaps.Count > 0)
            {
                result.MaxCompleted = result.Gaps.Max(g => g.CompletedCount);
                result.MinCompleted = result.Gaps.Min(g => g.CompletedCount);
                result.AvgCompleted = result.Gaps.Average(g => g.CompletedCount);
                result.MaxGap = result.MaxCompleted - result.MinCompleted;

                // 선두 학급
                result.LeadingRooms = result.Gaps
                    .Where(g => g.CompletedCount == result.MaxCompleted)
                    .Select(g => g.Room)
                    .ToList();

                // 뒤처진 학급
                result.BehindRooms = result.Gaps
                    .Where(g => g.GapFromMax >= 3)
                    .Select(g => g.Room)
                    .ToList();
            }

            result.Success = true;
            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"격차 분석 실패: {ex.Message}";
            return result;
        }
    }

    /// <summary>
    /// 동기화 제안 생성
    /// </summary>
    public async Task<List<SyncSuggestion>> SuggestSyncActionsAsync(int courseId, List<string> rooms)
    {
        var suggestions = new List<SyncSuggestion>();

        try
        {
            var analysis = await AnalyzeProgressGapAsync(courseId, rooms);
            if (!analysis.Success) return suggestions;

            // 뒤처진 학급에 대한 제안
            foreach (var room in analysis.BehindRooms)
            {
                var gap = analysis.Gaps.FirstOrDefault(g => g.Room == room);
                if (gap == null) continue;

                if (gap.GapFromMax >= 5)
                {
                    // 심한 격차 - 병합 제안
                    suggestions.Add(new SyncSuggestion
                    {
                        Room = room,
                        SuggestionType = SuggestionType.Merge,
                        Priority = SuggestionPriority.High,
                        Description = $"{room}: {gap.GapFromMax}단원 뒤처짐 - 단원 병합 권장",
                        GapCount = gap.GapFromMax
                    });
                }
                else if (gap.GapFromMax >= 3)
                {
                    // 중간 격차 - 보강 제안
                    suggestions.Add(new SyncSuggestion
                    {
                        Room = room,
                        SuggestionType = SuggestionType.Makeup,
                        Priority = SuggestionPriority.Medium,
                        Description = $"{room}: {gap.GapFromMax}단원 뒤처짐 - 보강 수업 권장",
                        GapCount = gap.GapFromMax
                    });
                }
            }

            // 선두 학급이 너무 앞서는 경우
            if (analysis.MaxGap >= 5 && analysis.LeadingRooms.Count > 0)
            {
                foreach (var room in analysis.LeadingRooms)
                {
                    suggestions.Add(new SyncSuggestion
                    {
                        Room = room,
                        SuggestionType = SuggestionType.SlowDown,
                        Priority = SuggestionPriority.Low,
                        Description = $"{room}: 다른 학급보다 {analysis.MaxGap}단원 앞서 있음 - 진도 조절 고려",
                        GapCount = analysis.MaxGap
                    });
                }
            }

            return suggestions.OrderByDescending(s => s.Priority).ToList();
        }
        catch
        {
            return suggestions;
        }
    }

    #endregion

    #region 일괄 처리

    /// <summary>
    /// 모든 학급 진도 초기화
    /// </summary>
    public async Task<SyncResult> InitializeAllProgressAsync(int courseId, List<string> rooms)
    {
        var result = new SyncResult { ActionType = SyncActionType.Initialize };

        try
        {
            var sections = await _sectionRepo.GetByCourseAsync(courseId);
            var sectionIds = sections.Select(s => s.No).ToList();

            foreach (var room in rooms)
            {
                await _progressRepo.InitializeProgressForRoomAsync(courseId, room, sectionIds);
                result.AffectedCount += sectionIds.Count;
            }

            result.Success = true;
            result.Message = $"{rooms.Count}개 학급, {sectionIds.Count}개 단원 진도 초기화 완료";
            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"초기화 실패: {ex.Message}";
            return result;
        }
    }

    /// <summary>
    /// 특정 학급 진도 복사
    /// </summary>
    public async Task<SyncResult> CopyProgressFromRoomAsync(
        int courseId,
        string sourceRoom,
        string targetRoom)
    {
        var result = new SyncResult { ActionType = SyncActionType.Copy };

        try
        {
            var sourceProgress = await _progressRepo.GetByRoomAsync(courseId, sourceRoom);

            foreach (var sp in sourceProgress)
            {
                var targetProgress = await _progressRepo.GetOrCreateAsync(sp.CourseSectionId, targetRoom);
                targetProgress.IsCompleted = sp.IsCompleted;
                targetProgress.CompletedDate = sp.CompletedDate;
                targetProgress.ProgressType = sp.ProgressType;
                targetProgress.Memo = $"복사됨 ({sourceRoom})";
                targetProgress.UpdatedAt = DateTime.Now;

                await _progressRepo.UpdateAsync(targetProgress);
                result.AffectedCount++;
            }

            result.Success = true;
            result.Message = $"{sourceRoom} → {targetRoom}: {result.AffectedCount}개 진도 복사 완료";
            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"복사 실패: {ex.Message}";
            return result;
        }
    }

    /// <summary>
    /// 일정과 진도 동기화 (완료된 일정 → 진도 완료)
    /// </summary>
    public async Task<SyncResult> SyncProgressFromSchedulesAsync(int courseId, string room)
    {
        var result = new SyncResult { ActionType = SyncActionType.Sync };

        try
        {
            // 완료된 일정 조회
            var schedules = await _scheduleRepo.GetByCourseAndRoomAsync(courseId, room);
            var completedSchedules = schedules.Where(s => s.IsCompleted).ToList();

            foreach (var schedule in completedSchedules)
            {
                // 일정에 연결된 단원 조회
                var maps = await _mapRepo.GetByScheduleAsync(schedule.No);

                foreach (var map in maps)
                {
                    var progress = await _progressRepo.GetOrCreateAsync(map.CourseSectionId, room);

                    if (!progress.IsCompleted)
                    {
                        progress.IsCompleted = true;
                        progress.CompletedDate = schedule.Date;
                        progress.ScheduleId = schedule.No;
                        progress.UpdatedAt = DateTime.Now;

                        await _progressRepo.UpdateAsync(progress);
                        result.AffectedCount++;
                    }
                }
            }

            result.Success = true;
            result.Message = $"{result.AffectedCount}개 단원 진도 동기화 완료";
            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"동기화 실패: {ex.Message}";
            return result;
        }
    }

    #endregion

    #region 진도 조회

    /// <summary>
    /// 매트릭스 데이터 생성
    /// </summary>
    public async Task<ProgressMatrixData> GetProgressMatrixAsync(int courseId, List<string> rooms)
    {
        var matrixData = new ProgressMatrixData();

        try
        {
            // 단원 목록
            matrixData.Sections = await _sectionRepo.GetByCourseAsync(courseId);
            matrixData.Rooms = rooms;

            // 전체 진도 조회 + 딕셔너리로 인덱싱 (O(1) 조회)
            var allProgress = await _progressRepo.GetByCourseAsync(courseId);
            var progressLookup = allProgress.ToDictionary(
                p => (p.CourseSectionId, p.Room),
                p => p);

            // 매트릭스 셀 생성
            foreach (var section in matrixData.Sections)
            {
                foreach (var room in rooms)
                {
                    progressLookup.TryGetValue((section.No, room), out var progress);

                    matrixData.Cells.Add(new ProgressMatrixCell
                    {
                        CourseSectionId = section.No,
                        Room = room,
                        Progress = progress
                    });
                }
            }

            // 학급별 통계
            foreach (var room in rooms)
            {
                var stats = await _progressRepo.GetStatsAsync(courseId, room);
                matrixData.StatsByRoom[room] = stats;
            }

            matrixData.Success = true;
            return matrixData;
        }
        catch (Exception ex)
        {
            matrixData.Success = false;
            matrixData.Message = ex.Message;
            return matrixData;
        }
    }

    #endregion
}

#region Result Classes

/// <summary>
/// 동기화 결과
/// </summary>
public class SyncResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public SyncActionType ActionType { get; set; }
    public int AffectedCount { get; set; }
}

/// <summary>
/// 동기화 작업 유형
/// </summary>
public enum SyncActionType
{
    Makeup,
    Merge,
    Skip,
    Initialize,
    Copy,
    Sync
}

/// <summary>
/// 격차 분석 결과
/// </summary>
public class GapAnalysisResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int TotalSections { get; set; }
    public List<ProgressGap> Gaps { get; set; } = new();
    public int MaxCompleted { get; set; }
    public int MinCompleted { get; set; }
    public double AvgCompleted { get; set; }
    public int MaxGap { get; set; }
    public List<string> LeadingRooms { get; set; } = new();
    public List<string> BehindRooms { get; set; } = new();
}

/// <summary>
/// 동기화 제안
/// </summary>
public class SyncSuggestion
{
    public string Room { get; set; } = string.Empty;
    public SuggestionType SuggestionType { get; set; }
    public SuggestionPriority Priority { get; set; }
    public string Description { get; set; } = string.Empty;
    public int GapCount { get; set; }

    public string PriorityDisplay => Priority switch
    {
        SuggestionPriority.High => "🔴 높음",
        SuggestionPriority.Medium => "🟡 중간",
        SuggestionPriority.Low => "🟢 낮음",
        _ => "알 수 없음"
    };

    public string TypeIcon => SuggestionType switch
    {
        SuggestionType.Makeup => "➕",
        SuggestionType.Merge => "🔗",
        SuggestionType.Skip => "⏭",
        SuggestionType.SlowDown => "⏸",
        _ => "?"
    };
}

/// <summary>
/// 제안 유형
/// </summary>
public enum SuggestionType
{
    Makeup,
    Merge,
    Skip,
    SlowDown
}

/// <summary>
/// 제안 우선순위
/// </summary>
public enum SuggestionPriority
{
    Low,
    Medium,
    High
}

/// <summary>
/// 진도 매트릭스 데이터
/// </summary>
public class ProgressMatrixData
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<CourseSection> Sections { get; set; } = new();
    public List<string> Rooms { get; set; } = new();
    public List<ProgressMatrixCell> Cells { get; set; } = new();
    public Dictionary<string, ProgressStats> StatsByRoom { get; set; } = new();

    /// <summary>
    /// 특정 셀 가져오기
    /// </summary>
    public ProgressMatrixCell? GetCell(int sectionId, string room)
    {
        return Cells.FirstOrDefault(c => c.CourseSectionId == sectionId && c.Room == room);
    }
}

#endregion

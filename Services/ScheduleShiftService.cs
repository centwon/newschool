using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Repositories;

namespace NewSchool.Services;

/// <summary>
/// 수업 일정 이동 서비스 (Push/Pull + Undo/Redo)
/// </summary>
public class ScheduleShiftService
{
    private readonly ScheduleRepository _scheduleRepo;
    private readonly ScheduleUnitMapRepository _mapRepo;
    private readonly UndoHistoryRepository _undoRepo;
    private readonly LessonRepository _lessonRepo;
    private readonly SchoolScheduleRepository _schoolScheduleRepo;

    public ScheduleShiftService(
        ScheduleRepository scheduleRepo,
        ScheduleUnitMapRepository mapRepo,
        UndoHistoryRepository undoRepo,
        LessonRepository lessonRepo,
        SchoolScheduleRepository schoolScheduleRepo)
    {
        _scheduleRepo = scheduleRepo;
        _mapRepo = mapRepo;
        _undoRepo = undoRepo;
        _lessonRepo = lessonRepo;
        _schoolScheduleRepo = schoolScheduleRepo;
    }

    #region Push (밀기)

    /// <summary>
    /// 수업 밀기 (지정 날짜 이후 모든 수업을 다음 슬롯으로)
    /// </summary>
    /// <param name="courseId">과목 번호</param>
    /// <param name="room">학급</param>
    /// <param name="fromDate">기준 날짜</param>
    /// <param name="fromPeriod">기준 교시 (해당 슬롯 포함)</param>
    /// <param name="semesterEnd">학기 종료일</param>
    public async Task<ShiftResult> PushSchedulesAsync(
        int courseId,
        string room,
        DateTime fromDate,
        int fromPeriod,
        DateTime semesterEnd)
    {
        var result = new ShiftResult();

        try
        {
            // 1. 기준일 이후 모든 비고정 스케줄 조회
            var schedules = await _scheduleRepo.GetUnpinnedSchedulesFromDateAsync(
                courseId, room, fromDate);

            // 기준 교시 이후만 필터링
            schedules = schedules
                .Where(s => s.Date > fromDate || (s.Date.Date == fromDate.Date && s.Period >= fromPeriod))
                .OrderByDescending(s => s.Date)
                .ThenByDescending(s => s.Period)
                .ToList();

            if (schedules.Count == 0)
            {
                result.Success = true;
                result.Message = "이동할 수업이 없습니다.";
                return result;
            }

            // 2. 가용 슬롯 목록 생성
            var availableSlots = await GenerateAvailableSlotsAsync(courseId, room, fromDate, semesterEnd);

            // 3. Undo 데이터 준비
            var shiftData = new ShiftActionData
            {
                Direction = 1, // Push
                FromDate = fromDate,
                FromPeriod = fromPeriod
            };

            // 4. 역순으로 밀기 (맨 뒤부터 밀어야 충돌 방지)
            foreach (var schedule in schedules)
            {
                var currentSlot = (schedule.Date, schedule.Period);
                var nextSlot = FindNextSlot(availableSlots, currentSlot);

                if (nextSlot == null)
                {
                    // 더 이상 밀 수 없음 (학기 종료)
                    result.OverflowCount++;
                    continue;
                }

                // 이동 정보 기록
                shiftData.ShiftedSchedules.Add(new ScheduleShiftInfo
                {
                    ScheduleId = schedule.No,
                    OriginalDate = schedule.Date,
                    OriginalPeriod = schedule.Period,
                    NewDate = nextSlot.Value.Date,
                    NewPeriod = nextSlot.Value.Period
                });

                // 실제 이동
                schedule.Date = nextSlot.Value.Date;
                schedule.Period = nextSlot.Value.Period;
                await _scheduleRepo.UpdateAsync(schedule);

                result.ShiftedCount++;
            }

            // 5. Undo 기록 저장
            if (result.ShiftedCount > 0)
            {
                await SaveUndoActionAsync(courseId, room, UndoActionType.ScheduleShift,
                    $"{fromDate:M/d} {fromPeriod}교시부터 {result.ShiftedCount}개 수업 밀기",
                    shiftData);
            }

            result.Success = true;
            result.Message = $"{result.ShiftedCount}개 수업을 다음 슬롯으로 이동했습니다.";
            if (result.OverflowCount > 0)
            {
                result.Message += $" ({result.OverflowCount}개는 학기 종료로 이동 불가)";
            }

            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"밀기 실패: {ex.Message}";
            return result;
        }
    }

    #endregion

    #region Pull (당기기)

    /// <summary>
    /// 수업 당기기 (지정 날짜 이후 모든 수업을 이전 슬롯으로)
    /// </summary>
    public async Task<ShiftResult> PullSchedulesAsync(
        int courseId,
        string room,
        DateTime fromDate,
        int fromPeriod,
        DateTime semesterStart)
    {
        var result = new ShiftResult();

        try
        {
            // 1. 기준일 이후 모든 비고정 스케줄 조회
            var schedules = await _scheduleRepo.GetUnpinnedSchedulesFromDateAsync(
                courseId, room, fromDate);

            // 기준 교시 이후만 필터링 + 정순 정렬 (앞에서부터 당기기)
            schedules = schedules
                .Where(s => s.Date > fromDate || (s.Date.Date == fromDate.Date && s.Period >= fromPeriod))
                .OrderBy(s => s.Date)
                .ThenBy(s => s.Period)
                .ToList();

            if (schedules.Count == 0)
            {
                result.Success = true;
                result.Message = "이동할 수업이 없습니다.";
                return result;
            }

            // 2. 가용 슬롯 목록 생성
            var availableSlots = await GenerateAvailableSlotsAsync(courseId, room, semesterStart, fromDate.AddMonths(6));

            // 3. Undo 데이터 준비
            var shiftData = new ShiftActionData
            {
                Direction = -1, // Pull
                FromDate = fromDate,
                FromPeriod = fromPeriod
            };

            // 4. 정순으로 당기기 (앞에서부터 당겨야 충돌 방지)
            var occupiedSlots = new HashSet<(DateTime, int)>();
            foreach (var schedule in schedules)
            {
                var currentSlot = (schedule.Date, schedule.Period);
                var prevSlot = FindPreviousSlot(availableSlots, currentSlot, occupiedSlots);

                if (prevSlot == null)
                {
                    // 더 이상 당길 수 없음
                    occupiedSlots.Add(currentSlot);
                    continue;
                }

                // 이동 정보 기록
                shiftData.ShiftedSchedules.Add(new ScheduleShiftInfo
                {
                    ScheduleId = schedule.No,
                    OriginalDate = schedule.Date,
                    OriginalPeriod = schedule.Period,
                    NewDate = prevSlot.Value.Date,
                    NewPeriod = prevSlot.Value.Period
                });

                // 실제 이동
                schedule.Date = prevSlot.Value.Date;
                schedule.Period = prevSlot.Value.Period;
                await _scheduleRepo.UpdateAsync(schedule);

                occupiedSlots.Add(prevSlot.Value);
                result.ShiftedCount++;
            }

            // 5. Undo 기록 저장
            if (result.ShiftedCount > 0)
            {
                await SaveUndoActionAsync(courseId, room, UndoActionType.ScheduleShift,
                    $"{fromDate:M/d} {fromPeriod}교시부터 {result.ShiftedCount}개 수업 당기기",
                    shiftData);
            }

            result.Success = true;
            result.Message = $"{result.ShiftedCount}개 수업을 이전 슬롯으로 이동했습니다.";

            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"당기기 실패: {ex.Message}";
            return result;
        }
    }

    #endregion

    #region Undo/Redo

    /// <summary>
    /// 마지막 작업 취소 (Undo)
    /// </summary>
    public async Task<UndoRedoResult> UndoLastActionAsync(int courseId, string room)
    {
        var result = new UndoRedoResult();

        try
        {
            var action = await _undoRepo.GetLastUndoableActionAsync(courseId, room);

            if (action == null)
            {
                result.Success = false;
                result.Message = "취소할 작업이 없습니다.";
                return result;
            }

            // 작업 유형별 Undo 처리
            switch (action.ActionType)
            {
                case UndoActionType.ScheduleShift:
                    await UndoShiftAsync(action);
                    break;

                case UndoActionType.ScheduleCreate:
                    await UndoCreateAsync(action);
                    break;

                case UndoActionType.ScheduleDelete:
                    await UndoDeleteAsync(action);
                    break;

                case UndoActionType.BulkGenerate:
                    await UndoBulkGenerateAsync(action);
                    break;

                default:
                    result.Success = false;
                    result.Message = $"지원하지 않는 작업 유형: {action.ActionTypeDisplay}";
                    return result;
            }

            // Undo 완료 마킹
            await _undoRepo.MarkAsUndoneAsync(action.No);

            result.Success = true;
            result.Message = $"'{action.Description}' 취소됨";
            result.ActionDescription = action.Description;

            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"취소 실패: {ex.Message}";
            return result;
        }
    }

    /// <summary>
    /// 마지막 취소 작업 다시 실행 (Redo)
    /// </summary>
    public async Task<UndoRedoResult> RedoLastActionAsync(int courseId, string room)
    {
        var result = new UndoRedoResult();

        try
        {
            var action = await _undoRepo.GetLastRedoableActionAsync(courseId, room);

            if (action == null)
            {
                result.Success = false;
                result.Message = "다시 실행할 작업이 없습니다.";
                return result;
            }

            // 작업 유형별 Redo 처리
            switch (action.ActionType)
            {
                case UndoActionType.ScheduleShift:
                    await RedoShiftAsync(action);
                    break;

                case UndoActionType.ScheduleCreate:
                    await RedoCreateAsync(action);
                    break;

                case UndoActionType.ScheduleDelete:
                    await RedoDeleteAsync(action);
                    break;

                case UndoActionType.BulkGenerate:
                    await RedoBulkGenerateAsync(action);
                    break;

                default:
                    result.Success = false;
                    result.Message = $"지원하지 않는 작업 유형: {action.ActionTypeDisplay}";
                    return result;
            }

            // Redo 완료 마킹 (IsUndone = 0)
            await _undoRepo.MarkAsRedoneAsync(action.No);

            result.Success = true;
            result.Message = $"'{action.Description}' 다시 실행됨";
            result.ActionDescription = action.Description;

            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"다시 실행 실패: {ex.Message}";
            return result;
        }
    }

    #endregion

    #region Undo Implementations

    private async Task UndoShiftAsync(UndoAction action)
    {
        var data = action.GetData<ShiftActionData>();
        if (data == null) return;

        // 역방향으로 원래 위치로 복원
        foreach (var shift in data.ShiftedSchedules.OrderBy(s => s.OriginalDate).ThenBy(s => s.OriginalPeriod))
        {
            var schedule = await _scheduleRepo.GetByIdAsync(shift.ScheduleId);
            if (schedule != null)
            {
                schedule.Date = shift.OriginalDate;
                schedule.Period = shift.OriginalPeriod;
                await _scheduleRepo.UpdateAsync(schedule);
            }
        }
    }

    private async Task UndoCreateAsync(UndoAction action)
    {
        var data = action.GetData<ScheduleActionData>();
        if (data == null) return;

        // 생성된 스케줄 삭제
        await _mapRepo.DeleteByScheduleAsync(data.ScheduleId);
        await _scheduleRepo.DeleteAsync(data.ScheduleId);
    }

    private async Task UndoDeleteAsync(UndoAction action)
    {
        var data = action.GetData<ScheduleActionData>();
        if (data == null) return;

        // 삭제된 스케줄 복원
        var schedule = new Schedule
        {
            CourseId = data.CourseId,
            Room = data.Room,
            Date = data.Date,
            Period = data.Period,
            IsPinned = data.IsPinned
        };

        await _scheduleRepo.CreateAsync(schedule);

        // 매핑 복원
        foreach (var sectionId in data.SectionIds)
        {
            await _mapRepo.AddUnitToScheduleAsync(schedule.No, sectionId);
        }
    }

    private async Task UndoBulkGenerateAsync(UndoAction action)
    {
        var data = action.GetData<BulkGenerateActionData>();
        if (data == null) return;

        // 생성된 모든 스케줄 삭제
        foreach (var scheduleId in data.CreatedScheduleIds)
        {
            await _mapRepo.DeleteByScheduleAsync(scheduleId);
            await _scheduleRepo.DeleteAsync(scheduleId);
        }
    }

    #endregion

    #region Redo Implementations

    private async Task RedoShiftAsync(UndoAction action)
    {
        var data = action.GetData<ShiftActionData>();
        if (data == null) return;

        // 다시 이동
        foreach (var shift in data.ShiftedSchedules)
        {
            var schedule = await _scheduleRepo.GetByIdAsync(shift.ScheduleId);
            if (schedule != null)
            {
                schedule.Date = shift.NewDate;
                schedule.Period = shift.NewPeriod;
                await _scheduleRepo.UpdateAsync(schedule);
            }
        }
    }

    private async Task RedoCreateAsync(UndoAction action)
    {
        // Undo가 삭제였으므로 다시 생성
        await UndoDeleteAsync(action);
    }

    private async Task RedoDeleteAsync(UndoAction action)
    {
        // Undo가 복원이었으므로 다시 삭제
        await UndoCreateAsync(action);
    }

    private async Task RedoBulkGenerateAsync(UndoAction action)
    {
        // 일괄 생성은 복잡하므로 현재 미지원
        throw new NotSupportedException("자동 배치의 다시 실행은 지원되지 않습니다. 배치를 다시 수행해주세요.");
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// 가용 슬롯 목록 생성
    /// </summary>
    private async Task<List<(DateTime Date, int Period)>> GenerateAvailableSlotsAsync(
        int courseId,
        string room,
        DateTime startDate,
        DateTime endDate)
    {
        var slots = new List<(DateTime Date, int Period)>();

        // 시간표 조회
        var lessons = await _lessonRepo.GetByCourseAsync(courseId);
        var roomLessons = lessons.Where(l => l.Room == room).ToList();

        if (roomLessons.Count == 0)
            return slots;

        // 휴일 조회
        var holidays = new HashSet<DateTime>();
        try
        {
            var schedules = await _schoolScheduleRepo.GetByDateRangeAsync(
                Settings.SchoolCode.Value, startDate, endDate);
            foreach (var schedule in schedules)
            {
                if (schedule.IsHoliday || schedule.EVENT_NM.Contains("휴업") ||
                    schedule.EVENT_NM.Contains("방학"))
                {
                    holidays.Add(schedule.AA_YMD.Date);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ScheduleShiftService] 휴일 조회 실패: {ex.Message}");
        }

        // 요일별 시간표 그룹화
        var lessonsByDay = roomLessons
            .GroupBy(l => l.DayOfWeek)
            .ToDictionary(g => g.Key, g => g.OrderBy(l => l.Period).ToList());

        // 날짜별 슬롯 생성
        for (var date = startDate.Date; date <= endDate.Date; date = date.AddDays(1))
        {
            if (holidays.Contains(date))
                continue;

            int dayOfWeek = (int)date.DayOfWeek;
            if (!lessonsByDay.ContainsKey(dayOfWeek))
                continue;

            foreach (var lesson in lessonsByDay[dayOfWeek])
            {
                slots.Add((date, lesson.Period));
            }
        }

        return slots.OrderBy(s => s.Date).ThenBy(s => s.Period).ToList();
    }

    /// <summary>
    /// 다음 슬롯 찾기
    /// </summary>
    private (DateTime Date, int Period)? FindNextSlot(
        List<(DateTime Date, int Period)> slots,
        (DateTime Date, int Period) current)
    {
        var idx = slots.FindIndex(s => s.Date.Date == current.Date.Date && s.Period == current.Period);
        if (idx < 0 || idx >= slots.Count - 1)
            return null;

        return slots[idx + 1];
    }

    /// <summary>
    /// 이전 빈 슬롯 찾기
    /// </summary>
    private (DateTime Date, int Period)? FindPreviousSlot(
        List<(DateTime Date, int Period)> slots,
        (DateTime Date, int Period) current,
        HashSet<(DateTime, int)> occupied)
    {
        var idx = slots.FindIndex(s => s.Date.Date == current.Date.Date && s.Period == current.Period);
        if (idx <= 0)
            return null;

        // 이전 슬롯 중 비어있는 것 찾기
        for (int i = idx - 1; i >= 0; i--)
        {
            var slot = slots[i];
            if (!occupied.Contains((slot.Date, slot.Period)))
            {
                return slots[i];
            }
        }

        return null;
    }

    /// <summary>
    /// Undo 기록 저장
    /// </summary>
    private async Task SaveUndoActionAsync<T>(
        int courseId,
        string room,
        UndoActionType actionType,
        string description,
        T data) where T : class
    {
        // 새 작업 시 Redo 스택 비움
        await _undoRepo.ClearRedoStackAsync(courseId, room);

        var action = new UndoAction
        {
            CourseId = courseId,
            Room = room,
            ActionType = actionType,
            Description = description,
            CreatedAt = DateTime.Now
        };
        action.SetData(data);

        await _undoRepo.CreateAsync(action);
    }

    #endregion

    #region Public Query Methods

    /// <summary>
    /// Undo 가능 여부
    /// </summary>
    public async Task<bool> CanUndoAsync(int courseId, string room)
    {
        return await _undoRepo.CanUndoAsync(courseId, room);
    }

    /// <summary>
    /// Redo 가능 여부
    /// </summary>
    public async Task<bool> CanRedoAsync(int courseId, string room)
    {
        return await _undoRepo.CanRedoAsync(courseId, room);
    }

    /// <summary>
    /// 최근 Undo 작업 목록
    /// </summary>
    public async Task<List<UndoAction>> GetUndoableActionsAsync(int courseId, string room, int limit = 10)
    {
        return await _undoRepo.GetUndoableActionsAsync(courseId, room, limit);
    }

    #endregion
}

#region Result Classes

/// <summary>
/// 이동 결과
/// </summary>
public class ShiftResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int ShiftedCount { get; set; }
    public int OverflowCount { get; set; }
}

/// <summary>
/// Undo/Redo 결과
/// </summary>
public class UndoRedoResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string ActionDescription { get; set; } = string.Empty;
}

#endregion

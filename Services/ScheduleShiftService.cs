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

    #region Plan (계획 산출 — 미리보기용, DB 변경 없음)

    /// <summary>
    /// 밀기 계획 산출 — 어떤 수업이 어디로 이동하는지 계산만 하고 DB 는 변경하지 않는다.
    /// 미리보기 UI 와 실제 실행(PushSchedulesAsync)이 같은 계획을 공유한다.
    /// </summary>
    public async Task<ShiftPlan> BuildPushPlanAsync(
        int courseId, string room, DateTime fromDate, int fromPeriod, DateTime semesterEnd)
    {
        var plan = new ShiftPlan { Direction = 1 };

        // 1. 기준일 이후 모든 비고정 스케줄 조회 (역순 — 맨 뒤부터 밀어야 충돌 방지)
        var schedules = await _scheduleRepo.GetUnpinnedSchedulesFromDateAsync(
            courseId, room, fromDate);
        schedules = schedules
            .Where(s => s.Date > fromDate || (s.Date.Date == fromDate.Date && s.Period >= fromPeriod))
            .OrderByDescending(s => s.Date)
            .ThenByDescending(s => s.Period)
            .ToList();

        if (schedules.Count == 0)
            return plan;

        // 2. 가용 슬롯 + 비이동 스케줄(고정 수업 등) 점유 시딩 — 이중 배치 방지
        var availableSlots = await GenerateAvailableSlotsAsync(courseId, room, fromDate, semesterEnd);
        var occupiedSlots = await SeedOccupiedSlotsAsync(courseId, room, schedules);

        foreach (var schedule in schedules)
        {
            var nextSlot = FindNextSlot(availableSlots, (schedule.Date, schedule.Period), occupiedSlots);

            if (nextSlot == null)
            {
                // 더 이상 밀 수 없음 (학기 종료 또는 이후 슬롯이 모두 점유됨)
                plan.OverflowCount++;
                continue;
            }

            plan.Moves.Add(new ShiftPlanItem
            {
                Schedule = schedule,
                NewDate = nextSlot.Value.Date,
                NewPeriod = nextSlot.Value.Period
            });
            occupiedSlots.Add((nextSlot.Value.Date.Date, nextSlot.Value.Period));
        }

        return plan;
    }

    /// <summary>
    /// 당기기 계획 산출 — DB 변경 없음. (BuildPushPlanAsync 와 대칭)
    /// </summary>
    public async Task<ShiftPlan> BuildPullPlanAsync(
        int courseId, string room, DateTime fromDate, int fromPeriod, DateTime semesterStart)
    {
        var plan = new ShiftPlan { Direction = -1 };

        // 1. 기준일 이후 모든 비고정 스케줄 조회 (정순 — 앞에서부터 당겨야 충돌 방지)
        var schedules = await _scheduleRepo.GetUnpinnedSchedulesFromDateAsync(
            courseId, room, fromDate);
        schedules = schedules
            .Where(s => s.Date > fromDate || (s.Date.Date == fromDate.Date && s.Period >= fromPeriod))
            .OrderBy(s => s.Date)
            .ThenBy(s => s.Period)
            .ToList();

        if (schedules.Count == 0)
            return plan;

        // 2. 가용 슬롯 + 비이동 스케줄(고정 수업, 기준일 이전 수업) 점유 시딩 — 이중 배치 방지
        var availableSlots = await GenerateAvailableSlotsAsync(courseId, room, semesterStart, fromDate.AddMonths(6));
        var occupiedSlots = await SeedOccupiedSlotsAsync(courseId, room, schedules);

        foreach (var schedule in schedules)
        {
            var prevSlot = FindPreviousSlot(availableSlots, (schedule.Date, schedule.Period), occupiedSlots);

            if (prevSlot == null)
            {
                // 더 이상 당길 수 없음 (이전 슬롯이 모두 점유됨) — 현 위치를 점유로 유지
                occupiedSlots.Add((schedule.Date.Date, schedule.Period));
                plan.OverflowCount++;
                continue;
            }

            plan.Moves.Add(new ShiftPlanItem
            {
                Schedule = schedule,
                NewDate = prevSlot.Value.Date,
                NewPeriod = prevSlot.Value.Period
            });
            occupiedSlots.Add((prevSlot.Value.Date.Date, prevSlot.Value.Period));
        }

        return plan;
    }

    /// <summary>이동하지 않는(비이동·비취소) 스케줄의 슬롯을 점유 집합으로 시딩.</summary>
    private async Task<HashSet<(DateTime, int)>> SeedOccupiedSlotsAsync(
        int courseId, string room, List<Schedule> movingSchedules)
    {
        var movingIds = movingSchedules.Select(s => s.No).ToHashSet();
        var allSchedules = await _scheduleRepo.GetByCourseAndRoomAsync(courseId, room);
        return new HashSet<(DateTime, int)>(
            allSchedules
                .Where(s => !movingIds.Contains(s.No) && !s.IsCancelled)
                .Select(s => (s.Date.Date, s.Period)));
    }

    #endregion

    #region Push / Pull (실행)

    /// <summary>
    /// 수업 밀기 (지정 날짜 이후 모든 수업을 다음 슬롯으로)
    /// </summary>
    /// <param name="courseId">과목 번호</param>
    /// <param name="room">학급</param>
    /// <param name="fromDate">기준 날짜</param>
    /// <param name="fromPeriod">기준 교시 (해당 슬롯 포함)</param>
    /// <param name="semesterEnd">학기 종료일</param>
    /// <param name="plan">미리보기에서 이미 산출한 계획(선택). 없으면 새로 산출한다.</param>
    public async Task<ShiftResult> PushSchedulesAsync(
        int courseId,
        string room,
        DateTime fromDate,
        int fromPeriod,
        DateTime semesterEnd,
        ShiftPlan? plan = null)
    {
        var result = new ShiftResult();

        try
        {
            plan ??= await BuildPushPlanAsync(courseId, room, fromDate, fromPeriod, semesterEnd);

            if (plan.Moves.Count == 0 && plan.OverflowCount == 0)
            {
                result.Success = true;
                result.Message = "이동할 수업이 없습니다.";
                return result;
            }

            var shiftData = await ApplyPlanAsync(plan, fromDate, fromPeriod, result);

            // Undo 기록 저장
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

    /// <summary>
    /// 수업 당기기 (지정 날짜 이후 모든 수업을 이전 슬롯으로)
    /// </summary>
    /// <param name="plan">미리보기에서 이미 산출한 계획(선택). 없으면 새로 산출한다.</param>
    public async Task<ShiftResult> PullSchedulesAsync(
        int courseId,
        string room,
        DateTime fromDate,
        int fromPeriod,
        DateTime semesterStart,
        ShiftPlan? plan = null)
    {
        var result = new ShiftResult();

        try
        {
            plan ??= await BuildPullPlanAsync(courseId, room, fromDate, fromPeriod, semesterStart);

            if (plan.Moves.Count == 0 && plan.OverflowCount == 0)
            {
                result.Success = true;
                result.Message = "이동할 수업이 없습니다.";
                return result;
            }

            var shiftData = await ApplyPlanAsync(plan, fromDate, fromPeriod, result);

            // Undo 기록 저장
            if (result.ShiftedCount > 0)
            {
                await SaveUndoActionAsync(courseId, room, UndoActionType.ScheduleShift,
                    $"{fromDate:M/d} {fromPeriod}교시부터 {result.ShiftedCount}개 수업 당기기",
                    shiftData);
            }

            result.Success = true;
            result.Message = $"{result.ShiftedCount}개 수업을 이전 슬롯으로 이동했습니다.";
            if (result.OverflowCount > 0)
            {
                result.Message += $" ({result.OverflowCount}개는 빈 슬롯이 없어 이동 불가)";
            }

            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"당기기 실패: {ex.Message}";
            return result;
        }
    }

    /// <summary>
    /// 계획을 DB 에 적용 — 이동 전체를 단일 트랜잭션으로(중간 실패 시 일부만 이동된 상태 방지,
    /// 건당 자동커밋 fsync 제거). Undo 용 ShiftActionData 를 반환한다.
    /// </summary>
    private async Task<ShiftActionData> ApplyPlanAsync(
        ShiftPlan plan, DateTime fromDate, int fromPeriod, ShiftResult result)
    {
        var shiftData = new ShiftActionData
        {
            Direction = plan.Direction,
            FromDate = fromDate,
            FromPeriod = fromPeriod
        };

        result.OverflowCount = plan.OverflowCount;

        _scheduleRepo.BeginTransaction();
        try
        {
            foreach (var move in plan.Moves)
            {
                shiftData.ShiftedSchedules.Add(new ScheduleShiftInfo
                {
                    ScheduleId = move.Schedule.No,
                    OriginalDate = move.Schedule.Date,
                    OriginalPeriod = move.Schedule.Period,
                    NewDate = move.NewDate,
                    NewPeriod = move.NewPeriod
                });

                move.Schedule.Date = move.NewDate;
                move.Schedule.Period = move.NewPeriod;
                await _scheduleRepo.UpdateAsync(move.Schedule);
                result.ShiftedCount++;
            }

            _scheduleRepo.Commit();
        }
        catch
        {
            _scheduleRepo.Rollback();
            throw;
        }

        return shiftData;
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

        // 적용의 정확한 역순으로 되돌린다.
        //
        // ⚠ 순서가 핵심이다. 예전에는 원래 날짜 오름차순으로 되돌렸는데, 그건 밀기에만
        //   우연히 맞는 순서였다. 당기기는 계획이 앞→뒤 정순이라 오름차순으로 되돌리면
        //   "아직 비켜나지 않은 뒷 수업의 자리"로 먼저 되돌리려다 슬롯 UNIQUE 제약
        //   (CourseId·Room·Date·Period)에 걸려 <b>당기기 취소가 통째로 실패</b>했다.
        //   적용 순서를 뒤집으면 항상 뒤에서부터 자리가 비므로 두 방향 모두 안전하다.
        await MoveSchedulesAsync(
            Enumerable.Reverse(data.ShiftedSchedules),
            shift => (shift.OriginalDate, shift.OriginalPeriod));
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

        if (data.Slots.Count > 0)
        {
            // 슬롯(날짜·교시) 기준 삭제 — Redo 로 재생성돼 ID 가 바뀌어도 항상 현재 스케줄을 찾아 지운다.
            // (자동 배치는 clearExisting 으로 기존 배치를 먼저 비우므로 이 슬롯들은 이 배치가 만든 것)
            var slotSet = data.Slots.Select(s => (s.Date.Date, s.Period)).ToHashSet();
            var existing = await _scheduleRepo.GetByCourseAndRoomAsync(action.CourseId, action.Room);
            foreach (var schedule in existing.Where(s => slotSet.Contains((s.Date.Date, s.Period))))
            {
                await _mapRepo.DeleteByScheduleAsync(schedule.No);
                await _scheduleRepo.DeleteAsync(schedule.No);
            }
        }
        else
        {
            // 구 기록 호환: 저장된 생성 ID 로 삭제
            foreach (var scheduleId in data.CreatedScheduleIds)
            {
                await _mapRepo.DeleteByScheduleAsync(scheduleId);
                await _scheduleRepo.DeleteAsync(scheduleId);
            }
        }
    }

    #endregion

    #region Redo Implementations

    private async Task RedoShiftAsync(UndoAction action)
    {
        var data = action.GetData<ShiftActionData>();
        if (data == null) return;

        // 저장된 순서 = 적용 순서 그대로 다시 옮긴다
        await MoveSchedulesAsync(
            data.ShiftedSchedules,
            shift => (shift.NewDate, shift.NewPeriod));
    }

    /// <summary>
    /// 기록된 이동대로 스케줄을 옮긴다. <paramref name="shifts"/> 는 반드시 <b>앞의 수업이 먼저
    /// 비켜나는 순서</b>여야 한다 — 어긋나면 슬롯 UNIQUE 제약에 걸려 통째로 실패한다.
    /// <c>ApplyPlanAsync</c> 와 동일하게 단일 트랜잭션으로 처리해, 중간에 실패해도
    /// "일부만 되돌아간" 상태가 남지 않는다.
    /// </summary>
    private async Task MoveSchedulesAsync(
        IEnumerable<ScheduleShiftInfo> shifts,
        Func<ScheduleShiftInfo, (DateTime Date, int Period)> target)
    {
        _scheduleRepo.BeginTransaction();
        try
        {
            foreach (var shift in shifts)
            {
                var schedule = await _scheduleRepo.GetByIdAsync(shift.ScheduleId);
                if (schedule == null) continue;

                var (date, period) = target(shift);
                schedule.Date = date;
                schedule.Period = period;
                await _scheduleRepo.UpdateAsync(schedule);
            }

            _scheduleRepo.Commit();
        }
        catch
        {
            _scheduleRepo.Rollback();
            throw;
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
        var data = action.GetData<BulkGenerateActionData>();
        if (data == null || data.Slots.Count == 0)
        {
            // 슬롯 스냅샷이 없는 구 기록은 정확한 복원 정보가 없어 재실행 불가
            throw new NotSupportedException(
                "이 배치는 다시 실행에 필요한 정보를 담고 있지 않습니다. 자동 배치를 다시 수행해주세요.");
        }

        // 스냅샷대로 슬롯을 재생성 (SchedulingEngine.PlaceSectionToSlotAsync 와 동일한 방식)
        foreach (var slot in data.Slots)
        {
            var schedule = await _scheduleRepo.GetOrCreateAsync(
                action.CourseId, action.Room, slot.Date, slot.Period);

            if (slot.IsPinned && !schedule.IsPinned)
            {
                schedule.IsPinned = true;
                await _scheduleRepo.UpdateAsync(schedule);
            }

            foreach (var sectionId in slot.SectionIds)
            {
                if (!await _mapRepo.ExistsAsync(schedule.No, sectionId))
                    await _mapRepo.AddUnitToScheduleAsync(schedule.No, sectionId);
            }
        }
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
            // [start, end) 반개구간이라 endDate 당일이 빠진다 — 자동배치와 같은 기준을 쓰려면
            // 하루를 더해 포함시켜야 한다(안 그러면 마지막 날 휴일로 수업을 밀어 넣게 된다).
            var schedules = await _schoolScheduleRepo.GetByDateRangeAsync(
                Settings.SchoolCode.Value, startDate, endDate.AddDays(1));
            foreach (var schedule in schedules)
            {
                // 최초 자동배치(SchedulingEngine)와 반드시 같은 기준이어야 한다 —
                // 밀기/당기기가 최초 배치 땐 뺐던 공휴일 슬롯으로 수업을 옮기면 안 되므로
                // 판정은 SchoolCalendar 한 곳에서만 한다
                if (Helpers.SchoolCalendar.IsNonTeachingDay(schedule))
                    holidays.Add(schedule.AA_YMD.Date);
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

            int dayOfWeek = Helpers.SchoolCalendar.ToLessonDayOfWeek(date);
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
        (DateTime Date, int Period) current,
        HashSet<(DateTime, int)> occupied)
    {
        var idx = slots.FindIndex(s => s.Date.Date == current.Date.Date && s.Period == current.Period);
        if (idx < 0)
            return null;

        // 이후 슬롯 중 점유되지 않은 첫 슬롯 (고정 수업·이미 밀린 수업이 차지한 슬롯은 건너뜀)
        for (int i = idx + 1; i < slots.Count; i++)
        {
            var slot = slots[i];
            if (!occupied.Contains((slot.Date.Date, slot.Period)))
                return slot;
        }

        return null;
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
            if (!occupied.Contains((slot.Date.Date, slot.Period)))
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
/// 이동 계획 (미리보기·실행 공용). DB 변경 없이 산출된다.
/// </summary>
public class ShiftPlan
{
    /// <summary>1=밀기(Push), -1=당기기(Pull)</summary>
    public int Direction { get; set; }

    /// <summary>이동 목록 (Push 는 뒤→앞 역순, Pull 은 앞→뒤 정순 — 적용 순서 그대로)</summary>
    public List<ShiftPlanItem> Moves { get; } = new();

    /// <summary>이동 불가(빈 슬롯 없음) 수업 수</summary>
    public int OverflowCount { get; set; }
}

/// <summary>
/// 이동 계획 항목
/// </summary>
public class ShiftPlanItem
{
    public required Schedule Schedule { get; init; }
    public DateTime NewDate { get; init; }
    public int NewPeriod { get; init; }

    /// <summary>미리보기 표시용: "3/4(수) 1교시 → 3/5(목) 1교시"</summary>
    public string Display =>
        $"{Schedule.Date:M/d(ddd)} {Schedule.Period}교시 → {NewDate:M/d(ddd)} {NewPeriod}교시";
}

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

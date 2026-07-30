using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Repositories;

namespace NewSchool.Services;

/// <summary>
/// 지능형 배치 엔진
/// Anchor-Fill-Alert 3단계 알고리즘으로 수업 자동 배치
/// </summary>
public class SchedulingEngine
{
    private readonly ScheduleRepository _scheduleRepo;
    private readonly ScheduleUnitMapRepository _mapRepo;
    private readonly CourseSectionRepository _sectionRepo;
    private readonly LessonRepository _lessonRepo;
    private readonly SchoolScheduleRepository _schoolScheduleRepo;

    public SchedulingEngine(
        ScheduleRepository scheduleRepo,
        ScheduleUnitMapRepository mapRepo,
        CourseSectionRepository sectionRepo,
        LessonRepository lessonRepo,
        SchoolScheduleRepository schoolScheduleRepo)
    {
        _scheduleRepo = scheduleRepo;
        _mapRepo = mapRepo;
        _sectionRepo = sectionRepo;
        _lessonRepo = lessonRepo;
        _schoolScheduleRepo = schoolScheduleRepo;
    }

    #region Main Entry Point

    /// <summary>
    /// 수업 스케줄 자동 생성 (메인 메서드)
    /// </summary>
    /// <param name="courseId">과목 번호</param>
    /// <param name="room">학급/강의실</param>
    /// <param name="startDate">학기 시작일</param>
    /// <param name="endDate">학기 종료일</param>
    public async Task<SchedulingResult> GenerateScheduleAsync(
        int courseId,
        string room,
        DateTime startDate,
        DateTime endDate)
    {
        var result = new SchedulingResult
        {
            CourseId = courseId,
            Room = room,
            StartDate = startDate,
            EndDate = endDate
        };

        try
        {
            // 1. 데이터 로드 + 검증 (읽기 전용 — 기존 배치 삭제는 검증 통과 후에만 수행한다.
            //    이전에는 검증 전에 삭제해, "단원 없음" 등으로 조기 반환해도 기존 배치가 사라졌음)
            var sections = await _sectionRepo.GetByCourseAsync(courseId);
            var lessons = await _lessonRepo.GetByCourseAsync(courseId);
            var (holidays, calendarWarning) = await GetHolidaysAsync(startDate, endDate);
            result.CalendarWarning = calendarWarning;

            if (sections.Count == 0)
            {
                result.Success = false;
                result.Message = "배치할 단원이 없습니다.";
                return result;
            }

            if (lessons.Count == 0)
            {
                result.Success = false;
                result.Message = "시간표가 설정되지 않았습니다.";
                return result;
            }

            // 해당 학급의 시간표만 필터링
            var roomLessons = lessons.Where(l => l.Room == room).ToList();
            if (roomLessons.Count == 0)
            {
                result.Success = false;
                result.Message = $"{room}의 시간표가 없습니다.";
                return result;
            }

            // 2. 가용 슬롯 생성
            var availableSlots = GenerateAvailableSlots(roomLessons, startDate, endDate, holidays);
            result.TotalAvailableSlots = availableSlots.Count;

            if (availableSlots.Count == 0)
            {
                result.Success = false;
                result.Message = "가용 슬롯이 없습니다.";
                return result;
            }

            // 3. 단원 분류 (고정 vs 일반)
            var pinnedSections = sections.Where(s => s.IsFixed).ToList();
            var normalSections = sections.Where(s => !s.IsFixed).ToList();

            // 4~5. 쓰기 단계(기존 삭제 → Anchor → Fill)를 단일 트랜잭션으로 —
            //  · 원자성: 중간 실패 시 롤백되어 "기존 배치만 지워지고 새 배치는 일부만" 남는 것을 방지
            //  · 성능: 수백 건의 INSERT/UPDATE 를 자동커밋(건당 fsync) 대신 한 번에 커밋
            // 두 리포지토리가 연결을 공유할 때만 가능(호출부에서 공유 연결로 생성). 아니면 종전대로 개별 커밋.
            bool atomic = ReferenceEquals(_scheduleRepo.GetConnection(), _mapRepo.GetConnection());
            if (atomic)
            {
                _scheduleRepo.BeginTransaction();
                _mapRepo.SetTransaction(_scheduleRepo.GetTransaction());
            }

            PlacementResult anchorResult, fillResult;
            try
            {
                // 기존 스케줄 삭제 (ON DELETE CASCADE 로 ScheduleUnitMap 도 함께)
                await _scheduleRepo.DeleteByCourseAndRoomAsync(courseId, room);

                // Step 1: Anchor - 고정 단원 배치
                anchorResult = await PlaceAnchorSectionsAsync(
                    courseId, room, pinnedSections, availableSlots);

                // Step 2: Fill - 일반 단원 순차 배치
                fillResult = await FillRemainingSlotsAsync(
                    courseId, room, normalSections, availableSlots);

                if (atomic)
                {
                    _scheduleRepo.Commit();
                    _mapRepo.SetTransaction(null);
                }
            }
            catch
            {
                if (atomic)
                {
                    _scheduleRepo.Rollback();
                    _mapRepo.SetTransaction(null);
                }
                throw;
            }

            result.AnchoredCount = anchorResult.PlacedCount;
            result.AnchorFailures.AddRange(anchorResult.Failures);
            result.FilledCount = fillResult.PlacedCount;
            result.FillWarnings.AddRange(fillResult.Failures);
            result.CreatedScheduleIds.AddRange(anchorResult.CreatedScheduleIds);
            result.CreatedScheduleIds.AddRange(fillResult.CreatedScheduleIds);

            // 6. Step 3: Alert - 미배치 단원 확인
            var unplacedSections = sections
                .Where(s => !anchorResult.PlacedSectionIds.Contains(s.No) &&
                           !fillResult.PlacedSectionIds.Contains(s.No))
                .ToList();

            result.UnplacedSections = unplacedSections;
            result.UnplacedCount = unplacedSections.Count;

            // 7. 결과 계산
            result.TotalPlaced = result.AnchoredCount + result.FilledCount;
            result.TotalSections = sections.Count;

            // 부족 시수 계산
            int totalRequiredHours = sections.Sum(s => s.EstimatedHours);
            result.RequiredHours = totalRequiredHours;
            result.AvailableHours = availableSlots.Count;
            result.ExcessHours = result.AvailableHours - totalRequiredHours;

            result.Success = result.UnplacedCount == 0;
            result.Message = result.Success
                ? $"배치 완료: {result.TotalPlaced}개 단원 배치됨"
                : $"배치 완료: {result.TotalPlaced}개 배치, {result.UnplacedCount}개 미배치";
            if (result.FillWarnings.Count > 0)
            {
                result.Message += $" ({result.FillWarnings.Count}개 단원 시수 부족)";
            }
            if (!string.IsNullOrEmpty(result.CalendarWarning))
            {
                result.Message += " / 주의: " + result.CalendarWarning;
            }

            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"배치 오류: {ex.Message}";
            return result;
        }
    }

    #endregion

    #region Step 1: Anchor (고정 배치)

    /// <summary>
    /// 고정 단원 배치 (지필고사, 수행평가 등)
    /// </summary>
    private async Task<PlacementResult> PlaceAnchorSectionsAsync(
        int courseId,
        string room,
        List<CourseSection> pinnedSections,
        List<SlotInfo> availableSlots)
    {
        var result = new PlacementResult();

        foreach (var section in pinnedSections.OrderBy(s => s.PinnedDate))
        {
            if (!section.PinnedDate.HasValue)
            {
                result.Failures.Add(new PlacementFailure
                {
                    Section = section,
                    Reason = "고정 날짜가 설정되지 않음"
                });
                continue;
            }

            var targetDate = section.PinnedDate.Value.Date;

            // 해당 날짜의 가용 슬롯 찾기
            var slot = availableSlots.FirstOrDefault(s =>
                s.Date.Date == targetDate && !s.IsOccupied);

            if (slot == null)
            {
                // 해당 날짜에 슬롯이 없으면 지정일 이후 가장 이른 슬롯에 배치.
                // (지필/수행 등 고정 단원을 지정일보다 앞당기지 않도록 이후 슬롯만 대상으로 함)
                slot = availableSlots
                    .Where(s => !s.IsOccupied && s.Date.Date >= targetDate)
                    .OrderBy(s => s.Date)
                    .ThenBy(s => s.Period)
                    .FirstOrDefault();

                if (slot != null)
                {
                    result.Failures.Add(new PlacementFailure
                    {
                        Section = section,
                        Reason = $"지정일({targetDate:M/d})에 슬롯 없음, {slot.Date:M/d}에 배치됨",
                        IsWarning = true
                    });
                }
            }

            if (slot != null)
            {
                // 배치 실행
                result.CreatedScheduleIds.Add(
                    await PlaceSectionToSlotAsync(courseId, room, section, slot));
                slot.IsOccupied = true;
                slot.OccupiedBySectionId = section.No;
                result.PlacedCount++;
                result.PlacedSectionIds.Add(section.No);
            }
            else
            {
                result.Failures.Add(new PlacementFailure
                {
                    Section = section,
                    Reason = "배치 가능한 슬롯 없음"
                });
            }
        }

        return result;
    }

    #endregion

    #region Step 2: Fill (순차 배치)

    /// <summary>
    /// 일반 단원 순차 배치
    /// </summary>
    private async Task<PlacementResult> FillRemainingSlotsAsync(
        int courseId,
        string room,
        List<CourseSection> normalSections,
        List<SlotInfo> availableSlots)
    {
        var result = new PlacementResult();

        // 빈 슬롯만 필터 (날짜순)
        var emptySlots = availableSlots
            .Where(s => !s.IsOccupied)
            .OrderBy(s => s.Date)
            .ThenBy(s => s.Period)
            .ToList();

        int slotIndex = 0;

        foreach (var section in normalSections.OrderBy(s => s.SortOrder))
        {
            // 필요한 슬롯 수 (EstimatedHours 기준)
            int requiredSlots = section.EstimatedHours;
            int placedSlots = 0;

            while (placedSlots < requiredSlots && slotIndex < emptySlots.Count)
            {
                var slot = emptySlots[slotIndex];

                // 배치 실행
                result.CreatedScheduleIds.Add(
                    await PlaceSectionToSlotAsync(courseId, room, section, slot));
                slot.IsOccupied = true;
                slot.OccupiedBySectionId = section.No;

                placedSlots++;
                slotIndex++;
            }

            if (placedSlots > 0)
            {
                result.PlacedCount++;
                result.PlacedSectionIds.Add(section.No);

                // 필요 시수의 일부만 배치된 경우 — "배치됨"으로만 집계하면 시수 부족이
                // 총계(ExcessHours)로만 간접 확인 가능하므로 단원별 경고로 명시한다
                if (placedSlots < requiredSlots)
                {
                    result.Failures.Add(new PlacementFailure
                    {
                        Section = section,
                        Reason = $"시수 부족 배치: {placedSlots}/{requiredSlots}차시",
                        IsWarning = true
                    });
                }
            }

            // 모든 슬롯 소진 시 중단
            if (slotIndex >= emptySlots.Count && placedSlots < requiredSlots)
            {
                break;
            }
        }

        return result;
    }

    #endregion

    #region Slot Generation

    /// <summary>
    /// 가용 슬롯 목록 생성
    /// </summary>
    private List<SlotInfo> GenerateAvailableSlots(
        List<Lesson> lessons,
        DateTime startDate,
        DateTime endDate,
        HashSet<DateTime> holidays)
    {
        var slots = new List<SlotInfo>();

        // 요일별 시간표 그룹화
        var lessonsByDay = lessons
            .GroupBy(l => l.DayOfWeek)
            .ToDictionary(g => g.Key, g => g.OrderBy(l => l.Period).ToList());

        // 날짜 순회
        for (var date = startDate.Date; date <= endDate.Date; date = date.AddDays(1))
        {
            // 휴일 제외
            if (holidays.Contains(date))
                continue;

            // 해당 요일의 수업 확인 (Lesson.DayOfWeek 규약: 월=1 … 일=7)
            int dayOfWeek = Helpers.SchoolCalendar.ToLessonDayOfWeek(date);
            if (!lessonsByDay.ContainsKey(dayOfWeek))
                continue;

            // 슬롯 생성
            foreach (var lesson in lessonsByDay[dayOfWeek])
            {
                slots.Add(new SlotInfo
                {
                    Date = date,
                    Period = lesson.Period,
                    Room = lesson.Room,
                    IsOccupied = false
                });
            }
        }

        return slots;
    }

    /// <summary>
    /// 휴일 목록 조회. 조회에 실패하면 <b>휴일을 하나도 못 걸러낸 채</b> 배치가 진행되므로
    /// 그 사실을 경고 문구로 함께 돌려준다.
    ///
    /// ⚠ 예전에는 실패를 통째로 삼켜서(빈 catch), 학사일정을 못 읽으면 공휴일·방학에도
    /// 수업이 배치되는데 사용자는 아무것도 모른 채 정상 배치로 받아들였다.
    /// </summary>
    private async Task<(HashSet<DateTime> Holidays, string? Warning)> GetHolidaysAsync(
        DateTime startDate, DateTime endDate)
    {
        var holidays = new HashSet<DateTime>();

        try
        {
            var schedules = await _schoolScheduleRepo.GetByDateRangeAsync(
                Settings.SchoolCode.Value, startDate, endDate);

            foreach (var schedule in schedules)
            {
                // 휴업일·공휴일·방학 판정은 밀기/당기기와 같은 기준을 써야 한다
                if (Helpers.SchoolCalendar.IsNonTeachingDay(schedule))
                    holidays.Add(schedule.AA_YMD.Date);
            }

            return (holidays, null);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SchedulingEngine] 학사일정 조회 실패: {ex}");
            return (holidays, "학사일정을 읽지 못해 휴일(공휴일·방학 등)을 제외하지 못했습니다. 배치 결과를 확인해주세요.");
        }
    }

    #endregion

    #region Placement Execution

    /// <summary>
    /// 단원을 슬롯에 배치 (DB 저장). 생성/사용된 Schedule No 를 반환한다(Undo 기록용).
    /// </summary>
    private async Task<int> PlaceSectionToSlotAsync(
        int courseId,
        string room,
        CourseSection section,
        SlotInfo slot)
    {
        // 1. Schedule 생성 또는 조회
        var schedule = await _scheduleRepo.GetOrCreateAsync(
            courseId, room, slot.Date, slot.Period);

        // 고정 단원이면 Schedule도 고정
        if (section.IsFixed)
        {
            schedule.IsPinned = true;
            await _scheduleRepo.UpdateAsync(schedule);
        }

        // 2. ScheduleUnitMap 생성
        var exists = await _mapRepo.ExistsAsync(schedule.No, section.No);
        if (!exists)
        {
            await _mapRepo.AddUnitToScheduleAsync(schedule.No, section.No);
        }

        return schedule.No;
    }

    #endregion

    #region Validation

    /// <summary>
    /// 스케줄 유효성 검사
    /// </summary>
    public async Task<ValidationResult> ValidateScheduleAsync(int courseId, string room)
    {
        var result = new ValidationResult();

        // 1. 단원 조회
        var sections = await _sectionRepo.GetByCourseAsync(courseId);
        var schedules = await _scheduleRepo.GetByCourseAndRoomAsync(courseId, room);

        // 2. 매핑을 IN 절로 한 번에 조회 (기존: 스케줄마다 개별 조회를 두 루프에서 반복 — 2N회 쿼리)
        var allMaps = await _mapRepo.GetBySchedulesAsync(schedules.Select(s => s.No).ToList());

        // 3. 모든 단원이 배치되었는지 + 단원별 배치 횟수를 한 패스로 집계
        var placedSectionIds = new HashSet<int>();
        var sectionScheduleCount = new Dictionary<int, int>();
        foreach (var map in allMaps)
        {
            placedSectionIds.Add(map.CourseSectionId);
            sectionScheduleCount[map.CourseSectionId] =
                sectionScheduleCount.GetValueOrDefault(map.CourseSectionId) + 1;
        }

        var unplacedSections = sections.Where(s => !placedSectionIds.Contains(s.No)).ToList();
        if (unplacedSections.Count > 0)
        {
            result.Warnings.Add($"미배치 단원 {unplacedSections.Count}개: " +
                string.Join(", ", unplacedSections.Take(3).Select(s => s.SectionName)));
        }

        // EstimatedHours보다 많이 배치된 단원 확인
        foreach (var section in sections)
        {
            if (sectionScheduleCount.TryGetValue(section.No, out int count))
            {
                if (count > section.EstimatedHours)
                {
                    result.Warnings.Add(
                        $"'{section.SectionName}' 초과 배치: {count}/{section.EstimatedHours}시간");
                }
            }
        }

        result.IsValid = result.Errors.Count == 0;
        return result;
    }

    #endregion

    #region Preview (미리보기)

    /// <summary>
    /// 배치 미리보기 (실제 저장 없이 결과 확인)
    /// </summary>
    public async Task<SchedulingPreview> PreviewScheduleAsync(
        int courseId,
        string room,
        DateTime startDate,
        DateTime endDate)
    {
        var preview = new SchedulingPreview();

        // 데이터 로드
        var sections = await _sectionRepo.GetByCourseAsync(courseId);
        var lessons = await _lessonRepo.GetByCourseAsync(courseId);
        var (holidays, calendarWarning) = await GetHolidaysAsync(startDate, endDate);
        preview.CalendarWarning = calendarWarning;

        var roomLessons = lessons.Where(l => l.Room == room).ToList();

        // 가용 슬롯 생성
        var availableSlots = GenerateAvailableSlots(roomLessons, startDate, endDate, holidays);

        // 통계 계산
        preview.TotalSections = sections.Count;
        preview.TotalRequiredHours = sections.Sum(s => s.EstimatedHours);
        preview.TotalAvailableSlots = availableSlots.Count;
        preview.PinnedSections = sections.Count(s => s.IsFixed);
        preview.NormalSections = sections.Count(s => !s.IsFixed);

        // 주차별 슬롯 수
        preview.WeeklySlots = availableSlots
            .GroupBy(s => GetWeekNumber(s.Date, startDate))
            .OrderBy(g => g.Key)
            .Select(g => new WeeklySlotInfo
            {
                WeekNumber = g.Key,
                StartDate = g.Min(s => s.Date),
                EndDate = g.Max(s => s.Date),
                SlotCount = g.Count()
            })
            .ToList();

        // 예상 결과
        preview.CanComplete = preview.TotalAvailableSlots >= preview.TotalRequiredHours;
        preview.ExcessSlots = preview.TotalAvailableSlots - preview.TotalRequiredHours;

        return preview;
    }

    /// <summary>
    /// 주차 번호 계산
    /// </summary>
    private int GetWeekNumber(DateTime date, DateTime startDate)
    {
        return (int)((date - startDate).TotalDays / 7) + 1;
    }

    #endregion
}

#region Result Classes

/// <summary>
/// 배치 결과
/// </summary>
public class SchedulingResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;

    public int CourseId { get; set; }
    public string Room { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }

    public int TotalSections { get; set; }
    public int TotalPlaced { get; set; }
    public int AnchoredCount { get; set; }
    public int FilledCount { get; set; }
    public int UnplacedCount { get; set; }

    public int TotalAvailableSlots { get; set; }
    public int RequiredHours { get; set; }
    public int AvailableHours { get; set; }
    public int ExcessHours { get; set; }

    public List<CourseSection> UnplacedSections { get; set; } = new();
    public List<PlacementFailure> AnchorFailures { get; set; } = new();

    /// <summary>순차 배치 경고 — 슬롯 부족으로 필요 시수의 일부만 배치된 단원(IsWarning=true).</summary>
    public List<PlacementFailure> FillWarnings { get; set; } = new();

    /// <summary>이번 배치에서 생성된 Schedule No 목록 — BulkGenerate Undo 기록에 사용.</summary>
    public List<int> CreatedScheduleIds { get; set; } = new();

    /// <summary>학사일정을 읽지 못해 휴일을 제외하지 못했을 때의 경고. 정상이면 null.</summary>
    public string? CalendarWarning { get; set; }

    public double CompletionRate => TotalSections > 0 ? (double)TotalPlaced / TotalSections * 100 : 0;
}

/// <summary>
/// 내부 배치 결과
/// </summary>
public class PlacementResult
{
    public int PlacedCount { get; set; }
    public HashSet<int> PlacedSectionIds { get; set; } = new();
    public List<PlacementFailure> Failures { get; set; } = new();

    /// <summary>이 단계에서 생성/사용된 Schedule No 목록 (Undo 기록용).</summary>
    public List<int> CreatedScheduleIds { get; set; } = new();
}

/// <summary>
/// 배치 실패 정보
/// </summary>
public class PlacementFailure
{
    public CourseSection? Section { get; set; }
    public string Reason { get; set; } = string.Empty;
    public bool IsWarning { get; set; }
}

/// <summary>
/// 슬롯 정보 (내부용)
/// </summary>
public class SlotInfo
{
    public DateTime Date { get; set; }
    public int Period { get; set; }
    public string Room { get; set; } = string.Empty;
    public bool IsOccupied { get; set; }
    public int? OccupiedBySectionId { get; set; }

    public string Display => $"{Date:M/d(ddd)} {Period}교시";
}

/// <summary>
/// 유효성 검사 결과
/// </summary>
public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// 배치 미리보기
/// </summary>
public class SchedulingPreview
{
    public int TotalSections { get; set; }
    public int TotalRequiredHours { get; set; }
    public int TotalAvailableSlots { get; set; }
    public int PinnedSections { get; set; }
    public int NormalSections { get; set; }
    public bool CanComplete { get; set; }
    public int ExcessSlots { get; set; }

    /// <summary>학사일정을 읽지 못해 휴일을 제외하지 못했을 때의 경고. 정상이면 null.</summary>
    public string? CalendarWarning { get; set; }
    public List<WeeklySlotInfo> WeeklySlots { get; set; } = new();
}

/// <summary>
/// 주차별 슬롯 정보
/// </summary>
public class WeeklySlotInfo
{
    public int WeekNumber { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int SlotCount { get; set; }

    public string DateRange => $"{StartDate:M/d}~{EndDate:M/d}";
}

#endregion

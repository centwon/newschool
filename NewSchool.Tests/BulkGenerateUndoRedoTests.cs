using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.Services;
using NewSchool.Tests.Infrastructure;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 자동 배치의 Undo/Redo 회귀 테스트 — 슬롯 스냅샷 기반이라 스케줄 ID 가 재발급돼도
/// Undo→Redo→Undo 반복이 정확히 동작하는지 검증한다(구현 전에는 Redo 가 NotSupportedException).
/// </summary>
public class BulkGenerateUndoRedoTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _db;

    private static readonly DateTime WeekStart = new(2026, 3, 2); // 월
    private static readonly DateTime WeekEnd = new(2026, 3, 6);   // 금
    private const string Room = "1-1";

    public BulkGenerateUndoRedoTests(SqliteTestFixture db) => _db = db;

    private ScheduleShiftService NewShiftService() => new(
        new ScheduleRepository(_db.DbPath),
        new ScheduleUnitMapRepository(_db.DbPath),
        new UndoHistoryRepository(_db.DbPath),
        new LessonRepository(_db.DbPath),
        new SchoolScheduleRepository(_db.DbPath));

    /// <summary>과목 + 월~금 1교시 시간표 + 단원(5차시)을 만들고 자동 배치까지 실행, 생성 결과 반환.</summary>
    private async Task<int> SetupAndGenerateAsync()
    {
        using var courseRepo = new CourseRepository(_db.DbPath);
        int courseNo = await courseRepo.CreateAsync(
            TestData.NewCourse(subject: $"배치UndoRedo_{Guid.NewGuid():N}"));

        using (var lessonRepo = new LessonRepository(_db.DbPath))
        {
            for (int dow = 1; dow <= 5; dow++)
                await lessonRepo.CreateAsync(new Lesson { Course = courseNo, DayOfWeek = dow, Period = 1, Room = Room });
        }
        using (var sectionRepo = new CourseSectionRepository(_db.DbPath))
        {
            await sectionRepo.CreateAsync(TestData.NewSection(courseNo, sectionNo: 1, sectionName: "1절", hours: 3));
            await sectionRepo.CreateAsync(TestData.NewSection(courseNo, sectionNo: 2, sectionName: "2절", hours: 2));
        }

        // 실제 호출부와 동일하게 공유 연결로 엔진 실행 후, 페이지와 같은 방식으로 Undo 기록 저장
        List<int> createdIds;
        List<BulkScheduleSlot> slots;
        using (var scheduleRepo = new ScheduleRepository(_db.DbPath))
        using (var mapRepo = new ScheduleUnitMapRepository(scheduleRepo.GetConnection()))
        using (var sectionRepo = new CourseSectionRepository(_db.DbPath))
        using (var lessonRepo = new LessonRepository(_db.DbPath))
        using (var schoolScheduleRepo = new SchoolScheduleRepository(_db.DbPath))
        {
            var engine = new SchedulingEngine(scheduleRepo, mapRepo, sectionRepo, lessonRepo, schoolScheduleRepo);
            var result = await engine.GenerateScheduleAsync(courseNo, Room, WeekStart, WeekEnd);
            Assert.True(result.CreatedScheduleIds.Count > 0);
            createdIds = result.CreatedScheduleIds.Distinct().ToList();
            slots = await BuildSlotsAsync(scheduleRepo, mapRepo, courseNo, createdIds);
        }

        using (var undoRepo = new UndoHistoryRepository(_db.DbPath))
        {
            var action = new UndoAction
            {
                CourseId = courseNo,
                Room = Room,
                ActionType = UndoActionType.BulkGenerate,
                Description = $"자동 배치 ({slots.Count}개 슬롯)",
                CreatedAt = DateTime.Now
            };
            action.SetData(new BulkGenerateActionData
            {
                CreatedScheduleIds = createdIds,
                Slots = slots,
                StartDate = WeekStart,
                EndDate = WeekEnd
            });
            await undoRepo.CreateAsync(action);
        }

        return courseNo;
    }

    private static async Task<List<BulkScheduleSlot>> BuildSlotsAsync(
        ScheduleRepository scheduleRepo, ScheduleUnitMapRepository mapRepo, int courseNo, List<int> createdIds)
    {
        var byId = (await scheduleRepo.GetByCourseAndRoomAsync(courseNo, Room)).ToDictionary(s => s.No);
        var sectionsById = (await mapRepo.GetBySchedulesAsync(createdIds))
            .GroupBy(m => m.ScheduleId).ToDictionary(g => g.Key, g => g.Select(m => m.CourseSectionId).ToList());

        var slots = new List<BulkScheduleSlot>();
        foreach (var id in createdIds)
        {
            if (!byId.TryGetValue(id, out var s)) continue;
            slots.Add(new BulkScheduleSlot
            {
                Date = s.Date, Period = s.Period, IsPinned = s.IsPinned,
                SectionIds = sectionsById.GetValueOrDefault(id) ?? new List<int>()
            });
        }
        return slots;
    }

    private async Task<(int Schedules, int Maps)> CountAsync(int courseNo)
    {
        using var scheduleRepo = new ScheduleRepository(_db.DbPath);
        using var mapRepo = new ScheduleUnitMapRepository(_db.DbPath);
        var schedules = await scheduleRepo.GetByCourseAndRoomAsync(courseNo, Room);
        var maps = await mapRepo.GetBySchedulesAsync(schedules.Select(s => s.No).ToList());
        return (schedules.Count, maps.Count);
    }

    [Fact]
    public async Task 자동배치_Undo_Redo_Undo_반복이_정확히_동작()
    {
        int courseNo = await SetupAndGenerateAsync();

        var afterGenerate = await CountAsync(courseNo);
        Assert.Equal(5, afterGenerate.Schedules); // 월~금 5슬롯
        Assert.Equal(5, afterGenerate.Maps);      // 3+2 차시

        // 1) Undo → 전부 삭제
        var undo1 = await NewShiftService().UndoLastActionAsync(courseNo, Room);
        Assert.True(undo1.Success);
        Assert.Equal((0, 0), await CountAsync(courseNo));

        // 2) Redo → 스냅샷대로 정확히 복원 (스케줄 ID 는 새로 발급됨)
        var redo = await NewShiftService().RedoLastActionAsync(courseNo, Room);
        Assert.True(redo.Success);
        Assert.Equal((5, 5), await CountAsync(courseNo));

        // 3) 다시 Undo → 재발급된 ID 여도 슬롯 기준으로 다시 삭제 (구현 전 회귀 지점)
        var undo2 = await NewShiftService().UndoLastActionAsync(courseNo, Room);
        Assert.True(undo2.Success);
        Assert.Equal((0, 0), await CountAsync(courseNo));
    }

    [Fact]
    public async Task 자동배치_직후_Redo는_불가_Undo후_가능()
    {
        int courseNo = await SetupAndGenerateAsync();

        // 배치 직후에는 되돌릴 게 없어 Redo 불가
        Assert.False(await NewShiftService().CanRedoAsync(courseNo, Room));

        await NewShiftService().UndoLastActionAsync(courseNo, Room);

        // Undo 후에는 Redo 가능
        Assert.True(await NewShiftService().CanRedoAsync(courseNo, Room));
    }
}

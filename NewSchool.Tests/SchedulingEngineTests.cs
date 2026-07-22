using System;
using System.Linq;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.Services;
using NewSchool.Tests.Infrastructure;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// SchedulingEngine 회귀 테스트 —
/// ① 검증 실패(단원 없음) 시 기존 배치를 지우지 않는지(이전에는 검증 전에 삭제해 데이터 유실)
/// ② 슬롯 부족으로 일부만 배치된 단원이 FillWarnings 로 보고되는지
/// ③ 공유 연결(단일 트랜잭션) 경로에서 정상 배치가 동작하는지
/// </summary>
public class SchedulingEngineTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _db;

    // 2026-03-02(월) ~ 2026-03-06(금) — 월~금 1교시 시간표 기준 슬롯 5개
    private static readonly DateTime WeekStart = new(2026, 3, 2);
    private static readonly DateTime WeekEnd = new(2026, 3, 6);

    private const string Room = "1-1";

    public SchedulingEngineTests(SqliteTestFixture db) => _db = db;

    /// <summary>과목 + 월~금 1교시 시간표를 생성하고 CourseId 반환.</summary>
    private async Task<int> SetupCourseAsync()
    {
        using var courseRepo = new CourseRepository(_db.DbPath);
        int courseNo = await courseRepo.CreateAsync(
            TestData.NewCourse(subject: $"배치테스트_{Guid.NewGuid():N}"));

        using var lessonRepo = new LessonRepository(_db.DbPath);
        for (int dow = 1; dow <= 5; dow++)
        {
            await lessonRepo.CreateAsync(new Lesson
            {
                Course = courseNo,
                DayOfWeek = dow,
                Period = 1,
                Room = Room,
            });
        }

        return courseNo;
    }

    /// <summary>실제 호출부(AnnualLessonPlanPage)와 동일하게 schedule/map 리포지토리가 연결을 공유하는 엔진 실행.</summary>
    private async Task<SchedulingResult> RunEngineAsync(int courseNo)
    {
        using var scheduleRepo = new ScheduleRepository(_db.DbPath);
        using var mapRepo = new ScheduleUnitMapRepository(scheduleRepo.GetConnection());
        using var sectionRepo = new CourseSectionRepository(_db.DbPath);
        using var lessonRepo = new LessonRepository(_db.DbPath);
        using var schoolScheduleRepo = new SchoolScheduleRepository(_db.DbPath);

        var engine = new SchedulingEngine(
            scheduleRepo, mapRepo, sectionRepo, lessonRepo, schoolScheduleRepo);

        return await engine.GenerateScheduleAsync(courseNo, Room, WeekStart, WeekEnd);
    }

    [Fact]
    public async Task 단원이_없으면_기존_배치를_지우지_않음()
    {
        int courseNo = await SetupCourseAsync();

        // 기존 배치 1건 (단원 없이 스케줄만 존재하는 상태)
        using (var scheduleRepo = new ScheduleRepository(_db.DbPath))
        {
            await scheduleRepo.CreateAsync(new Schedule
            {
                CourseId = courseNo,
                Room = Room,
                Date = WeekStart,
                Period = 1,
            });
        }

        var result = await RunEngineAsync(courseNo);

        Assert.False(result.Success);
        Assert.Contains("단원", result.Message);

        // 검증 실패 조기 반환 시 기존 배치가 유지되어야 한다
        using var repo = new ScheduleRepository(_db.DbPath);
        var remaining = await repo.GetByCourseAndRoomAsync(courseNo, Room);
        Assert.Single(remaining);
    }

    [Fact]
    public async Task 슬롯_부족_단원은_FillWarnings_로_보고()
    {
        int courseNo = await SetupCourseAsync();

        // 가용 슬롯 5개(월~금 1교시)인데 필요 시수 8차시 → 5차시만 배치되고 경고
        using (var sectionRepo = new CourseSectionRepository(_db.DbPath))
        {
            await sectionRepo.CreateAsync(TestData.NewSection(courseNo, hours: 8));
        }

        var result = await RunEngineAsync(courseNo);

        var warning = Assert.Single(result.FillWarnings);
        Assert.True(warning.IsWarning);
        Assert.Contains("5/8", warning.Reason);
        Assert.Contains("시수 부족", result.Message);

        // 배치 자체는 가용 슬롯 5개 전부에 수행됨
        using var repo = new ScheduleRepository(_db.DbPath);
        var schedules = await repo.GetByCourseAndRoomAsync(courseNo, Room);
        Assert.Equal(5, schedules.Count);
    }

    [Fact]
    public async Task 정상_배치_공유연결_트랜잭션_경로()
    {
        int courseNo = await SetupCourseAsync();

        using (var sectionRepo = new CourseSectionRepository(_db.DbPath))
        {
            await sectionRepo.CreateAsync(TestData.NewSection(courseNo, sectionNo: 1, sectionName: "1절", hours: 2));
            await sectionRepo.CreateAsync(TestData.NewSection(courseNo, sectionNo: 2, sectionName: "2절", hours: 3));
        }

        var result = await RunEngineAsync(courseNo);

        Assert.True(result.Success);
        Assert.Equal(2, result.TotalPlaced);
        Assert.Empty(result.FillWarnings);

        // 5차시가 월~금 순서대로 배치되고 단원 매핑도 함께 커밋됨
        using var scheduleRepo = new ScheduleRepository(_db.DbPath);
        var schedules = await scheduleRepo.GetByCourseAndRoomAsync(courseNo, Room);
        Assert.Equal(5, schedules.Count);

        using var mapRepo = new ScheduleUnitMapRepository(_db.DbPath);
        var maps = await mapRepo.GetBySchedulesAsync(schedules.Select(s => s.No).ToList());
        Assert.Equal(5, maps.Count);
    }
}

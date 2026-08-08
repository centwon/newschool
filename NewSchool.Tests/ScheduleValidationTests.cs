using System;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.Services;
using NewSchool.Tests.Infrastructure;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 배치 검사 회귀 테스트 — 전수 조사 후속(28차 관찰 처리).
///
/// <c>ValidateScheduleAsync</c> 는 <c>Warnings</c> 만 채우고 <c>Errors</c> 를 채우는 곳이
/// 한 군데도 없었다. 그래서 <c>IsValid</c> 는 늘 true 였고, 배치 검사 화면의 ❌ 분기는
/// 한 번도 실행되지 않았다 — 고쳐야 하는 문제와 확인만 하면 되는 문제가 구분되지 않았다.
/// </summary>
public class ScheduleValidationTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _db;

    private static readonly DateTime Day = new(2026, 3, 2); // 월요일
    private const string Room = "1-1";

    public ScheduleValidationTests(SqliteTestFixture db) => _db = db;

    private async Task<ValidationResult> ValidateAsync(int courseNo)
    {
        using var scheduleRepo = new ScheduleRepository(_db.DbPath);
        using var mapRepo = new ScheduleUnitMapRepository(scheduleRepo.GetConnection());
        using var sectionRepo = new CourseSectionRepository(_db.DbPath);
        using var lessonRepo = new LessonRepository(_db.DbPath);
        using var schoolScheduleRepo = new SchoolScheduleRepository(_db.DbPath);

        var engine = new SchedulingEngine(
            scheduleRepo, mapRepo, sectionRepo, lessonRepo, schoolScheduleRepo);

        return await engine.ValidateScheduleAsync(courseNo, Room, TestData.SchoolCode);
    }

    [Fact]
    public async Task 정상_배치는_오류도_경고도_없다()
    {
        using var courseRepo = new CourseRepository(_db.DbPath);
        using var sectionRepo = new CourseSectionRepository(_db.DbPath);
        using var scheduleRepo = new ScheduleRepository(_db.DbPath);
        using var mapRepo = new ScheduleUnitMapRepository(_db.DbPath);

        int courseNo = await courseRepo.CreateAsync(TestData.NewCourse(subject: "정상배치", rooms: Room));
        int sectionNo = await sectionRepo.CreateAsync(TestData.NewSection(courseNo, hours: 1));

        int scheduleNo = await scheduleRepo.CreateAsync(new Schedule
        {
            CourseId = courseNo, Room = Room, Date = Day, Period = 1,
        });
        await mapRepo.CreateAsync(new ScheduleUnitMap
        { ScheduleId = scheduleNo, CourseSectionId = sectionNo, AllocatedHours = 1 });

        var result = await ValidateAsync(courseNo);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
    }

    /// <summary>
    /// 단원을 지워도 배치의 매핑은 남는다(ScheduleUnitMap 은 CourseSection 에 CASCADE 지만
    /// 다른 수업으로 옮겨진 단원이 매핑에 남는 경우가 있다). 이런 배치는 시수만 먹고
    /// 진도에도 안 잡히므로 경고가 아니라 오류다.
    /// </summary>
    [Fact]
    public async Task 다른_수업의_단원이_매핑돼_있으면_오류다()
    {
        using var courseRepo = new CourseRepository(_db.DbPath);
        using var sectionRepo = new CourseSectionRepository(_db.DbPath);
        using var scheduleRepo = new ScheduleRepository(_db.DbPath);
        using var mapRepo = new ScheduleUnitMapRepository(_db.DbPath);

        int courseNo = await courseRepo.CreateAsync(TestData.NewCourse(subject: "본수업", rooms: Room));
        int otherCourseNo = await courseRepo.CreateAsync(TestData.NewCourse(subject: "남의수업", rooms: Room));

        // 본 수업의 단원 1개(정상 배치) + 남의 수업 단원 1개(잘못된 매핑)
        int ownSection = await sectionRepo.CreateAsync(TestData.NewSection(courseNo, hours: 1));
        int foreignSection = await sectionRepo.CreateAsync(TestData.NewSection(otherCourseNo, hours: 1));

        int s1 = await scheduleRepo.CreateAsync(new Schedule
        { CourseId = courseNo, Room = Room, Date = Day, Period = 1 });
        int s2 = await scheduleRepo.CreateAsync(new Schedule
        { CourseId = courseNo, Room = Room, Date = Day, Period = 2 });

        await mapRepo.CreateAsync(new ScheduleUnitMap
        { ScheduleId = s1, CourseSectionId = ownSection, AllocatedHours = 1 });
        await mapRepo.CreateAsync(new ScheduleUnitMap
        { ScheduleId = s2, CourseSectionId = foreignSection, AllocatedHours = 1 });

        var result = await ValidateAsync(courseNo);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("이 수업의 단원이 아닌"));
    }

    /// <summary>
    /// 배치한 뒤 학사일정이 바뀌면(개교기념일 추가 등) 멀쩡하던 배치가 휴일에 걸린다.
    /// 그 날은 수업이 없으므로 배치가 사실상 성립하지 않는다 — 오류로 보고해야 한다.
    /// </summary>
    [Fact]
    public async Task 휴일에_배치된_수업은_오류다()
    {
        using var courseRepo = new CourseRepository(_db.DbPath);
        using var sectionRepo = new CourseSectionRepository(_db.DbPath);
        using var scheduleRepo = new ScheduleRepository(_db.DbPath);
        using var mapRepo = new ScheduleUnitMapRepository(_db.DbPath);
        using var schoolScheduleRepo = new SchoolScheduleRepository(_db.DbPath);

        int courseNo = await courseRepo.CreateAsync(TestData.NewCourse(subject: "휴일배치", rooms: Room));
        int sectionNo = await sectionRepo.CreateAsync(TestData.NewSection(courseNo, hours: 1));

        var holiday = new DateTime(2026, 3, 9);
        int scheduleNo = await scheduleRepo.CreateAsync(new Schedule
        { CourseId = courseNo, Room = Room, Date = holiday, Period = 1 });
        await mapRepo.CreateAsync(new ScheduleUnitMap
        { ScheduleId = scheduleNo, CourseSectionId = sectionNo, AllocatedHours = 1 });

        // 그 날을 휴업일로 만드는 학사일정 추가
        await schoolScheduleRepo.CreateAsync(
            TestData.NewSchedule(holiday, eventName: "개교기념일(휴업일)"));

        var result = await ValidateAsync(courseNo);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("휴일에 배치된"));
    }
}

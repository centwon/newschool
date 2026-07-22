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
/// ScheduleShiftService 당기기(Pull) 회귀 테스트 —
/// 당겨지는 수업이 고정 수업(IsPinned)·기존 수업 슬롯 위로 이동해
/// 이중 배치되던 문제(점유 시딩 누락)를 검증한다.
/// </summary>
public class ScheduleShiftServiceTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _db;

    // 2026-03-02(월) 주간 — 월~금 매일 1교시 수업이 있는 시간표 기준
    private static readonly DateTime Mon = new(2026, 3, 2);
    private static readonly DateTime Tue = new(2026, 3, 3);
    private static readonly DateTime Wed = new(2026, 3, 4);

    private const string Room = "1-1";

    public ScheduleShiftServiceTests(SqliteTestFixture db) => _db = db;

    private ScheduleShiftService NewService() => new(
        new ScheduleRepository(_db.DbPath),
        new ScheduleUnitMapRepository(_db.DbPath),
        new UndoHistoryRepository(_db.DbPath),
        new LessonRepository(_db.DbPath),
        new SchoolScheduleRepository(_db.DbPath));

    /// <summary>과목 + 월~금 1교시 시간표 + 지정 스케줄들을 생성하고 CourseId 를 반환.</summary>
    private async Task<int> SetupCourseAsync(params (DateTime Date, bool Pinned)[] slots)
    {
        using var courseRepo = new CourseRepository(_db.DbPath);
        int courseNo = await courseRepo.CreateAsync(
            TestData.NewCourse(subject: $"당기기테스트_{Guid.NewGuid():N}"));

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

        using var scheduleRepo = new ScheduleRepository(_db.DbPath);
        foreach (var (date, pinned) in slots)
        {
            await scheduleRepo.CreateAsync(new Schedule
            {
                CourseId = courseNo,
                Room = Room,
                Date = date,
                Period = 1,
                IsPinned = pinned,
            });
        }

        return courseNo;
    }

    private async Task<List<Schedule>> GetSchedulesAsync(int courseNo)
    {
        using var repo = new ScheduleRepository(_db.DbPath);
        return await repo.GetByCourseAndRoomAsync(courseNo, Room);
    }

    [Fact]
    public async Task Pull_이전_슬롯이_모두_점유되면_이동하지_않음()
    {
        // 월(일반)·화(고정)·수(일반) — 수요일부터 당기면 화(고정)·월(기존) 모두 점유라 이동 불가
        int courseNo = await SetupCourseAsync((Mon, false), (Tue, true), (Wed, false));

        var result = await NewService().PullSchedulesAsync(
            courseNo, Room, fromDate: Wed, fromPeriod: 1, semesterStart: Mon);

        Assert.True(result.Success);
        Assert.Equal(0, result.ShiftedCount);
        Assert.Equal(1, result.OverflowCount);

        var schedules = await GetSchedulesAsync(courseNo);

        // 이중 배치 없음: 세 수업이 각각 월·화·수에 그대로
        var distinctSlots = schedules.Select(s => (s.Date.Date, s.Period)).Distinct().Count();
        Assert.Equal(3, distinctSlots);
        Assert.Contains(schedules, s => s.Date.Date == Wed && !s.IsPinned);
        Assert.Contains(schedules, s => s.Date.Date == Tue && s.IsPinned);
    }

    [Fact]
    public async Task Pull_고정_수업을_건너뛰고_빈_슬롯으로_당김()
    {
        // 월(빈 슬롯)·화(고정)·수(일반) — 수요일 수업은 고정을 건너뛰고 월요일로 당겨져야 함
        int courseNo = await SetupCourseAsync((Tue, true), (Wed, false));

        var result = await NewService().PullSchedulesAsync(
            courseNo, Room, fromDate: Wed, fromPeriod: 1, semesterStart: Mon);

        Assert.True(result.Success);
        Assert.Equal(1, result.ShiftedCount);

        var schedules = await GetSchedulesAsync(courseNo);

        // 고정 수업은 화요일 그대로, 일반 수업은 월요일로 이동 — 슬롯 겹침 없음
        Assert.Contains(schedules, s => s.Date.Date == Tue && s.IsPinned);
        Assert.Contains(schedules, s => s.Date.Date == Mon && !s.IsPinned);
        var distinctSlots = schedules.Select(s => (s.Date.Date, s.Period)).Distinct().Count();
        Assert.Equal(schedules.Count, distinctSlots);
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NewSchool.Services;
using NewSchool.Tests.Infrastructure;
using Xunit;

namespace NewSchool.Tests;

/// <summary>SchoolScheduleService — 날짜 범위(상한 배타) + NEIS 중복 제외 회귀 테스트 (TEST_PLAN 2단계).</summary>
public class SchoolScheduleServiceTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _db;

    public SchoolScheduleServiceTests(SqliteTestFixture db) => _db = db;

    [Fact]
    public async Task DateRange_상한은_배타_하한은_포함()
    {
        // 회귀: TodayPage 가 [Today, Today+1) 로 '오늘 하루'를 조회하는 계약. 상한 배타여야 한다.
        using var svc = new SchoolScheduleService(_db.DbPath);
        var today = new DateTime(2026, 7, 5);
        await svc.CreateBulkScheduleAsync(new()
        {
            TestData.NewSchedule(today.AddDays(-1), "어제행사"),
            TestData.NewSchedule(today, "오늘행사"),
            TestData.NewSchedule(today.AddDays(1), "내일행사"),
        });

        var (ok, _, list) = await svc.GetSchedulesByDataRangeAsync(
            TestData.SchoolCode, today, today.AddDays(1));

        Assert.True(ok);
        Assert.Single(list);                       // 오늘만 — 상한(내일) 배타
        Assert.Equal("오늘행사", list[0].EVENT_NM);
    }

    [Fact]
    public async Task DateRange_여러날_포함()
    {
        using var svc = new SchoolScheduleService(_db.DbPath);
        var start = new DateTime(2026, 8, 1);
        await svc.CreateBulkScheduleAsync(new()
        {
            TestData.NewSchedule(new DateTime(2026, 8, 1), "8/1"),
            TestData.NewSchedule(new DateTime(2026, 8, 3), "8/3"),
            TestData.NewSchedule(new DateTime(2026, 8, 5), "8/5"),  // 상한 배타로 제외
        });

        var (_, _, list) = await svc.GetSchedulesByDataRangeAsync(
            TestData.SchoolCode, start, new DateTime(2026, 8, 5));

        Assert.Equal(2, list.Count); // 8/1, 8/3 (8/5 제외)
    }

    [Fact]
    public async Task CreateBulk_학교_날짜_행사명_중복은_스킵()
    {
        // 회귀: NEIS 동기화 재실행 시 같은 일정이 중복 생성되지 않아야 함
        using var svc = new SchoolScheduleService(_db.DbPath);
        var date = new DateTime(2026, 9, 10);

        var (_, _, first) = await svc.CreateBulkScheduleAsync(new()
        {
            TestData.NewSchedule(date, "중간고사"),
            TestData.NewSchedule(date, "체육대회"),
        });
        Assert.Equal(2, first);

        // 재실행: 하나는 중복, 하나는 신규
        var (_, _, second) = await svc.CreateBulkScheduleAsync(new()
        {
            TestData.NewSchedule(date, "중간고사"),   // 중복 → 스킵
            TestData.NewSchedule(date, "학부모총회"), // 신규
        });
        Assert.Equal(1, second);

        var (_, _, all) = await svc.GetSchedulesByDataRangeAsync(
            TestData.SchoolCode, date, date.AddDays(1));
        Assert.Equal(3, all.Count); // 중간고사, 체육대회, 학부모총회
    }

    [Fact]
    public async Task CreateBulk_같은배치_내_중복도_스킵()
    {
        using var svc = new SchoolScheduleService(_db.DbPath);
        var date = new DateTime(2026, 10, 1);

        var (_, _, count) = await svc.CreateBulkScheduleAsync(new()
        {
            TestData.NewSchedule(date, "개교기념일"),
            TestData.NewSchedule(date, "개교기념일"), // 배치 내 중복
        });

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task GetBySchoolYear_해당_학년도만()
    {
        using var svc = new SchoolScheduleService(_db.DbPath);
        await svc.CreateBulkScheduleAsync(new()
        {
            TestData.NewSchedule(new DateTime(2030, 3, 2), "30년행사", year: 2030),
            TestData.NewSchedule(new DateTime(2031, 3, 2), "31년행사", year: 2031),
        });

        var (_, _, list) = await svc.GetSchedulesBySchoolYearAsync(TestData.SchoolCode, 2030);
        Assert.Single(list);
        Assert.Equal("30년행사", list[0].EVENT_NM);
    }

    /// <summary>
    /// 자동 받기는 "그 학교·학년도의 NEIS 행이 있는가" 로 판단한다(예전 켬/끔 설정은 한 번 켜지면
    /// 새 학년도를 영영 안 받았다). 손으로 넣은 행은 받은 흔적이 아니고, 지운 NEIS 행(묘비)은 흔적이다.
    /// </summary>
    [Fact]
    public async Task NEIS_받음_판정은_수동행을_세지_않고_지운_NEIS행은_센다()
    {
        using var svc = new SchoolScheduleService(_db.DbPath);
        using var repo = new NewSchool.Repositories.SchoolScheduleRepository(_db.DbPath);

        // 2031: 수동 행만 있으면 아직 안 받은 것
        await svc.CreateBulkScheduleAsync(new()
        {
            TestData.NewSchedule(new DateTime(2031, 4, 1), "수동만", year: 2031, isManual: true),
        });
        Assert.False(await repo.HasNeisScheduleAsync(TestData.SchoolCode, 2031));

        await svc.CreateBulkScheduleAsync(new()
        {
            TestData.NewSchedule(new DateTime(2031, 4, 2), "NEIS행", year: 2031, isManual: false),
        });
        Assert.True(await repo.HasNeisScheduleAsync(TestData.SchoolCode, 2031));

        // 2032: NEIS 행을 지워도 받은 것으로 본다 — 다시 받으면 지운 게 되살아난다
        await svc.CreateBulkScheduleAsync(new()
        {
            TestData.NewSchedule(new DateTime(2032, 5, 1), "지울NEIS", year: 2032, isManual: false),
        });
        var (_, _, rows) = await svc.GetSchedulesBySchoolYearAsync(TestData.SchoolCode, 2032);
        await svc.DeleteBulkScheduleAsync(new List<int> { rows[0].No });

        Assert.True(await repo.HasNeisScheduleAsync(TestData.SchoolCode, 2032));
        Assert.False(await repo.HasNeisScheduleAsync(TestData.SchoolCode, 2033));
    }
}

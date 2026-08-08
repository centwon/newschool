using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NewSchool.Repositories;
using NewSchool.Services;
using NewSchool.Tests.Infrastructure;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 진도 동기화 결과 보고 회귀 테스트 — 전수 조사 34차.
///
/// 보강·건너뛰기·병합은 리포지토리 반환값을 버리고 무조건 <c>AffectedCount++</c> 했고,
/// 페이지도 <c>SyncResult.Success</c> 를 보지 않아 한 건도 저장되지 않아도
/// "N개 처리 완료" 성공 메시지가 떴다. 이제 실제 반영 건수만 센다.
/// </summary>
public class ProgressSyncResultTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _db;

    public ProgressSyncResultTests(SqliteTestFixture db) => _db = db;

    private ProgressSyncService NewService(
        out LessonProgressRepository progressRepo,
        out CourseSectionRepository sectionRepo,
        out ScheduleRepository scheduleRepo,
        out ScheduleUnitMapRepository mapRepo)
    {
        progressRepo = new LessonProgressRepository(_db.DbPath);
        sectionRepo = new CourseSectionRepository(_db.DbPath);
        scheduleRepo = new ScheduleRepository(_db.DbPath);
        mapRepo = new ScheduleUnitMapRepository(_db.DbPath);
        return new ProgressSyncService(progressRepo, sectionRepo, scheduleRepo, mapRepo);
    }

    private async Task<(int CourseNo, List<int> SectionNos)> SeedAsync(string subject, int sections)
    {
        using var courseRepo = new CourseRepository(_db.DbPath);
        using var sectionRepo = new CourseSectionRepository(_db.DbPath);

        int courseNo = await courseRepo.CreateAsync(TestData.NewCourse(subject: subject, rooms: "1-1"));
        var nos = new List<int>();
        for (int i = 1; i <= sections; i++)
            nos.Add(await sectionRepo.CreateAsync(TestData.NewSection(courseNo, sectionNo: i)));

        return (courseNo, nos);
    }

    [Fact]
    public async Task 보강은_시도_건수와_반영_건수를_모두_보고한다()
    {
        var (courseNo, sectionNos) = await SeedAsync("보강집계", 3);
        var svc = NewService(out var p, out var s, out var sc, out var m);
        using (p) using (s) using (sc) using (m)
        {
            var result = await svc.AddMakeupLessonAsync(courseNo, "1-1", sectionNos, DateTime.Today);

            Assert.True(result.Success);
            Assert.Equal(3, result.RequestedCount);
            Assert.Equal(3, result.AffectedCount);
        }
    }

    [Fact]
    public async Task 건너뛰기도_시도_건수와_반영_건수를_모두_보고한다()
    {
        var (courseNo, sectionNos) = await SeedAsync("건너뛰기집계", 2);
        var svc = NewService(out var p, out var s, out var sc, out var m);
        using (p) using (s) using (sc) using (m)
        {
            var result = await svc.SkipSectionsAsync(courseNo, "1-1", sectionNos, "학교 행사");

            Assert.True(result.Success);
            Assert.Equal(2, result.RequestedCount);
            Assert.Equal(2, result.AffectedCount);
        }
    }

    [Fact]
    public async Task 병합은_단원이_하나뿐이면_실패로_보고한다()
    {
        var (courseNo, sectionNos) = await SeedAsync("병합집계", 1);
        var svc = NewService(out var p, out var s, out var sc, out var m);
        using (p) using (s) using (sc) using (m)
        {
            var result = await svc.MergeSectionsAsync(courseNo, "1-1", sectionNos, DateTime.Today);

            Assert.False(result.Success);
            Assert.Equal(0, result.AffectedCount);
        }
    }

    [Fact]
    public async Task 없는_단원_보강은_성공으로_보고하지_않는다()
    {
        // 부모 단원이 없으면 진도 행을 만들 수 없다 → 한 건도 반영되면 안 된다.
        var svc = NewService(out var p, out var s, out var sc, out var m);
        using (p) using (s) using (sc) using (m)
        {
            var result = await svc.AddMakeupLessonAsync(
                999_999, "9-9", [888_888], DateTime.Today);

            Assert.False(result.Success);
            Assert.Equal(0, result.AffectedCount);
        }
    }
}

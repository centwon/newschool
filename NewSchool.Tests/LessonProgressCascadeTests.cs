using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.Tests.Infrastructure;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 진도 기록 외래키 회귀 테스트 — 전수 조사 34차.
///
/// <c>LessonProgress</c> 의 두 외래키에 <c>ON DELETE</c> 절이 없어 NO ACTION 이었다.
/// 그래서 진도 기록이 한 건이라도 있으면 수업·단원·시간표 일정 삭제가
/// <c>FOREIGN KEY constraint failed</c> 로 영구 실패했다(진도 매트릭스를 한 번이라도
/// 쓴 수업은 지울 수 없었다). 이제 단원은 CASCADE, 일정은 SET NULL 이다.
/// </summary>
public class LessonProgressCascadeTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _db;

    public LessonProgressCascadeTests(SqliteTestFixture db) => _db = db;

    [Fact]
    public async Task 진도_기록이_있어도_수업이_삭제된다()
    {
        using var courseRepo = new CourseRepository(_db.DbPath);
        using var sectionRepo = new CourseSectionRepository(_db.DbPath);
        using var progressRepo = new LessonProgressRepository(_db.DbPath);

        int courseNo = await courseRepo.CreateAsync(TestData.NewCourse(subject: "수업삭제", rooms: "1-1"));
        int sectionNo = await sectionRepo.CreateAsync(TestData.NewSection(courseNo));
        Assert.True(await progressRepo.MarkAsCompletedAsync(sectionNo, "1-1"));

        Assert.True(await courseRepo.DeleteAsync(courseNo));
        Assert.Null(await courseRepo.GetByIdAsync(courseNo));

        // 단원과 함께 진도도 사라져야 한다(CASCADE)
        Assert.Null(await progressRepo.GetBySectionAndRoomAsync(sectionNo, "1-1"));
    }

    [Fact]
    public async Task 진도_기록이_있어도_단원이_삭제된다()
    {
        using var courseRepo = new CourseRepository(_db.DbPath);
        using var sectionRepo = new CourseSectionRepository(_db.DbPath);
        using var progressRepo = new LessonProgressRepository(_db.DbPath);

        int courseNo = await courseRepo.CreateAsync(TestData.NewCourse(subject: "단원삭제", rooms: "1-1"));
        int sectionNo = await sectionRepo.CreateAsync(TestData.NewSection(courseNo));
        Assert.True(await progressRepo.MarkAsCompletedAsync(sectionNo, "1-1"));

        Assert.True(await sectionRepo.DeleteAsync(sectionNo));
        Assert.Null(await progressRepo.GetBySectionAndRoomAsync(sectionNo, "1-1"));
    }

    [Fact]
    public async Task 일정을_지워도_진도는_남고_참조만_풀린다()
    {
        using var courseRepo = new CourseRepository(_db.DbPath);
        using var sectionRepo = new CourseSectionRepository(_db.DbPath);
        using var progressRepo = new LessonProgressRepository(_db.DbPath);
        using var scheduleRepo = new ScheduleRepository(_db.DbPath);

        int courseNo = await courseRepo.CreateAsync(TestData.NewCourse(subject: "일정삭제", rooms: "1-1"));
        int sectionNo = await sectionRepo.CreateAsync(TestData.NewSection(courseNo));

        int scheduleNo = await scheduleRepo.CreateAsync(new Schedule
        {
            CourseId = courseNo,
            Room = "1-1",
            Date = DateTime.Today,
            Period = 1,
        });
        Assert.True(scheduleNo > 0);
        Assert.True(await progressRepo.MarkAsCompletedAsync(sectionNo, "1-1", DateTime.Today, scheduleNo));

        Assert.True(await scheduleRepo.DeleteAsync(scheduleNo));

        // 진도 자체는 살아 있어야 한다 — 일정은 부가 정보일 뿐이다
        var progress = await progressRepo.GetBySectionAndRoomAsync(sectionNo, "1-1");
        Assert.NotNull(progress);
        Assert.True(progress!.IsCompleted);
        Assert.Null(progress.ScheduleId);
    }

}

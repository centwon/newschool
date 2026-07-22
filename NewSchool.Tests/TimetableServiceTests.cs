using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.Services;
using NewSchool.Tests.Infrastructure;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// TimetableService 원자성 테스트 — Course + Lesson 을 TimetableUnitOfWork 로 묶어
/// 원자적으로 생성/롤백하는지 검증(고아 과목 방지).
/// </summary>
public class TimetableServiceTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _db;

    public TimetableServiceTests(SqliteTestFixture db) => _db = db;

    [Fact]
    public async Task CreateCourseWithSchedule_성공시_과목과_시간표_함께_생성()
    {
        var svc = new TimetableService(_db.DbPath);
        var course = TestData.NewCourse(subject: "통합생성과목", grade: 1);
        var lessons = new List<Lesson>
        {
            new() { DayOfWeek = 1, Period = 1, Room = "1-1" },
            new() { DayOfWeek = 3, Period = 2, Room = "1-1" },
        };

        int courseNo = await svc.CreateCourseWithScheduleAsync(course, lessons);
        Assert.True(courseNo > 0);

        using var courseRepo = new CourseRepository(_db.DbPath);
        Assert.NotNull(await courseRepo.GetByIdAsync(courseNo));

        using var lessonRepo = new LessonRepository(_db.DbPath);
        var created = await lessonRepo.GetByCourseAsync(courseNo);
        Assert.Equal(2, created.Count);
    }

    [Fact]
    public async Task UnitOfWork_롤백시_과목_생성_취소()
    {
        // CreateCourseWithScheduleAsync 가 의존하는 원자성 보장을 직접 검증 —
        // 과목 생성 뒤 트랜잭션 내에서 실패하면 과목도 남지 않아야 한다.
        int courseNo = 0;

        using (var uow = new TimetableUnitOfWork(_db.DbPath))
        {
            await Assert.ThrowsAnyAsync<Exception>(() =>
                uow.ExecuteInTransactionAsync<int>(async () =>
                {
                    courseNo = await uow.Courses.CreateAsync(
                        TestData.NewCourse(subject: "롤백과목"));
                    Assert.True(courseNo > 0);
                    throw new InvalidOperationException("강제 실패 → 롤백 유도");
                }));
        }

        // 별도 연결로 확인: 롤백되어 과목이 남지 않아야 함
        using var courseRepo = new CourseRepository(_db.DbPath);
        Assert.Null(await courseRepo.GetByIdAsync(courseNo));
    }
}

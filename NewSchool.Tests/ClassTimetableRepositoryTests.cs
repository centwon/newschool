using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Repositories;
using NewSchool.Tests.Infrastructure;
using Xunit;

namespace NewSchool.Tests;

/// <summary>ClassTimetable 리포지토리 CRUD·UNIQUE 제약 테스트 (TEST_PLAN 1단계).</summary>
public class ClassTimetableRepositoryTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _db;

    public ClassTimetableRepositoryTests(SqliteTestFixture db) => _db = db;

    [Fact]
    public async Task CRUD_왕복()
    {
        using var repo = new ClassTimetableRepository(_db.DbPath);

        int no = await repo.CreateAsync(TestData.NewTimetableSlot(grade: 2, classNum: 1, dayOfWeek: 1, period: 1, subject: "수학"));
        Assert.True(no > 0);

        var loaded = await repo.GetByIdAsync(no);
        Assert.Equal("수학", loaded!.SubjectName);

        loaded.SubjectName = "영어";
        Assert.True(await repo.UpdateAsync(loaded));
        Assert.Equal("영어", (await repo.GetByIdAsync(no))!.SubjectName);

        Assert.True(await repo.DeleteAsync(no));
        Assert.Null(await repo.GetByIdAsync(no));
    }

    [Fact]
    public async Task 같은_슬롯_중복삽입은_UNIQUE제약_위반()
    {
        using var repo = new ClassTimetableRepository(_db.DbPath);
        await repo.CreateAsync(TestData.NewTimetableSlot(grade: 2, classNum: 2, dayOfWeek: 3, period: 4));

        // (SchoolCode, Year, Semester, Grade, Class, DayOfWeek, Period) UNIQUE
        await Assert.ThrowsAsync<SqliteException>(() =>
            repo.CreateAsync(TestData.NewTimetableSlot(grade: 2, classNum: 2, dayOfWeek: 3, period: 4, subject: "다른과목")));
    }

    [Fact]
    public async Task IsDuplicate_점유슬롯_true_빈슬롯_false()
    {
        using var repo = new ClassTimetableRepository(_db.DbPath);
        await repo.CreateAsync(TestData.NewTimetableSlot(grade: 2, classNum: 3, dayOfWeek: 2, period: 5));

        Assert.True(await repo.IsDuplicateAsync(TestData.SchoolCode, TestData.Year, 1, 2, 3, 2, 5));
        Assert.False(await repo.IsDuplicateAsync(TestData.SchoolCode, TestData.Year, 1, 2, 3, 2, 6));
    }

    [Fact]
    public async Task 배치삽입_후_반별조회_요일교시_정렬()
    {
        using var repo = new ClassTimetableRepository(_db.DbPath);
        var slots = new List<Models.ClassTimetable>
        {
            TestData.NewTimetableSlot(grade: 3, classNum: 1, dayOfWeek: 2, period: 2, subject: "화2"),
            TestData.NewTimetableSlot(grade: 3, classNum: 1, dayOfWeek: 1, period: 1, subject: "월1"),
            TestData.NewTimetableSlot(grade: 3, classNum: 1, dayOfWeek: 1, period: 2, subject: "월2"),
        };
        int created = await repo.CreateBatchAsync(slots);
        Assert.Equal(3, created);

        var list = await repo.GetByClassAsync(TestData.SchoolCode, TestData.Year, 1, 3, 1);
        Assert.Equal(3, list.Count);
    }

    [Fact]
    public async Task DeleteByClass_해당반만_삭제하고_건수반환()
    {
        using var repo = new ClassTimetableRepository(_db.DbPath);
        await repo.CreateAsync(TestData.NewTimetableSlot(grade: 4, classNum: 1, dayOfWeek: 1, period: 1));
        await repo.CreateAsync(TestData.NewTimetableSlot(grade: 4, classNum: 1, dayOfWeek: 1, period: 2));
        await repo.CreateAsync(TestData.NewTimetableSlot(grade: 4, classNum: 2, dayOfWeek: 1, period: 1));

        int deleted = await repo.DeleteByClassAsync(TestData.SchoolCode, TestData.Year, 1, 4, 1);
        Assert.Equal(2, deleted);

        var remaining = await repo.GetByClassAsync(TestData.SchoolCode, TestData.Year, 1, 4, 2);
        Assert.Single(remaining);
    }
}

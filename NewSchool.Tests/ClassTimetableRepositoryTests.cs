using System.Collections.Generic;
using System.Linq;
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

    /// <summary>
    /// 넣은 순서와 상관없이 <b>요일·교시 순</b>으로 돌려준다.
    /// (일괄 삽입 메서드는 호출부가 없어 지웠으므로 한 칸씩 넣는다 — 2026-09-04.)
    /// </summary>
    [Fact]
    public async Task 반별조회는_요일교시_순으로_돌려준다()
    {
        using var repo = new ClassTimetableRepository(_db.DbPath);
        await repo.CreateAsync(TestData.NewTimetableSlot(grade: 3, classNum: 1, dayOfWeek: 2, period: 2, subject: "화2"));
        await repo.CreateAsync(TestData.NewTimetableSlot(grade: 3, classNum: 1, dayOfWeek: 1, period: 1, subject: "월1"));
        await repo.CreateAsync(TestData.NewTimetableSlot(grade: 3, classNum: 1, dayOfWeek: 1, period: 2, subject: "월2"));

        var list = await repo.GetByClassAsync(TestData.SchoolCode, TestData.Year, 1, 3, 1);

        Assert.Equal(new[] { "월1", "월2", "화2" }, list.Select(t => t.SubjectName));
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

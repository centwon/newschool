using System;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.Tests.Infrastructure;
using Xunit;

namespace NewSchool.Tests;

/// <summary>Enrollment 리포지토리 경계 테스트 (TEST_PLAN 1단계) — 스모크 외 추가 케이스.</summary>
public class EnrollmentRepositoryTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _db;

    public EnrollmentRepositoryTests(SqliteTestFixture db) => _db = db;

    [Fact]
    public async Task DeleteAsync_논리삭제_후_GetById는_null()
    {
        using var repo = new EnrollmentRepository(_db.DbPath);
        var id = await _db.NewStudentInDbAsync("삭제대상");
        int no = await repo.CreateAsync(TestData.NewEnrollment(id, "삭제대상", classNum: 9, number: 1));

        Assert.True(await repo.DeleteAsync(no));

        // IsDeleted=1 이 되어 기본 조회에서 제외
        Assert.Null(await repo.GetByIdAsync(no));
    }

    [Fact]
    public async Task GetById_존재하지않는_No는_null()
    {
        using var repo = new EnrollmentRepository(_db.DbPath);
        Assert.Null(await repo.GetByIdAsync(999_999));
    }

    [Fact]
    public async Task TeacherID_왕복_보존()
    {
        // 회귀: MapEnrollment 가 TeacherID 를 매핑하지 않아 조회→저장 시 담임이 유실되던 버그 (2026-07-10)
        using var repo = new EnrollmentRepository(_db.DbPath);
        var id = await _db.NewStudentInDbAsync("담임확인");
        int no = await repo.CreateAsync(TestData.NewEnrollment(id, "담임확인", classNum: 9, number: 2));

        var loaded = await repo.GetByIdAsync(no);
        Assert.Equal(TestData.TeacherId, loaded!.TeacherID);

        // 로드한 객체 그대로 재저장해도 FK 위반 없이 성공해야 한다
        Assert.True(await repo.UpdateAsync(loaded));
        var again = await repo.GetByIdAsync(no);
        Assert.Equal(TestData.TeacherId, again!.TeacherID);
    }

    [Fact]
    public async Task ApplyChangeAsync_졸업처리시_변동과_일자와_재적여부가_함께_바뀐다()
    {
        using var repo = new EnrollmentRepository(_db.DbPath);
        var id = await _db.NewStudentInDbAsync("졸업생");
        int no = await repo.CreateAsync(TestData.NewEnrollment(id, "졸업생", classNum: 9, number: 3));

        var gradDate = new DateTime(TestData.Year + 1, 2, 28);
        Assert.True(await repo.ApplyChangeAsync(no, EnrollmentChange.Graduated, gradDate));

        var loaded = await repo.GetByIdAsync(no);
        Assert.Equal(EnrollmentChange.Graduated, loaded!.ChangeType);
        Assert.StartsWith($"{TestData.Year + 1}-02", loaded.ChangeDate);

        // 졸업생은 명단에서 빠진다 — 이것이 어긋나면 명렬표에 계속 남는다.
        Assert.False(loaded.IsActive);
    }

    [Fact]
    public async Task 전출한_학생은_명단_조회에서_빠지고_학생관리에서만_보인다()
    {
        using var repo = new EnrollmentRepository(_db.DbPath);
        int year = TestData.Year + 5;

        var staying = await _db.NewStudentInDbAsync("남는학생");
        var leaving = await _db.NewStudentInDbAsync("전출학생");
        await repo.CreateAsync(TestData.NewEnrollment(staying, "남는학생", year: year, classNum: 1, number: 1));
        int leftNo = await repo.CreateAsync(TestData.NewEnrollment(leaving, "전출학생", year: year, classNum: 1, number: 2));

        await repo.ApplyChangeAsync(leftNo, EnrollmentChange.TransferredOut, new DateTime(year, 5, 10));

        var roster = await repo.GetEnrollmentsAsync(TestData.SchoolCode, year, 1, 1);
        var everyone = await repo.GetEnrollmentsAsync(TestData.SchoolCode, year, 1, 1, includeInactive: true);

        Assert.Single(roster);
        Assert.Equal(staying, roster[0].StudentID);
        Assert.Equal(2, everyone.Count);
    }
}

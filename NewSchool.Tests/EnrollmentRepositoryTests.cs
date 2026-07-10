using System;
using System.Threading.Tasks;
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
    public async Task UpdateStatusAsync_졸업처리시_상태와_졸업일_기록()
    {
        using var repo = new EnrollmentRepository(_db.DbPath);
        var id = await _db.NewStudentInDbAsync("졸업생");
        int no = await repo.CreateAsync(TestData.NewEnrollment(id, "졸업생", classNum: 9, number: 3));

        var gradDate = new DateTime(TestData.Year + 1, 2, 28);
        Assert.True(await repo.UpdateStatusAsync(no, "졸업", gradDate));

        var loaded = await repo.GetByIdAsync(no);
        Assert.Equal("졸업", loaded!.Status);
        Assert.StartsWith($"{TestData.Year + 1}-02", loaded.GraduationDate);
    }

    [Fact]
    public async Task GetBySchoolAndYear_semester0은_전체학기()
    {
        using var repo = new EnrollmentRepository(_db.DbPath);
        // 다른 테스트와 격리되도록 별도 학년도 사용
        int year = TestData.Year + 5;
        await repo.CreateAsync(TestData.NewEnrollment(await _db.NewStudentInDbAsync("일학기"), "일학기", year: year, semester: 1));
        await repo.CreateAsync(TestData.NewEnrollment(await _db.NewStudentInDbAsync("이학기"), "이학기", year: year, semester: 2));

        var all = await repo.GetBySchoolAndYearAsync(TestData.SchoolCode, year, semester: 0);
        var s1 = await repo.GetBySchoolAndYearAsync(TestData.SchoolCode, year, semester: 1);

        Assert.Equal(2, all.Count);
        Assert.Single(s1);
    }
}

using System;
using System.Linq;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.Services;
using NewSchool.Tests.Infrastructure;
using Xunit;

namespace NewSchool.Tests;

/// <summary>EnrollmentService 서비스 로직 회귀 테스트 (TEST_PLAN 2단계).</summary>
public class EnrollmentServiceTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _db;

    public EnrollmentServiceTests(SqliteTestFixture db) => _db = db;

    [Fact]
    public async Task 한_학년도에_학적은_한_줄뿐이다()
    {
        // 학기 컬럼을 없애면서 UNIQUE(StudentID, SchoolCode, Year) 가 이것을 강제한다.
        // 예전에는 1·2학기 행이 둘 다 있는 학생이 명부에 두 번 나왔다(2026-07-15).
        int year = TestData.Year + 12;
        using var repo = new EnrollmentRepository(_db.DbPath);
        var sid = await _db.NewStudentInDbAsync();
        await repo.CreateAsync(TestData.NewEnrollment(sid, year: year, grade: 2, classNum: 3, number: 7));

        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await repo.CreateAsync(TestData.NewEnrollment(sid, year: year, grade: 2, classNum: 3, number: 7)));

        using var svc = new EnrollmentService(_db.DbPath);
        var roster = await svc.GetClassRosterAsync(TestData.SchoolCode, year, 2, 3);

        Assert.Single(roster.Where(e => e.StudentID == sid));
    }

    [Fact]
    public async Task GetEnrollmentsAsync_이름은_Student_에서_가져온다()
    {
        // 이름은 Enrollment 의 컬럼이 아니라 EnrollmentFull 뷰가 JOIN 으로 읽는 값이다.
        // 예전에는 학적 쪽에 사본이 있어, 학생 이름을 고치면 두 곳이 어긋날 수 있었다.
        int year = TestData.Year + 13;
        using var repo = new EnrollmentRepository(_db.DbPath);

        var sid = await _db.NewStudentInDbAsync("이름확인");
        await repo.CreateAsync(TestData.NewEnrollment(sid, "엉뚱한이름", year: year, grade: 2, classNum: 3, number: 7));

        using var svc = new EnrollmentService(_db.DbPath);
        var roster = await svc.GetEnrollmentsAsync(TestData.SchoolCode, year, grade: 2, classNum: 3);

        // 학적에 넣으려 한 "엉뚱한이름" 은 저장되지 않고, Student 의 이름이 나온다.
        Assert.Single(roster);
        Assert.Equal("이름확인", roster[0].Name);
    }

    [Fact]
    public async Task GetEnrollmentsAsync_학년도가_다르면_각각_남는다()
    {
        // 중복 제거는 (학생, 학년도) 단위여야 한다 — 학생 ID 로만 묶으면 과거 학년도 학적이 사라진다.
        int year = TestData.Year + 14;
        using var repo = new EnrollmentRepository(_db.DbPath);
        var sid = await _db.NewStudentInDbAsync("두학년도");
        await repo.CreateAsync(TestData.NewEnrollment(sid, year: year, grade: 1, classNum: 1, number: 4));
        await repo.CreateAsync(TestData.NewEnrollment(sid, year: year + 1, grade: 2, classNum: 1, number: 4));

        using var svc = new EnrollmentService(_db.DbPath);
        var all = await svc.GetEnrollmentsAsync(TestData.SchoolCode);

        Assert.Equal(2, all.Count(e => e.StudentID == sid));
    }
}
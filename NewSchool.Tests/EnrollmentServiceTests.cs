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
    public async Task GetClassRosterAsync_학기별_학적이_둘다_있어도_학생은_한번만()
    {
        // 회귀: 학기 필터가 없어 1·2학기 학적 행이 둘 다 있는 학생이 명부에 두 번 나오던 문제 (2026-07-15)
        int year = TestData.Year + 12;
        using var repo = new EnrollmentRepository(_db.DbPath);
        var sid = await _db.NewStudentInDbAsync();
        await repo.CreateAsync(TestData.NewEnrollment(sid, year: year, semester: 1, grade: 2, classNum: 3, number: 7));
        await repo.CreateAsync(TestData.NewEnrollment(sid, year: year, semester: 2, grade: 2, classNum: 3, number: 7));

        using var svc = new EnrollmentService(_db.DbPath);
        var roster = await svc.GetClassRosterAsync(TestData.SchoolCode, year, 2, 3);

        var mine = roster.Where(e => e.StudentID == sid).ToList();
        Assert.Single(mine);
        Assert.Equal(2, mine[0].Semester); // 최신 학기 행이 남는다
    }

    [Fact]
    public async Task GetEnrollmentsAsync_학기로_거르지_않고_학생당_한_건만_준다()
    {
        // 명부는 학년 단위다(2026-07-30 확정). 학기로 거르던 시절에는 1학기에만 학적이 있는
        // 학생이 2학기 조회에서 통째로 사라졌다 — 2학기 학적을 만드는 경로가 앱에 없으므로
        // 사실상 모든 학생이 사라졌다.
        int year = TestData.Year + 13;
        using var repo = new EnrollmentRepository(_db.DbPath);

        // 1학기 학적만 있는 학생
        var onlyFirst = await _db.NewStudentInDbAsync("1학기만");
        await repo.CreateAsync(TestData.NewEnrollment(onlyFirst, year: year, semester: 1, grade: 2, classNum: 3, number: 7));

        // 두 학기 학적이 다 있는 학생
        var both = await _db.NewStudentInDbAsync("두학기");
        await repo.CreateAsync(TestData.NewEnrollment(both, year: year, semester: 1, grade: 2, classNum: 3, number: 8));
        await repo.CreateAsync(TestData.NewEnrollment(both, year: year, semester: 2, grade: 2, classNum: 3, number: 8));

        using var svc = new EnrollmentService(_db.DbPath);
        var roster = await svc.GetEnrollmentsAsync(TestData.SchoolCode, year, grade: 2, classNum: 3);

        // 1학기 학적만 있어도 명부에 남는다 (핵심 회귀)
        Assert.Single(roster, e => e.StudentID == onlyFirst);

        // 두 학기 행이 다 있어도 한 번만 — 최신 학기 행으로
        Assert.Single(roster, e => e.StudentID == both);
        Assert.Equal(2, roster.First(e => e.StudentID == both).Semester);
    }

    [Fact]
    public async Task GetEnrollmentsAsync_학년도가_다르면_각각_남는다()
    {
        // 중복 제거는 (학생, 학년도) 단위여야 한다 — 학생 ID 로만 묶으면 과거 학년도 학적이 사라진다.
        int year = TestData.Year + 14;
        using var repo = new EnrollmentRepository(_db.DbPath);
        var sid = await _db.NewStudentInDbAsync("두학년도");
        await repo.CreateAsync(TestData.NewEnrollment(sid, year: year, semester: 1, grade: 1, classNum: 1, number: 4));
        await repo.CreateAsync(TestData.NewEnrollment(sid, year: year + 1, semester: 1, grade: 2, classNum: 1, number: 4));

        using var svc = new EnrollmentService(_db.DbPath);
        var all = await svc.GetEnrollmentsAsync(TestData.SchoolCode);

        Assert.Equal(2, all.Count(e => e.StudentID == sid));
    }
}
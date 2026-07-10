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

    // 2학기 재학생 1명을 학년 grade 에 배치하고 그 학생의 학적 반환
    private async Task<Enrollment> SeedGraduateCandidateAsync(int grade, int year, string status = "재학")
    {
        using var repo = new EnrollmentRepository(_db.DbPath);
        var sid = await _db.NewStudentInDbAsync();
        var e = TestData.NewEnrollment(sid, year: year, semester: 2, grade: grade, number: 1, status: status);
        e.No = await repo.CreateAsync(e);
        return e;
    }

    [Theory]
    [InlineData(2025, "2026-02-28")]  // 평년
    [InlineData(2027, "2028-02-29")]  // 윤년 (2028)
    public async Task GraduateAsync_졸업일은_학년도_다음해_2월_말일(int year, string expectedDate)
    {
        // 회귀: 졸업일이 new DateTime(year,2,28) 로 계산돼 2025학년도→2025-02 로 찍히던 버그 + 윤년 (2026-07-10)
        var e = await SeedGraduateCandidateAsync(grade: 3, year: year);
        using var svc = new EnrollmentService(_db.DbPath);

        int count = await svc.GraduateAsync(TestData.SchoolCode, year, 3);
        Assert.Equal(1, count);

        using var repo = new EnrollmentRepository(_db.DbPath);
        var updated = await repo.GetByIdAsync(e.No);
        Assert.Equal(EnrollmentStatus.Graduated, updated!.Status);
        Assert.Equal(expectedDate, updated.GraduationDate);
    }

    [Fact]
    public async Task GraduateAsync_재학생만_대상_휴학은_제외()
    {
        int year = TestData.Year + 10; // 격리용 연도
        await SeedGraduateCandidateAsync(grade: 3, year: year, status: "재학");
        await SeedGraduateCandidateAsync(grade: 3, year: year, status: "휴학");

        using var svc = new EnrollmentService(_db.DbPath);
        int count = await svc.GraduateAsync(TestData.SchoolCode, year, 3);

        Assert.Equal(1, count); // 휴학생은 졸업 처리되지 않음
    }

    [Fact]
    public async Task GraduateAsync_대상없으면_0반환()
    {
        using var svc = new EnrollmentService(_db.DbPath);
        int count = await svc.GraduateAsync(TestData.SchoolCode, TestData.Year + 20, 3);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task GraduateAsync_다른학년은_영향없음()
    {
        int year = TestData.Year + 11;
        var g3 = await SeedGraduateCandidateAsync(grade: 3, year: year);
        var g2 = await SeedGraduateCandidateAsync(grade: 2, year: year);

        using var svc = new EnrollmentService(_db.DbPath);
        await svc.GraduateAsync(TestData.SchoolCode, year, 3);

        using var repo = new EnrollmentRepository(_db.DbPath);
        Assert.Equal(EnrollmentStatus.Graduated, (await repo.GetByIdAsync(g3.No))!.Status);
        Assert.Equal(EnrollmentStatus.Enrolled, (await repo.GetByIdAsync(g2.No))!.Status); // 2학년 유지
    }
}

using System.Threading.Tasks;
using NewSchool.Repositories;
using NewSchool.Services;
using NewSchool.Tests.Infrastructure;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// StudentService — Student.Name 갱신 시 denormalized Enrollment.Name 자동 동기화 회귀 테스트.
/// (학생 관리 화면에서 이름 저장 시 정본 Student 를 거쳐 Enrollment 까지 반영되어야 함, 2026-07-05)
/// </summary>
public class StudentServiceTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _db;

    public StudentServiceTests(SqliteTestFixture db) => _db = db;

    // 같은 StudentID 로 Student + Enrollment 를 만들고 StudentID 반환
    private async Task<string> SeedStudentWithEnrollmentAsync(string name)
    {
        var sid = await _db.NewStudentInDbAsync(name);
        using var repo = new EnrollmentRepository(_db.DbPath);
        await repo.CreateAsync(TestData.NewEnrollment(sid, name, number: 5));
        return sid;
    }

    [Fact]
    public async Task UpdateBasicInfo_이름변경이_Enrollment로_동기화()
    {
        var sid = await SeedStudentWithEnrollmentAsync("원래이름");

        using var svc = new StudentService(_db.DbPath);
        var student = await svc.GetBasicInfoAsync(sid);
        student!.Name = "바뀐이름";
        Assert.True(await svc.UpdateBasicInfoAsync(student));

        // Student 정본 갱신
        Assert.Equal("바뀐이름", (await svc.GetBasicInfoAsync(sid))!.Name);

        // Enrollment 복제본도 동기화되어야 한다 (핵심 회귀)
        using var repo = new EnrollmentRepository(_db.DbPath);
        var enr = await repo.GetCurrentByStudentIdAsync(sid);
        Assert.Equal("바뀐이름", enr!.Name);
    }

    [Fact]
    public async Task UpdateBasicInfo_성별변경도_Enrollment로_동기화()
    {
        var sid = await SeedStudentWithEnrollmentAsync("성별학생");

        using var svc = new StudentService(_db.DbPath);
        var student = await svc.GetBasicInfoAsync(sid);
        student!.Sex = "여";
        await svc.UpdateBasicInfoAsync(student);

        using var repo = new EnrollmentRepository(_db.DbPath);
        Assert.Equal("여", (await repo.GetCurrentByStudentIdAsync(sid))!.Sex);
    }

    [Fact]
    public async Task UpdateBasicInfo_학적_없는_학생도_예외없이_성공()
    {
        // Enrollment 가 없어도 Student 갱신 자체는 성공해야 한다 (동기화 대상 0건)
        var sid = await _db.NewStudentInDbAsync("학적없음");

        using var svc = new StudentService(_db.DbPath);
        var student = await svc.GetBasicInfoAsync(sid);
        student!.Name = "이름만변경";
        Assert.True(await svc.UpdateBasicInfoAsync(student));
        Assert.Equal("이름만변경", (await svc.GetBasicInfoAsync(sid))!.Name);
    }
}

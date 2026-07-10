using System.Threading.Tasks;
using NewSchool.Repositories;
using NewSchool.Tests.Infrastructure;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// TEST_PLAN 0단계 스모크 — 임시 DB 픽스처 위에서 Enrollment CRUD 왕복이 통과하면
/// 인프라(픽스처·시드·dbPath 주입)가 동작하는 것으로 판정한다.
/// </summary>
public class EnrollmentRepositorySmokeTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _db;

    public EnrollmentRepositorySmokeTests(SqliteTestFixture db) => _db = db;

    [Fact]
    public async Task Enrollment_생성_조회_수정_왕복()
    {
        using var repo = new EnrollmentRepository(_db.DbPath);
        string studentId = await _db.NewStudentInDbAsync("김철수");
        var enrollment = TestData.NewEnrollment(studentId, name: "김철수", number: 7);

        int no = await repo.CreateAsync(enrollment);
        Assert.True(no > 0);

        var loaded = await repo.GetByIdAsync(no);
        Assert.NotNull(loaded);
        Assert.Equal("김철수", loaded!.Name);
        Assert.Equal(7, loaded.Number);
        Assert.Equal(TestData.SchoolCode, loaded.SchoolCode);
        Assert.Equal("재학", loaded.Status);

        loaded.Number = 8;
        loaded.Memo = "자리 변경";
        Assert.True(await repo.UpdateAsync(loaded));

        var updated = await repo.GetByIdAsync(no);
        Assert.NotNull(updated);
        Assert.Equal(8, updated!.Number);
        Assert.Equal("자리 변경", updated.Memo);
    }

    [Fact]
    public async Task GetByClass_해당_반만_조회()
    {
        using var repo = new EnrollmentRepository(_db.DbPath);
        // 5반에 2명, 6반에 1명 (다른 테스트와 격리되도록 반 번호를 크게)
        await repo.CreateAsync(TestData.NewEnrollment(await _db.NewStudentInDbAsync("가"), "가", classNum: 5, number: 1));
        await repo.CreateAsync(TestData.NewEnrollment(await _db.NewStudentInDbAsync("나"), "나", classNum: 5, number: 2));
        await repo.CreateAsync(TestData.NewEnrollment(await _db.NewStudentInDbAsync("다"), "다", classNum: 6, number: 1));

        var class5 = await repo.GetByClassAsync(TestData.SchoolCode, TestData.Year, 1, 5);

        Assert.Equal(2, class5.Count);
        Assert.All(class5, e => Assert.Equal(5, e.Class));
    }

    [Fact]
    public async Task 배치조회_GetCurrentByStudentIds_빈목록과_다건()
    {
        using var repo = new EnrollmentRepository(_db.DbPath);
        var id1 = await _db.NewStudentInDbAsync("일");
        var id2 = await _db.NewStudentInDbAsync("이");
        await repo.CreateAsync(TestData.NewEnrollment(id1, "일", classNum: 7, number: 1));
        await repo.CreateAsync(TestData.NewEnrollment(id2, "이", classNum: 7, number: 2));

        var empty = await repo.GetCurrentByStudentIdsAsync(new());
        Assert.Empty(empty);

        var two = await repo.GetCurrentByStudentIdsAsync(new() { id1, id2, "없는ID" });
        Assert.Equal(2, two.Count);
    }
}

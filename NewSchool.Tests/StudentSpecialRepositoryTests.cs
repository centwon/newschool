using System.Threading.Tasks;
using NewSchool.Repositories;
using NewSchool.Tests.Infrastructure;
using Xunit;

namespace NewSchool.Tests;

/// <summary>StudentSpecial 리포지토리 CRUD·경계 테스트 (TEST_PLAN 1단계).</summary>
public class StudentSpecialRepositoryTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _db;

    public StudentSpecialRepositoryTests(SqliteTestFixture db) => _db = db;

    [Fact]
    public async Task CRUD_왕복()
    {
        using var repo = new StudentSpecialRepository(_db.DbPath);
        var id = await _db.NewStudentInDbAsync("세특학생");

        int no = await repo.CreateAsync(TestData.NewSpecial(id, title: "자율활동 특기", content: "리더십 발휘"));
        Assert.True(no > 0);

        var loaded = await repo.GetByIdAsync(no);
        Assert.NotNull(loaded);
        Assert.Equal("자율활동 특기", loaded!.Title);
        Assert.Equal("리더십 발휘", loaded.Content);

        loaded.Content = "수정된 내용";
        Assert.True(await repo.UpdateAsync(loaded));
        Assert.Equal("수정된 내용", (await repo.GetByIdAsync(no))!.Content);

        Assert.True(await repo.DeleteAsync(no));
        Assert.Null(await repo.GetByIdAsync(no));
    }

    [Fact]
    public async Task IsFinalized는_DB의_IsActive_반전으로_왕복()
    {
        // 계약: DB 컬럼 IsActive 를 C# IsFinalized 로 반전 매핑 (마감=IsActive 0)
        using var repo = new StudentSpecialRepository(_db.DbPath);
        var id = await _db.NewStudentInDbAsync("마감학생");

        int draftNo = await repo.CreateAsync(TestData.NewSpecial(id, isFinalized: false));
        int finalNo = await repo.CreateAsync(TestData.NewSpecial(id, type: "진로활동", isFinalized: true));

        Assert.False((await repo.GetByIdAsync(draftNo))!.IsFinalized);
        Assert.True((await repo.GetByIdAsync(finalNo))!.IsFinalized);

        // 마감 상태 토글
        Assert.True(await repo.UpdateFinalizedStatusAsync(draftNo, true));
        Assert.True((await repo.GetByIdAsync(draftNo))!.IsFinalized);
    }

    [Fact]
    public async Task GetByStudent_해당연도만()
    {
        using var repo = new StudentSpecialRepository(_db.DbPath);
        var id = await _db.NewStudentInDbAsync("연도세특");
        await repo.CreateAsync(TestData.NewSpecial(id, year: TestData.Year));
        await repo.CreateAsync(TestData.NewSpecial(id, year: TestData.Year + 1));

        var list = await repo.GetByStudentAsync(id, TestData.Year);
        Assert.Single(list);
    }

    [Fact]
    public async Task 배치조회_학생별_그룹핑과_빈목록()
    {
        using var repo = new StudentSpecialRepository(_db.DbPath);
        var id1 = await _db.NewStudentInDbAsync("세특일");
        var id2 = await _db.NewStudentInDbAsync("세특이");
        await repo.CreateAsync(TestData.NewSpecial(id1, type: "자율활동"));
        await repo.CreateAsync(TestData.NewSpecial(id1, type: "진로활동"));
        await repo.CreateAsync(TestData.NewSpecial(id2));

        var empty = await repo.GetByStudentIdsAsync([], TestData.Year);
        Assert.Empty(empty);

        // 계약: 요청한 모든 ID 에 키가 존재하고, 기록 없는 학생은 빈 리스트
        var grouped = await repo.GetByStudentIdsAsync([id1, id2, "없는ID"], TestData.Year);
        Assert.Equal(2, grouped[id1].Count);
        Assert.Single(grouped[id2]);
        Assert.Empty(grouped["없는ID"]);
    }

    [Fact]
    public async Task GetByType_유형과_연도로_필터()
    {
        using var repo = new StudentSpecialRepository(_db.DbPath);
        var id = await _db.NewStudentInDbAsync("유형세특");
        await repo.CreateAsync(TestData.NewSpecial(id, type: "동아리활동", title: "동아리"));
        await repo.CreateAsync(TestData.NewSpecial(id, type: "자율활동", title: "자율"));

        var club = await repo.GetByTypeAsync("동아리활동", TestData.Year);
        Assert.All(club, s => Assert.Equal("동아리활동", s.Type));
        Assert.Contains(club, s => s.Title == "동아리");
    }
}

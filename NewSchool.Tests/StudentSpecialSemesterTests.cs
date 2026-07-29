using System.Threading.Tasks;
using NewSchool.Helpers;
using NewSchool.Repositories;
using NewSchool.Tests.Infrastructure;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 학생부 학기(<c>StudentSpecial.Semester</c>) 규칙.
///
/// 교과 세부능력 및 특기사항(교과활동)만 학기별이고, 개인별세특을 포함한 나머지는 학년 단위(0)다.
/// 학기를 <c>CourseNo → Course.Semester</c> 로 유도하지 않고 직접 저장하는 이유는,
/// <c>CourseNo</c> 가 <c>ON DELETE SET NULL</c> 이라 교과목을 지우면 학기를 알 수 없게 되기 때문이다.
/// </summary>
public class StudentSpecialSemesterTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _db;

    public StudentSpecialSemesterTests(SqliteTestFixture db) => _db = db;

    #region 영역별 학기 적용 규칙 (정의표)

    [Fact]
    public void 교과활동만_학기별()
    {
        Assert.True(NeisHelper.IsSemesterScoped("교과활동"));
    }

    /// <summary>개인별세특은 교과 영역이지만 학년 단위다(사용자 확인, 2026-07-30).</summary>
    [Theory]
    [InlineData("개인별세특")]
    [InlineData("자율활동")]
    [InlineData("동아리활동")]
    [InlineData("봉사활동")]
    [InlineData("진로활동")]
    [InlineData("종합의견")]
    public void 나머지_영역은_학년단위(string type)
    {
        Assert.False(NeisHelper.IsSemesterScoped(type));
    }

    [Fact]
    public void 모르는_영역은_안전하게_학년단위()
    {
        Assert.False(NeisHelper.IsSemesterScoped("존재하지않는영역"));
    }

    #endregion

    #region DB 왕복

    [Fact]
    public async Task 학기가_저장되고_다시_읽힌다()
    {
        using var repo = new StudentSpecialRepository(_db.DbPath);
        var id = await _db.NewStudentInDbAsync("학기왕복");

        int no = await repo.CreateAsync(
            TestData.NewSpecial(id, type: "교과활동", title: "국어", semester: 2));

        var loaded = await repo.GetByIdAsync(no);
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded!.Semester);
    }

    [Fact]
    public async Task 학년단위_기록은_0으로_저장된다()
    {
        using var repo = new StudentSpecialRepository(_db.DbPath);
        var id = await _db.NewStudentInDbAsync("연간기록");

        int no = await repo.CreateAsync(TestData.NewSpecial(id, type: "진로활동"));

        var loaded = await repo.GetByIdAsync(no);
        Assert.Equal(0, loaded!.Semester);
    }

    [Fact]
    public async Task 학기_수정이_반영된다()
    {
        using var repo = new StudentSpecialRepository(_db.DbPath);
        var id = await _db.NewStudentInDbAsync("학기수정");

        int no = await repo.CreateAsync(
            TestData.NewSpecial(id, type: "교과활동", semester: 1));

        var loaded = await repo.GetByIdAsync(no);
        loaded!.Semester = 2;
        Assert.True(await repo.UpdateAsync(loaded));

        var reloaded = await repo.GetByIdAsync(no);
        Assert.Equal(2, reloaded!.Semester);
    }

    /// <summary>
    /// 같은 학생·같은 과목명이라도 학기가 다르면 별개 기록으로 공존해야 한다.
    /// (예전에는 과목명으로 매칭해 2학기 입력이 1학기 기록을 덮어썼다.)
    /// </summary>
    [Fact]
    public async Task 같은_과목이라도_학기가_다르면_별개_기록()
    {
        using var repo = new StudentSpecialRepository(_db.DbPath);
        var id = await _db.NewStudentInDbAsync("학기분리");

        int first = await repo.CreateAsync(
            TestData.NewSpecial(id, type: "교과활동", title: "국어", content: "1학기 내용", semester: 1));
        int second = await repo.CreateAsync(
            TestData.NewSpecial(id, type: "교과활동", title: "국어", content: "2학기 내용", semester: 2));

        Assert.NotEqual(first, second);

        var a = await repo.GetByIdAsync(first);
        var b = await repo.GetByIdAsync(second);
        Assert.Equal("1학기 내용", a!.Content);
        Assert.Equal("2학기 내용", b!.Content);
        Assert.Equal(1, a.Semester);
        Assert.Equal(2, b.Semester);
    }

    #endregion
}

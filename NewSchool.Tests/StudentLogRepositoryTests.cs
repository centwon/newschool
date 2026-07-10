using System;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.Tests.Infrastructure;
using Xunit;

namespace NewSchool.Tests;

/// <summary>StudentLog 리포지토리 CRUD·경계 테스트 (TEST_PLAN 1단계).</summary>
public class StudentLogRepositoryTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _db;

    public StudentLogRepositoryTests(SqliteTestFixture db) => _db = db;

    [Fact]
    public async Task CRUD_왕복()
    {
        using var repo = new StudentLogRepository(_db.DbPath);
        var id = await _db.NewStudentInDbAsync("기록학생");

        int no = await repo.CreateAsync(TestData.NewStudentLog(id, log: "첫 기록"));
        Assert.True(no > 0);

        var loaded = await repo.GetByIdAsync(no);
        Assert.NotNull(loaded);
        Assert.Equal("첫 기록", loaded!.Log);
        Assert.Equal(LogCategory.기타, loaded.Category);

        loaded.Log = "수정된 기록";
        loaded.Topic = "주제";
        Assert.True(await repo.UpdateAsync(loaded));
        var updated = await repo.GetByIdAsync(no);
        Assert.Equal("수정된 기록", updated!.Log);
        Assert.Equal("주제", updated.Topic);

        Assert.True(await repo.DeleteAsync(no));
        Assert.Null(await repo.GetByIdAsync(no));
    }

    [Fact]
    public async Task CourseNo_0은_NULL로_저장되어_FK위반_없음()
    {
        // Course 행이 없어도 CourseNo=0(미지정) 로그는 저장돼야 한다 (0→NULL 변환 계약)
        using var repo = new StudentLogRepository(_db.DbPath);
        var id = await _db.NewStudentInDbAsync("과목없음");

        var log = TestData.NewStudentLog(id);
        log.CourseNo = 0;
        int no = await repo.CreateAsync(log);

        var loaded = await repo.GetByIdAsync(no);
        Assert.Equal(0, loaded!.CourseNo); // NULL → 0 으로 왕복
    }

    [Fact]
    public async Task GetByStudent_semester0은_전체학기()
    {
        using var repo = new StudentLogRepository(_db.DbPath);
        var id = await _db.NewStudentInDbAsync("학기필터");
        await repo.CreateAsync(TestData.NewStudentLog(id, semester: 1, log: "1학기"));
        await repo.CreateAsync(TestData.NewStudentLog(id, semester: 2, log: "2학기"));

        var all = await repo.GetByStudentAsync(id, TestData.Year, semester: 0);
        var s1 = await repo.GetByStudentAsync(id, TestData.Year, semester: 1);
        var s2 = await repo.GetByStudentAsync(id, TestData.Year, semester: 2);

        Assert.Equal(2, all.Count);
        Assert.Single(s1);
        Assert.Equal("1학기", s1[0].Log);
        Assert.Single(s2);
    }

    [Fact]
    public async Task GetByStudent_다른학년도는_제외()
    {
        using var repo = new StudentLogRepository(_db.DbPath);
        var id = await _db.NewStudentInDbAsync("연도필터");
        await repo.CreateAsync(TestData.NewStudentLog(id, year: TestData.Year));
        await repo.CreateAsync(TestData.NewStudentLog(id, year: TestData.Year + 1));

        var thisYear = await repo.GetByStudentAsync(id, TestData.Year);
        Assert.Single(thisYear);
    }

    [Fact]
    public async Task 배치조회_학생별_그룹핑과_빈목록()
    {
        using var repo = new StudentLogRepository(_db.DbPath);
        var id1 = await _db.NewStudentInDbAsync("배치일");
        var id2 = await _db.NewStudentInDbAsync("배치이");
        await repo.CreateAsync(TestData.NewStudentLog(id1, log: "일-1"));
        await repo.CreateAsync(TestData.NewStudentLog(id1, log: "일-2"));
        await repo.CreateAsync(TestData.NewStudentLog(id2, log: "이-1"));

        var empty = await repo.GetByStudentIdsAsync([], TestData.Year);
        Assert.Empty(empty);

        // 계약: 요청한 모든 ID 에 키가 존재하고, 기록 없는 학생은 빈 리스트
        var grouped = await repo.GetByStudentIdsAsync([id1, id2, "없는ID"], TestData.Year);
        Assert.Equal(3, grouped.Count);
        Assert.Equal(2, grouped[id1].Count);
        Assert.Single(grouped[id2]);
        Assert.Empty(grouped["없는ID"]);
    }

    [Fact]
    public async Task 배치조회_semester필터_적용()
    {
        using var repo = new StudentLogRepository(_db.DbPath);
        var id = await _db.NewStudentInDbAsync("배치학기");
        await repo.CreateAsync(TestData.NewStudentLog(id, semester: 1));
        await repo.CreateAsync(TestData.NewStudentLog(id, semester: 2));

        var s2Only = await repo.GetByStudentIdsAsync([id], TestData.Year, semester: 2);
        Assert.Single(s2Only[id]);
        Assert.Equal(2, s2Only[id][0].Semester);
    }

    [Fact]
    public async Task 카테고리별_조회()
    {
        using var repo = new StudentLogRepository(_db.DbPath);
        var id = await _db.NewStudentInDbAsync("카테고리");
        await repo.CreateAsync(TestData.NewStudentLog(id, category: LogCategory.자율활동));
        await repo.CreateAsync(TestData.NewStudentLog(id, category: LogCategory.진로활동));

        var career = await repo.GetByCategoryAsync(id, TestData.Year, 1, LogCategory.진로활동);
        Assert.Single(career);
        Assert.Equal(LogCategory.진로활동, career[0].Category);
    }
}

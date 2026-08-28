using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NewSchool.Repositories;
using NewSchool.Tests.Infrastructure;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 배치 학적 조회(GetCurrentByStudentIdsAsync)가 단건 조회(GetCurrentByStudentIdAsync)와
/// 완전히 같은 결과를 주는지 검증한다.
///
/// 회귀 배경(2026-07-22, 20차 전수 조사): StudentLogViewModel 이 기록 1건마다 학적·기본정보를
/// 재조회하던 N+1 을 배치 조회로 교체했다. 이때 "현재 학적" 판정 규칙(Year DESC, Semester DESC
/// 중 첫 행)이 두 쿼리에서 어긋나면 명렬 표시 학년·반이 조용히 틀어지므로 등가성을 고정한다.
/// </summary>
public class EnrollmentBatchLookupTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _db;

    public EnrollmentBatchLookupTests(SqliteTestFixture db) => _db = db;

    [Fact]
    public async Task 배치조회는_단건조회와_동일한_현재학적을_반환()
    {
        using var repo = new EnrollmentRepository(_db.DbPath);

        // 학생 3명, 각자 학적 1건
        var ids = new List<string>();
        for (int i = 0; i < 3; i++)
        {
            var id = await _db.NewStudentInDbAsync($"배치{i}");
            await repo.CreateAsync(
                TestData.NewEnrollment(id, $"배치{i}", classNum: 3, number: i + 1));
            ids.Add(id);
        }

        var batch = await repo.GetCurrentByStudentIdsAsync(ids);
        var batchById = batch.ToDictionary(e => e.StudentID);

        Assert.Equal(ids.Count, batch.Count);

        foreach (var id in ids)
        {
            var single = await repo.GetCurrentByStudentIdAsync(id);
            Assert.NotNull(single);
            Assert.Equal(single!.No, batchById[id].No);
            Assert.Equal(single.Grade, batchById[id].Grade);
            Assert.Equal(single.Class, batchById[id].Class);
            Assert.Equal(single.Number, batchById[id].Number);
        }
    }

    [Fact]
    public async Task 학적이_여러_학년도면_최신_학년도가_선택되고_학생당_1건만_나온다()
    {
        using var repo = new EnrollmentRepository(_db.DbPath);
        var id = await _db.NewStudentInDbAsync("다년도");

        // 일부러 오래된 것부터 넣어 삽입 순서가 결과에 영향을 주지 않음을 확인.
        // 학기 행은 만들지 않는다 — UNIQUE(StudentID, SchoolCode, Year) 라 학년도당 한 줄이다.
        await repo.CreateAsync(TestData.NewEnrollment(
            id, "다년도", year: TestData.Year - 1, grade: 1, classNum: 1, number: 1));
        await repo.CreateAsync(TestData.NewEnrollment(
            id, "다년도", year: TestData.Year, grade: 2, classNum: 5, number: 9));

        var single = await repo.GetCurrentByStudentIdAsync(id);
        var batch = await repo.GetCurrentByStudentIdsAsync(new List<string> { id });

        // 학생당 정확히 1건
        Assert.Single(batch);

        // 최신(Year DESC) = 올해 행
        Assert.Equal(TestData.Year, single!.Year);
        Assert.Equal(5, single.Class);

        // 그리고 배치도 같은 행을 골라야 한다
        Assert.Equal(single.No, batch[0].No);
        Assert.Equal(single.Year, batch[0].Year);
        Assert.Equal(single.Class, batch[0].Class);
    }

    [Fact]
    public async Task 존재하지_않는_ID가_섞여도_있는_학생만_반환()
    {
        using var repo = new EnrollmentRepository(_db.DbPath);
        var id = await _db.NewStudentInDbAsync("혼합");
        await repo.CreateAsync(TestData.NewEnrollment(id, "혼합", classNum: 7, number: 1));

        var batch = await repo.GetCurrentByStudentIdsAsync(
            new List<string> { id, "9999999999999999", "" });

        Assert.Single(batch);
        Assert.Equal(id, batch[0].StudentID);
    }

    [Fact]
    public async Task 빈_목록은_빈_결과()
    {
        using var repo = new EnrollmentRepository(_db.DbPath);
        Assert.Empty(await repo.GetCurrentByStudentIdsAsync(new List<string>()));
    }
}

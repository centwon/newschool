using System.Threading.Tasks;
using NewSchool.Repositories;
using NewSchool.Tests.Infrastructure;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 트랜잭션 원자성 테스트 (TEST_PLAN 1단계) — 학생 추가(AddStudentsPage)가 쓰는
/// BeginTransaction/Commit/Rollback 패턴이 실제로 원자적인지 검증.
/// </summary>
public class TransactionTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _db;

    public TransactionTests(SqliteTestFixture db) => _db = db;

    [Fact]
    public async Task Rollback시_학생_저장취소()
    {
        using var repo = new StudentRepository(_db.DbPath);
        var student = TestData.NewStudent(name: "롤백학생");

        repo.BeginTransaction();
        await repo.CreateAsync(student);
        repo.Rollback();

        Assert.Null(await repo.GetByIdAsync(student.StudentID));
    }

    [Fact]
    public async Task Commit시_학생_저장확정()
    {
        using var repo = new StudentRepository(_db.DbPath);
        var student = TestData.NewStudent(name: "커밋학생");

        repo.BeginTransaction();
        await repo.CreateAsync(student);
        repo.Commit();

        var loaded = await repo.GetByIdAsync(student.StudentID);
        Assert.NotNull(loaded);
        Assert.Equal("커밋학생", loaded!.Name);
    }
}

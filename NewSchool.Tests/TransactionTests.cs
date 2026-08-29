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

    // ── 트랜잭션을 겹쳐 열지 못하게 한다 (2026-08-30) ──────────────────
    //
    // 예전에는 BeginTransaction 이 기존 트랜잭션을 Transaction?.Dispose() 로 버렸다.
    // SqliteTransaction 은 커밋되지 않은 채 Dispose 되면 **조용히 롤백된다** — 오류도
    // 경고도 없이 앞선 작업이 사라지므로, 무엇이 왜 저장되지 않았는지 알 방법이 없었다.

    /// <summary>
    /// 겹쳐 열면 <b>던진다</b>. 이게 이 고침의 전부다 — 조용한 손실을 시끄러운 실패로 바꾼다.
    /// </summary>
    [Fact]
    public void 트랜잭션을_겹쳐_열면_던진다()
    {
        using var repo = new StudentRepository(_db.DbPath);

        repo.BeginTransaction();
        try
        {
            Assert.Throws<System.InvalidOperationException>(() => repo.BeginTransaction());
        }
        finally
        {
            repo.Rollback();
        }
    }

    /// <summary>
    /// ⚠ 그 반대편 — <b>정상 흐름은 그대로여야 한다.</b> 열고 닫기를 되풀이하는 것은
    /// 겹쳐 여는 것이 아니다. 가드가 여기까지 막으면 일괄 저장 화면들이 통째로 멎는다.
    /// </summary>
    [Fact]
    public async Task 열고_닫기를_되풀이하는_것은_막지_않는다()
    {
        using var repo = new StudentRepository(_db.DbPath);

        repo.BeginTransaction();
        await repo.CreateAsync(TestData.NewStudent(name: "첫번째"));
        repo.Commit();

        repo.BeginTransaction();          // Commit 뒤에는 다시 열 수 있어야 한다
        await repo.CreateAsync(TestData.NewStudent(name: "두번째"));
        repo.Rollback();

        repo.BeginTransaction();          // Rollback 뒤에도 마찬가지
        await repo.CreateAsync(TestData.NewStudent(name: "세번째"));
        repo.Commit();
    }

    /// <summary>
    /// 공유 트랜잭션(<c>SetTransaction</c>)을 받은 리포지토리가 자기 트랜잭션을 열려 하면
    /// 막는다 — <b>이것이 막으려던 바로 그 조합이다.</b>
    ///
    /// <para>예전에는 여기서 공유 트랜잭션이 버려졌다. 준 쪽(UnitOfWork·다른 리포지토리)은
    /// 이미 Dispose 된 트랜잭션을 계속 들고 있게 되고, 그때까지의 작업은 사라진다.</para>
    /// </summary>
    [Fact]
    public void 공유_트랜잭션을_받은_리포지토리는_겹쳐_열_수_없다()
    {
        using var owner = new StudentRepository(_db.DbPath);
        using var sharer = new EnrollmentRepository(owner.GetConnection());

        owner.BeginTransaction();
        try
        {
            sharer.SetTransaction(owner.GetTransaction());

            Assert.Throws<System.InvalidOperationException>(() => sharer.BeginTransaction());
        }
        finally
        {
            sharer.SetTransaction(null);
            owner.Rollback();
        }
    }
}

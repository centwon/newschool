using System;
using System.Threading.Tasks;
using NewSchool.Board.Repositories;
using NewSchool.Tests.Infrastructure;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 게시판 <see cref="UnitOfWork"/> 의 트랜잭션 겹침 고정 (2026-08-30).
///
/// <para><c>SqliteTransaction</c> 은 커밋되지 않은 채 Dispose 되면 <b>조용히 롤백된다</b>.
/// 예전 <c>BeginTransaction</c> 은 이미 열려 있는 트랜잭션을 그냥 <c>Dispose()</c> 했으므로,
/// 겹쳐 열면 앞선 작업이 오류도 경고도 없이 사라졌다. 여기는 Post·Comment·PostFile 이
/// 한 연결·한 트랜잭션을 나눠 갖는 자리라 셋의 작업이 함께 버려진다.</para>
///
/// <para><c>Repositories/BaseRepository</c> 는 같은 결함을 이미 고쳐 두고 그 주석에
/// "게시판 쪽이 특히 걸리는 이유는 UnitOfWork 다" 라고 적어 두었는데, 정작 이 파일이
/// 그대로였다 — 그래서 여기에 못을 박는다.</para>
/// </summary>
public class BoardUnitOfWorkTests : IClassFixture<BoardTestFixture>
{
    private readonly BoardTestFixture _db;

    public BoardUnitOfWorkTests(BoardTestFixture db) => _db = db;

    [Fact]
    public void 트랜잭션을_겹쳐_열면_조용히_삼키지_않고_던진다()
    {
        using var uow = new UnitOfWork(_db.DbPath);
        uow.BeginTransaction();

        var ex = Assert.Throws<InvalidOperationException>(() => uow.BeginTransaction());
        Assert.Contains("조용히 롤백", ex.Message);

        uow.Rollback();   // 뒷정리
    }

    [Fact]
    public void 커밋한_뒤에는_다시_열_수_있다()
    {
        using var uow = new UnitOfWork(_db.DbPath);

        uow.BeginTransaction();
        uow.Commit();

        uow.BeginTransaction();   // 던지지 않아야 한다
        uow.Rollback();
    }

    [Fact]
    public void 롤백한_뒤에도_다시_열_수_있다()
    {
        using var uow = new UnitOfWork(_db.DbPath);

        uow.BeginTransaction();
        uow.Rollback();

        uow.BeginTransaction();
        uow.Commit();
    }

    /// <summary>
    /// 실제 쓰기 경로(BoardService 는 메서드마다 UnitOfWork 를 새로 만든다)는 겹치지 않는다 —
    /// 위 가드를 넣어도 평소 저장·삭제가 멀쩡히 돌아야 한다.
    /// </summary>
    [Fact]
    public async Task 연속_저장은_그대로_돈다()
    {
        using var svc = new NewSchool.Board.Services.BoardService(_db.DbPath);

        int first = await svc.SavePostAsync(TestData.NewPost(title: "겹침확인1"));
        int second = await svc.SavePostAsync(TestData.NewPost(title: "겹침확인2"));

        Assert.True(first > 0);
        Assert.True(second > first);
        Assert.Equal("겹침확인2", (await svc.GetPostAsync(second, false))!.Title);
    }
}

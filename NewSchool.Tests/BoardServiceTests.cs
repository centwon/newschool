using System.Threading.Tasks;
using NewSchool.Board.Services;
using NewSchool.Tests.Infrastructure;
using Xunit;

namespace NewSchool.Tests;

/// <summary>BoardService — 메모 저장/완료(확인)/조회 필터 회귀 테스트 (TEST_PLAN 2단계, board.db).</summary>
public class BoardServiceTests : IClassFixture<BoardTestFixture>
{
    private readonly BoardTestFixture _db;

    public BoardServiceTests(BoardTestFixture db) => _db = db;

    [Fact]
    public async Task SavePost_저장_후_조회()
    {
        using var svc = new BoardService(_db.DbPath);
        int no = await svc.SavePostAsync(TestData.NewPost(category: "수업", title: "서비스저장"));
        Assert.True(no > 0);

        var loaded = await svc.GetPostAsync(no, incrementReadCount: false);
        Assert.Equal("서비스저장", loaded!.Title);
    }

    [Fact]
    public async Task UpdatePostIsCompleted_확인상태_토글()
    {
        // 회귀: 아카이브/게시글 보기의 메모 '확인(완료)' 표시 기반 (2026-07-05)
        using var svc = new BoardService(_db.DbPath);
        int no = await svc.SavePostAsync(TestData.NewPost(title: "확인메모"));

        Assert.True(await svc.UpdatePostIsCompletedAsync(no, true));
        Assert.True((await svc.GetPostAsync(no, false))!.IsCompleted);

        Assert.True(await svc.UpdatePostIsCompletedAsync(no, false));
        Assert.False((await svc.GetPostAsync(no, false))!.IsCompleted);
    }

    [Fact]
    public async Task GetMemos_includeCompleted_false는_완료제외()
    {
        using var svc = new BoardService(_db.DbPath);
        await svc.SavePostAsync(TestData.NewPost(category: "메모필터", subject: "메모", title: "활성메모"));
        int done = await svc.SavePostAsync(TestData.NewPost(category: "메모필터", subject: "메모", title: "완료메모"));
        await svc.UpdatePostIsCompletedAsync(done, true);

        var all = await svc.GetMemosAsync(category: "메모필터", subject: "메모", includeCompleted: true);
        var active = await svc.GetMemosAsync(category: "메모필터", subject: "메모", includeCompleted: false);

        Assert.Equal(2, all.Count);
        Assert.Single(active);
        Assert.Equal("활성메모", active[0].Title);
    }

    [Fact]
    public async Task GetMemos_카테고리_필터()
    {
        using var svc = new BoardService(_db.DbPath);
        await svc.SavePostAsync(TestData.NewPost(category: "수업", subject: "메모", title: "수업메모"));
        await svc.SavePostAsync(TestData.NewPost(category: "학급", subject: "메모", title: "학급메모"));

        var lesson = await svc.GetMemosAsync(category: "수업", subject: "메모");
        Assert.Single(lesson);
        Assert.Equal("수업메모", lesson[0].Title);
    }

    [Fact]
    public async Task GetMemos_카테고리_빈값은_전체()
    {
        using var svc = new BoardService(_db.DbPath);
        await svc.SavePostAsync(TestData.NewPost(category: "업무", subject: "전체메모", title: "A"));
        await svc.SavePostAsync(TestData.NewPost(category: "개인", subject: "전체메모", title: "B"));

        var all = await svc.GetMemosAsync(category: "", subject: "전체메모");
        Assert.Equal(2, all.Count);
    }

    /// <summary>
    /// 회귀: 예전에는 조회수를 올리기 <b>전에</b> 읽은 객체를 그대로 돌려줘서,
    /// 글을 열었을 때 화면의 조회수가 DB 보다 항상 1 작았다(자기 열람이 반영 안 된 숫자).
    /// </summary>
    [Fact]
    public async Task GetPost_조회수증가_반환값에도_반영된다()
    {
        using var svc = new BoardService(_db.DbPath);
        int no = await svc.SavePostAsync(TestData.NewPost(title: "조회수"));

        // 첫 열람: 0 -> 1 이 되고, 돌려받은 객체도 1 이어야 한다
        var first = await svc.GetPostAsync(no, incrementReadCount: true);
        Assert.Equal(1, first!.ReadCount);

        // 증가 없이 다시 읽어도 DB 값은 1 — 화면과 DB 가 일치한다
        Assert.Equal(1, (await svc.GetPostAsync(no, incrementReadCount: false))!.ReadCount);

        // 두 번째 열람: 1 -> 2
        Assert.Equal(2, (await svc.GetPostAsync(no, incrementReadCount: true))!.ReadCount);
        Assert.Equal(2, (await svc.GetPostAsync(no, incrementReadCount: false))!.ReadCount);
    }

    [Fact]
    public async Task GetPost_증가안함이면_조회수_그대로()
    {
        using var svc = new BoardService(_db.DbPath);
        int no = await svc.SavePostAsync(TestData.NewPost(title: "조회수유지"));

        Assert.Equal(0, (await svc.GetPostAsync(no, incrementReadCount: false))!.ReadCount);
        Assert.Equal(0, (await svc.GetPostAsync(no, incrementReadCount: false))!.ReadCount);
    }
}

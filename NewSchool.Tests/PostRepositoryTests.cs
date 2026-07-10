using System.Threading.Tasks;
using NewSchool.Board.Repositories;
using NewSchool.Tests.Infrastructure;
using Xunit;

namespace NewSchool.Tests;

/// <summary>게시판 Post 리포지토리 테스트 (TEST_PLAN 1단계, board.db).</summary>
public class PostRepositoryTests : IClassFixture<BoardTestFixture>
{
    private readonly BoardTestFixture _db;

    public PostRepositoryTests(BoardTestFixture db) => _db = db;

    [Fact]
    public async Task CRUD_왕복()
    {
        using var repo = new PostRepository(_db.DbPath);

        int no = await repo.CreateAsync(TestData.NewPost(category: "수업", title: "첫 글"));
        Assert.True(no > 0);

        var loaded = await repo.GetByIdAsync(no);
        Assert.NotNull(loaded);
        Assert.Equal("첫 글", loaded!.Title);
        Assert.Equal("수업", loaded.Category);
        Assert.False(loaded.IsCompleted);

        loaded.Title = "수정된 글";
        Assert.True(await repo.UpdateAsync(loaded));
        Assert.Equal("수정된 글", (await repo.GetByIdAsync(no))!.Title);

        Assert.True(await repo.DeleteAsync(no));
        Assert.Null(await repo.GetByIdAsync(no));
    }

    [Fact]
    public async Task IsCompleted_갱신_왕복()
    {
        // 회귀: 메모 완료(확인) 처리 — 아카이브 표시의 기반 계약 (2026-07-05)
        using var repo = new PostRepository(_db.DbPath);
        int no = await repo.CreateAsync(TestData.NewPost(title: "완료할 메모"));

        Assert.True(await repo.UpdateIsCompletedAsync(no, true));
        Assert.True((await repo.GetByIdAsync(no))!.IsCompleted);

        Assert.True(await repo.UpdateIsCompletedAsync(no, false));
        Assert.False((await repo.GetByIdAsync(no))!.IsCompleted);
    }

    [Fact]
    public async Task GetList_includeCompleted_false는_완료글_제외()
    {
        using var repo = new PostRepository(_db.DbPath);
        int active = await repo.CreateAsync(TestData.NewPost(category: "필터", title: "활성"));
        int done = await repo.CreateAsync(TestData.NewPost(category: "필터", title: "완료"));
        await repo.UpdateIsCompletedAsync(done, true);

        var all = await repo.GetListAsync(category: "필터", includeCompleted: true);
        var activeOnly = await repo.GetListAsync(category: "필터", includeCompleted: false);

        Assert.Equal(2, all.Count);
        Assert.Single(activeOnly);
        Assert.Equal(active, activeOnly[0].No);
    }

    [Fact]
    public async Task 조회수_증가()
    {
        using var repo = new PostRepository(_db.DbPath);
        int no = await repo.CreateAsync(TestData.NewPost(title: "조회수"));

        await repo.IncrementReadCountAsync(no);
        await repo.IncrementReadCountAsync(no);

        Assert.Equal(2, (await repo.GetByIdAsync(no))!.ReadCount);
    }

    [Fact]
    public async Task 카테고리_서브젝트_필터()
    {
        using var repo = new PostRepository(_db.DbPath);
        await repo.CreateAsync(TestData.NewPost(category: "학급", subject: "메모", title: "학급메모"));
        await repo.CreateAsync(TestData.NewPost(category: "학급", subject: "공지", title: "학급공지"));
        await repo.CreateAsync(TestData.NewPost(category: "업무", subject: "메모", title: "업무메모"));

        var classMemo = await repo.GetListAsync(category: "학급", subject: "메모");
        Assert.Single(classMemo);
        Assert.Equal("학급메모", classMemo[0].Title);
    }
}

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

    /// <summary>글쓰기·수정 화면의 "중요 글" 체크는 글 저장(UpdateAsync)에 실려 들어간다.</summary>
    private static async Task<bool> PinAsync(PostRepository repo, int no)
    {
        var post = await repo.GetByIdAsync(no);
        post!.IsPinned = true;
        return await repo.UpdateAsync(post);
    }

    [Fact]
    public async Task IsPinned_저장_왕복()
    {
        using var repo = new PostRepository(_db.DbPath);
        int no = await repo.CreateAsync(TestData.NewPost(title: "중요로 올릴 글"));

        Assert.False((await repo.GetByIdAsync(no))!.IsPinned);

        Assert.True(await PinAsync(repo, no));
        Assert.True((await repo.GetByIdAsync(no))!.IsPinned);

        var post = await repo.GetByIdAsync(no);
        post!.IsPinned = false;
        Assert.True(await repo.UpdateAsync(post));
        Assert.False((await repo.GetByIdAsync(no))!.IsPinned);
    }

    [Fact]
    public async Task 중요글은_나중_글보다_앞에_온다()
    {
        // 목록은 기본이 최신순(No DESC)이다. 중요로 표시한 글은 더 오래됐어도 맨 앞으로 와야 한다.
        using var repo = new PostRepository(_db.DbPath);
        int old = await repo.CreateAsync(TestData.NewPost(category: "중요정렬", title: "오래된 공지"));
        await repo.CreateAsync(TestData.NewPost(category: "중요정렬", title: "새 글1"));
        await repo.CreateAsync(TestData.NewPost(category: "중요정렬", title: "새 글2"));

        var before = await repo.GetListAsync(category: "중요정렬");
        Assert.Equal("새 글2", before[0].Title);   // 표시 전에는 최신순

        Assert.True(await PinAsync(repo, old));

        var after = await repo.GetListAsync(category: "중요정렬");
        Assert.Equal(old, after[0].No);
        Assert.Equal("오래된 공지", after[0].Title);
        // 중요 글 뒤는 원래 순서(최신순)를 그대로 지킨다
        Assert.Equal("새 글2", after[1].Title);
        Assert.Equal("새 글1", after[2].Title);
    }

    [Fact]
    public async Task 중요글은_페이지_정렬에서도_앞에_온다()
    {
        // 목록 화면은 GetListWithCountAsync 를 쓰고 정렬 기준을 고를 수 있다.
        // 어떤 정렬을 골라도 중요 글이 먼저 와야 한다.
        using var repo = new PostRepository(_db.DbPath);
        int old = await repo.CreateAsync(TestData.NewPost(category: "중요페이지", title: "가 공지"));
        await repo.CreateAsync(TestData.NewPost(category: "중요페이지", title: "나 새글"));
        Assert.True(await PinAsync(repo, old));

        var (posts, total) = await repo.GetListWithCountAsync(
            limit: 10, offset: 0, category: "중요페이지",
            sortOrder: NewSchool.Board.Models.PostSortOrder.OldestFirst);

        Assert.Equal(2, total);
        Assert.Equal(old, posts[0].No);
        Assert.True(posts[0].IsPinned);
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

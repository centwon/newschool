using System;
using System.Threading.Tasks;
using NewSchool.Board.Services;
using NewSchool.Tests.Infrastructure;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 목록 캐시 무효화 회귀 테스트 (2026-08-25, 40차).
///
/// 댓글·첨부를 더하거나 지우면 <c>Post.HasComment</c>/<c>HasFile</c> 이 DB 에서 바뀌고,
/// 그 값이 목록의 💬·📎 아이콘이 된다. 예전에는 상세 캐시(<c>board:post:N</c>)만 지워서
/// 댓글을 달고 목록으로 돌아와도 아이콘이 최대 2분(목록 캐시 수명)간 안 붙었다.
/// </summary>
public class CachedBoardListInvalidationTests : IClassFixture<BoardTestFixture>
{
    private readonly BoardTestFixture _db;

    public CachedBoardListInvalidationTests(BoardTestFixture db) => _db = db;

    /// <summary>이 테스트만의 분류 — 캐시 키가 다른 테스트와 겹치지 않게 한다.</summary>
    private static string UniqueCategory() => $"캐시무효화_{Guid.NewGuid():N}";

    [Fact]
    public async Task 댓글을_달면_목록의_댓글표시가_바로_바뀐다()
    {
        var category = UniqueCategory();
        using var svc = new CachedBoardService(_db.DbPath);

        int postNo = await svc.SavePostAsync(TestData.NewPost(category: category, title: "댓글대상"));
        Assert.True(postNo > 0);

        // 1) 목록을 한 번 읽어 캐시를 채운다 — 아직 댓글이 없다.
        var before = await svc.GetPostsPagedAsync(1, 20, category);
        Assert.Single(before.Items);
        Assert.False(before.Items[0].HasComment);

        // 2) 댓글을 단다 (BoardService 가 Post.HasComment 를 켠다).
        int commentNo = await svc.CreateCommentAsync(new NewSchool.Board.Comment
        {
            Post = postNo,
            User = "테스트교사",
            DateTime = DateTime.Now,
            Content = "댓글 본문",
        });
        Assert.True(commentNo > 0);

        // 3) 같은 조건으로 목록을 다시 읽으면 캐시가 아니라 새 값을 봐야 한다.
        var after = await svc.GetPostsPagedAsync(1, 20, category);
        Assert.Single(after.Items);
        Assert.True(after.Items[0].HasComment);
    }

    [Fact]
    public async Task 마지막_댓글을_지우면_목록의_댓글표시가_바로_사라진다()
    {
        var category = UniqueCategory();
        using var svc = new CachedBoardService(_db.DbPath);

        int postNo = await svc.SavePostAsync(TestData.NewPost(category: category, title: "댓글삭제대상"));
        int commentNo = await svc.CreateCommentAsync(new NewSchool.Board.Comment
        {
            Post = postNo,
            User = "테스트교사",
            DateTime = DateTime.Now,
            Content = "지울 댓글",
        });

        // 목록 캐시를 채운다 — 이 시점에는 댓글 표시가 켜져 있다.
        var before = await svc.GetPostsPagedAsync(1, 20, category);
        Assert.True(before.Items[0].HasComment);

        Assert.True(await svc.DeleteCommentAsync(commentNo, category));

        var after = await svc.GetPostsPagedAsync(1, 20, category);
        Assert.False(after.Items[0].HasComment);
    }
}

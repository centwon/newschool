using System;
using System.Threading.Tasks;
using NewSchool.Board;
using NewSchool.Board.Services;
using NewSchool.Tests.Infrastructure;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 캐시가 자기 객체를 밖으로 내보내지 않는지 고정 (2026-08-31).
///
/// <para>예전에는 <see cref="CachedBoardService"/> 가 캐시에 담긴 <b>그 인스턴스</b>를
/// 그대로 돌려줬다. 받은 쪽이 값을 고치면 저장하지 않아도 캐시가 함께 바뀌었다 —
/// 편집 화면에서 제목을 고치다 취소해도 목록에 고친 제목이 비쳤고, 댓글 수정이 DB 에서
/// 실패해도 캐시에는 새 내용이 남았다.</para>
///
/// <para>이제 모든 조회가 <c>Clone()</c> 사본을 돌려준다. "누가 이 객체를 또 들고 있나"를
/// 따지지 않아도 되도록, 조회 경로마다 못을 박는다.</para>
/// </summary>
public class CachedBoardServiceIsolationTests : IClassFixture<BoardTestFixture>
{
    private readonly BoardTestFixture _db;

    public CachedBoardServiceIsolationTests(BoardTestFixture db) => _db = db;

    private static string UniqueCategory() => $"사본격리_{Guid.NewGuid():N}";

    [Fact]
    public async Task 상세를_고쳐도_다음_조회는_원래_값이다()
    {
        using var svc = new CachedBoardService(_db.DbPath);
        int postNo = await svc.SavePostAsync(TestData.NewPost(title: "원래 제목"));

        var first = await svc.GetPostAsync(postNo, incrementReadCount: false);
        first!.Title = "고치다 만 제목";          // 편집 화면이 하는 짓
        first.Category = "엉뚱한분류";

        var second = await svc.GetPostAsync(postNo, incrementReadCount: false);

        Assert.NotSame(first, second);
        Assert.Equal("원래 제목", second!.Title);
    }

    [Fact]
    public async Task 조회수_증가_경로에서도_사본을_준다()
    {
        using var svc = new CachedBoardService(_db.DbPath);
        int postNo = await svc.SavePostAsync(TestData.NewPost(title: "조회수사본"));

        var first = await svc.GetPostAsync(postNo);      // 캐시 채움 + 조회수 1
        var second = await svc.GetPostAsync(postNo);     // 캐시 히트 + 조회수 2

        Assert.NotSame(first, second);
        Assert.Equal(1, first!.ReadCount);
        Assert.Equal(2, second!.ReadCount);   // 열람한 값이 곧바로 보여야 한다

        second.Title = "손댐";
        Assert.Equal("조회수사본", (await svc.GetPostAsync(postNo, false))!.Title);
    }

    [Fact]
    public async Task 목록의_글을_고쳐도_다음_조회는_원래_값이다()
    {
        var category = UniqueCategory();
        using var svc = new CachedBoardService(_db.DbPath);
        await svc.SavePostAsync(TestData.NewPost(category: category, title: "목록원본"));

        var first = await svc.GetPostsPagedAsync(1, 20, category);
        first.Items[0].Title = "목록에서 손댐";
        first.Items[0].IsPinned = true;

        var second = await svc.GetPostsPagedAsync(1, 20, category);

        Assert.NotSame(first.Items[0], second.Items[0]);
        Assert.Equal("목록원본", second.Items[0].Title);
        Assert.False(second.Items[0].IsPinned);
    }

    [Fact]
    public async Task 댓글을_고쳐도_다음_조회는_원래_내용이다()
    {
        using var svc = new CachedBoardService(_db.DbPath);
        int postNo = await svc.SavePostAsync(TestData.NewPost(title: "댓글사본"));
        await svc.CreateCommentAsync(new Comment
        {
            Post = postNo,
            User = "테스트교사",
            DateTime = DateTime.Now,
            Content = "원래 댓글",
        });

        var first = await svc.GetCommentsByPostAsync(postNo);
        first[0].Content = "저장 실패할 수정";     // 수정 화면이 먼저 고치고 저장을 시도한다

        var second = await svc.GetCommentsByPostAsync(postNo);

        Assert.NotSame(first[0], second[0]);
        Assert.Equal("원래 댓글", second[0].Content);
    }

    [Fact]
    public async Task 첨부_이름을_고쳐도_다음_조회는_원래_이름이다()
    {
        using var svc = new CachedBoardService(_db.DbPath);
        int postNo = await svc.SavePostAsync(TestData.NewPost(title: "첨부사본"));
        await svc.AddPostFileAsync(new PostFile
        {
            Post = postNo,
            FileName = "원래이름.hwp",
            FileSize = 10,
            DateTime = DateTime.Now,
        });

        var first = await svc.GetPostFilesByPostAsync(postNo);
        first[0].FileName = "손댄이름.hwp";

        var second = await svc.GetPostFilesByPostAsync(postNo);

        Assert.NotSame(first[0], second[0]);
        Assert.Equal("원래이름.hwp", second[0].FileName);
    }

    /// <summary>사본이 값까지 빠뜨리면 안 된다 — 모든 칸이 그대로 따라와야 한다.</summary>
    [Fact]
    public async Task 사본은_모든_값을_그대로_옮긴다()
    {
        using var svc = new CachedBoardService(_db.DbPath);

        var original = TestData.NewPost(category: "값확인", subject: "주제", title: "제목");
        original.User = "테스트교사";
        original.PlainText = "본문 평문";
        original.Content = [1, 2, 3];
        original.IsPinned = true;
        original.IsCompleted = true;

        int postNo = await svc.SavePostAsync(original);
        var loaded = await svc.GetPostAsync(postNo, incrementReadCount: false);
        var copy = loaded!.Clone();

        Assert.Equal(loaded.No, copy.No);
        Assert.Equal(loaded.User, copy.User);
        Assert.Equal(loaded.DateTime, copy.DateTime);
        Assert.Equal(loaded.Category, copy.Category);
        Assert.Equal(loaded.Subject, copy.Subject);
        Assert.Equal(loaded.Title, copy.Title);
        Assert.Equal(loaded.PlainText, copy.PlainText);
        Assert.Equal(loaded.Content, copy.Content);
        Assert.Equal(loaded.ReadCount, copy.ReadCount);
        Assert.Equal(loaded.HasFile, copy.HasFile);
        Assert.Equal(loaded.HasComment, copy.HasComment);
        Assert.Equal(loaded.IsCompleted, copy.IsCompleted);
        Assert.Equal(loaded.IsPinned, copy.IsPinned);
    }
}

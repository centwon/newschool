using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NewSchool.Board;
using NewSchool.Board.Models;
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

    /// <summary>
    /// 사본이 값까지 빠뜨리면 안 된다 — <b>모든 칸</b>이 그대로 따라와야 한다.
    ///
    /// <para>속성을 손으로 적지 않고 <b>반사로 훑는다</b>. 예전에는 열세 줄을 손으로 적어
    /// 두었는데, 그러면 <c>Post</c> 에 칸이 늘 때 <see cref="Post.Clone"/> 과 이 테스트
    /// <b>양쪽</b>에 사람이 기억해서 넣어야 한다. 실제로 이미 새고 있었다 —
    /// <c>RefNo</c>·<c>ReplyOrder</c>·<c>Depth</c> 는 Clone 이 복사하는데 확인하지 않았다.</para>
    ///
    /// <para>빠뜨리면 조용히 아프다. 39차에 중요 글(<c>IsPinned</c>) 칸을 늘릴 때 Clone 에
    /// 넣는 것을 잊었다면, 글을 고쳐 저장하는 순간 중요 표시가 사라졌을 것이다. 답글 칸
    /// (<c>RefNo</c>·<c>Depth</c>)이었다면 글 계층이 무너진다. 이제는 칸을 늘리고 Clone 을
    /// 잊으면 이 테스트가 저절로 깨진다.</para>
    /// </summary>
    [Fact]
    public void 사본은_모든_값을_그대로_옮긴다()
    {
        var original = new Post();
        var writable = typeof(Post).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite)
            .ToList();

        // 기본값으로 두면 "복사를 빠뜨렸다" 와 "원래 기본값이다" 를 구별할 수 없다.
        // 칸마다 서로 다른, 기본값이 아닌 값을 넣는다.
        Assert.NotEmpty(writable);
        for (int i = 0; i < writable.Count; i++)
            writable[i].SetValue(original, DistinctValueFor(writable[i], seed: i + 1));

        var copy = original.Clone();

        foreach (var prop in writable)
        {
            Assert.True(
                Equals(prop.GetValue(original), prop.GetValue(copy)),
                $"Post.Clone() 이 '{prop.Name}' 을 옮기지 않았다 — Clone 에 이 칸을 넣어야 한다.");
        }
    }

    /// <summary>칸마다 다른 값을 만든다(자리를 바꿔 복사해도 걸리도록).</summary>
    private static object DistinctValueFor(PropertyInfo prop, int seed) => prop.PropertyType switch
    {
        var t when t == typeof(string) => $"{prop.Name}-{seed}",
        var t when t == typeof(int) => 1000 + seed,
        var t when t == typeof(bool) => true,
        var t when t == typeof(DateTime) => new DateTime(2026, 1, 1).AddDays(seed),
        var t when t == typeof(byte[]) => new byte[] { (byte)seed, 2, 3 },
        var t => throw new NotSupportedException(
            $"Post.{prop.Name} 의 형식({t.Name})에 쓸 시험값이 없다 — 이 표에 한 줄 넣을 것."),
    };
}

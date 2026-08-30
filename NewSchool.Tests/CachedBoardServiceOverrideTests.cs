using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NewSchool.Board.Services;
using NewSchool.Tests.Infrastructure;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 게시판 캐시 무효화 구멍 고정 (2026-08-30).
///
/// <para><see cref="CachedBoardService"/> 는 <see cref="BoardService"/> 의 쓰기 메서드를
/// 하나씩 가로채 관련 캐시를 비운다. 그런데 <c>UpdatePostIsCompletedAsync</c> 만
/// <c>virtual</c> 이 아니어서 가로채지지 못했고, 메모 '완료'를 켜도 목록 캐시가 그대로였다 —
/// 상세에서 체크하고 뒤로 나오면 목록의 ✓ 와 취소선이 최대 2분간 옛 상태였다.</para>
///
/// <para>새 쓰기 메서드를 더할 때 <c>virtual</c> 이나 override 를 빠뜨려도 컴파일은 되고
/// 앱도 뜬다. 그래서 반사로 전수 확인한다.</para>
/// </summary>
public class CachedBoardServiceOverrideTests : IClassFixture<BoardTestFixture>
{
    private readonly BoardTestFixture _db;

    public CachedBoardServiceOverrideTests(BoardTestFixture db) => _db = db;

    /// <summary>이름이 이렇게 시작하면 DB 를 고치는 메서드로 본다.</summary>
    private static readonly string[] WritePrefixes =
        ["Save", "Create", "Update", "Delete", "Add", "Increment"];

    private static IEnumerable<MethodInfo> WriteMethods() =>
        typeof(BoardService)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Where(m => WritePrefixes.Any(p => m.Name.StartsWith(p, StringComparison.Ordinal)));

    [Fact]
    public void 쓰기_메서드를_하나라도_찾지_못하면_이_테스트가_헛돈다()
    {
        // 이름 규칙이 바뀌어 목록이 비면 아래 검사가 조용히 통과해 버린다 — 그걸 막는 빗장.
        Assert.True(WriteMethods().Count() >= 8,
            "BoardService 에서 쓰기 메서드를 찾지 못했다. 이름 규칙(WritePrefixes)을 확인할 것.");
    }

    /// <summary>
    /// 모든 쓰기 메서드는 <see cref="CachedBoardService"/> 가 실제로 <b>재정의</b>해야 한다.
    /// virtual 이 아니면 재정의할 수 없으므로 이 검사 하나가 두 가지 실수를 다 잡는다.
    /// </summary>
    [Fact]
    public void 모든_쓰기_메서드는_캐시_서비스가_가로챈다()
    {
        var missed = new List<string>();

        foreach (var method in WriteMethods())
        {
            var parameterTypes = method.GetParameters().Select(p => p.ParameterType).ToArray();
            var onCached = typeof(CachedBoardService).GetMethod(
                method.Name,
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                types: parameterTypes,
                modifiers: null);

            if (onCached == null || onCached.DeclaringType != typeof(CachedBoardService))
                missed.Add(method.Name + (method.IsVirtual ? "" : " (virtual 아님)"));
        }

        Assert.True(missed.Count == 0,
            "CachedBoardService 가 가로채지 못하는 쓰기 메서드가 있다 — 그만큼 캐시가 옛 값을 " +
            "들고 있게 된다: " + string.Join(", ", missed));
    }

    /// <summary>
    /// 완료(확인) 표시를 바꾸면 목록이 곧바로 따라와야 한다.
    /// 목록의 ✓ 아이콘과 제목 취소선이 이 값으로 그려진다.
    /// </summary>
    [Fact]
    public async Task 완료_표시를_바꾸면_목록이_곧바로_따라온다()
    {
        var category = $"완료캐시_{Guid.NewGuid():N}";
        using var svc = new CachedBoardService(_db.DbPath);

        int postNo = await svc.SavePostAsync(TestData.NewPost(category: category, title: "완료대상"));

        // 목록 캐시를 채운다 — 아직 미완료다.
        var before = await svc.GetPostsPagedAsync(1, 20, category);
        Assert.False(Assert.Single(before.Items).IsCompleted);

        Assert.True(await svc.UpdatePostIsCompletedAsync(postNo, true));

        var after = await svc.GetPostsPagedAsync(1, 20, category);
        Assert.True(Assert.Single(after.Items).IsCompleted);

        // 되돌릴 때도 마찬가지
        Assert.True(await svc.UpdatePostIsCompletedAsync(postNo, false));
        Assert.False(Assert.Single((await svc.GetPostsPagedAsync(1, 20, category)).Items).IsCompleted);
    }

    /// <summary>상세 캐시도 함께 비워야 한다.</summary>
    [Fact]
    public async Task 완료_표시를_바꾸면_상세도_곧바로_따라온다()
    {
        var category = $"완료상세_{Guid.NewGuid():N}";
        using var svc = new CachedBoardService(_db.DbPath);

        int postNo = await svc.SavePostAsync(TestData.NewPost(category: category, title: "완료상세대상"));

        Assert.False((await svc.GetPostAsync(postNo, incrementReadCount: false))!.IsCompleted);

        Assert.True(await svc.UpdatePostIsCompletedAsync(postNo, true));

        Assert.True((await svc.GetPostAsync(postNo, incrementReadCount: false))!.IsCompleted);
    }
}

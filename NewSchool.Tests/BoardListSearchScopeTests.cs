using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NewSchool.Board.Services;
using NewSchool.Tests.Infrastructure;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 게시판 목록의 <b>검색이 페이징·정렬·새로고침을 넘어 살아남는지</b> 고정한다(2026-08-30).
///
/// <para>예전에는 목록 조회가 둘로 갈라져 있었다. 검색은 <c>SearchPostsAsync</c> 가,
/// 나머지(페이지 넘김·정렬·필터·새로고침·뒤로가기)는 <c>LoadPostsAsync</c> 가 맡았는데
/// <b>뒤엣것이 검색어를 아예 넘기지 않았다</b>. 총 페이지 수는 검색 결과 기준으로 잡혀
/// '다음' 버튼은 켜져 있는데, 누르면 <b>전체 목록 2페이지</b>가 나왔다. 검색창에는 검색어가
/// 그대로 남아 있어 증상은 "검색이 안 먹는다"로만 보였다.</para>
///
/// <para>조회가 다시 갈라지는 것은 컴파일러가 못 잡으므로 소스를 훑어 고정한다
/// (<see cref="ComboBoxTagTests"/> 와 같은 방식). 아래 두 소스 가드가 그 역할이고,
/// 마지막 하나는 그 조회가 실제로 검색 안에서 페이지를 센다는 동작 확인이다.</para>
/// </summary>
public class BoardListSearchScopeTests : IClassFixture<BoardTestFixture>
{
    private readonly BoardTestFixture _db;

    public BoardListSearchScopeTests(BoardTestFixture db) => _db = db;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "NewSchool.csproj")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string ReadSource(string relativePath)
    {
        var path = Path.Combine(RepoRoot(), relativePath);
        Assert.True(File.Exists(path), $"소스를 찾지 못했다: {relativePath}");
        return File.ReadAllText(path);
    }

    /// <summary>
    /// 목록 조회 입구는 하나여야 한다. 둘로 갈라지는 순간 한쪽이 검색어를 빠뜨리기 시작한다 —
    /// 실제로 그렇게 났던 결함이다.
    /// </summary>
    [Fact]
    public void 목록조회_호출은_한_곳뿐이다()
    {
        var source = ReadSource(Path.Combine("Board", "ViewModels", "PostListViewModel.cs"));
        int calls = Regex.Matches(source, @"GetPostsPagedAsync\s*\(").Count;

        Assert.True(calls == 1,
            $"PostListViewModel 안의 목록 조회가 {calls} 곳이다. " +
            "조회는 LoadAsync 하나로 모아야 한다 — 갈라지면 한쪽이 검색어를 빠뜨린다.");
    }

    /// <summary>그 하나뿐인 조회는 검색 조건 셋을 모두 넘겨야 한다.</summary>
    [Theory]
    [InlineData("searchTitle:")]
    [InlineData("searchContent:")]
    [InlineData("searchText:")]
    public void 목록조회는_검색조건을_모두_넘긴다(string argument)
    {
        var source = ReadSource(Path.Combine("Board", "ViewModels", "PostListViewModel.cs"));
        var call = Regex.Match(source, @"GetPostsPagedAsync\s*\((?:[^()]|\([^()]*\))*\)");

        Assert.True(call.Success, "PostListViewModel 에서 목록 조회 호출을 찾지 못했다.");
        Assert.True(call.Value.Contains(argument),
            $"목록 조회에 {argument} 가 빠졌다 — 페이지를 넘기면 검색이 풀린다.");
    }

    /// <summary>
    /// 검색창은 <c>UpdateSourceTrigger=PropertyChanged</c> 여야 한다.
    ///
    /// <para>WinUI 의 <c>TextBox.Text</c> 는 TwoWay 기본값이 <b>LostFocus</b> 다. 그래서
    /// 검색어를 치고 곧바로 Enter 를 누르면 포커스가 그대로여서 ViewModel 에 아직 아무것도
    /// 넘어가지 않았고, <b>직전 검색어(첫 시도면 빈 값)</b>로 검색됐다. 버튼 클릭은 포커스가
    /// 옮겨가며 값이 넘어와 우연히 동작했다 — 그래서 "Enter 만 안 된다"로 보였다.</para>
    /// </summary>
    [Fact]
    public void 검색창_바인딩은_입력_즉시_반영된다()
    {
        var xaml = ReadSource(Path.Combine("Board", "Pages", "PostListPage.xaml"));
        var binding = Regex.Match(xaml, @"x:Name=""SearchTextBox""[\s\S]*?/>");

        Assert.True(binding.Success, "PostListPage.xaml 에서 SearchTextBox 를 찾지 못했다.");
        Assert.True(binding.Value.Contains("UpdateSourceTrigger=PropertyChanged"),
            "검색창 TwoWay 바인딩에 UpdateSourceTrigger=PropertyChanged 가 없다 — " +
            "TextBox.Text 의 기본값은 LostFocus 라, 치고 바로 Enter 를 누르면 직전 검색어로 검색된다.");
    }

    /// <summary>
    /// 검색 결과의 2페이지는 <b>검색 안에서</b> 세야 한다.
    /// (검색어에 걸리지 않는 글이 섞여 들어오면 조회가 검색을 잃은 것이다.)
    /// </summary>
    [Fact]
    public async Task 검색결과의_다음_페이지도_검색_안에서_센다()
    {
        const string category = "검색페이징";
        using var svc = new BoardService(_db.DbPath);

        // 검색어에 걸리는 글 25개 + 걸리지 않는 글 10개
        for (int i = 1; i <= 25; i++)
            await svc.SavePostAsync(TestData.NewPost(category: category, title: $"회의록 {i:00}"));
        for (int i = 1; i <= 10; i++)
            await svc.SavePostAsync(TestData.NewPost(category: category, title: $"잡글 {i:00}"));

        var page2 = await svc.GetPostsPagedAsync(
            pageNumber: 2, pageSize: 20, category: category,
            searchTitle: true, searchText: "회의록");

        Assert.Equal(25, page2.TotalCount);
        Assert.Equal(2, page2.TotalPages);
        Assert.Equal(5, page2.Items.Count);
        Assert.All(page2.Items, p => Assert.Contains("회의록", p.Title));
    }
}

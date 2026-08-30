using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 게시판 목록(표 보기)의 뼈대 고정 (2026-08-30).
///
/// <para>표 보기는 머리글 줄과 행 템플릿이 <b>서로 다른 곳</b>에 각자 열 정의를 들고 있다.
/// 한쪽만 고치면 컴파일도 되고 앱도 뜨지만 머리글과 칸이 어긋난 채로 나간다 —
/// 눈으로만 잡히는 결함이라 소스로 대조한다.</para>
/// </summary>
public class BoardListLayoutTests
{
    private static string ListXaml()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "NewSchool.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        return File.ReadAllText(Path.Combine(dir!.FullName, "Board", "Pages", "PostListPage.xaml"));
    }

    /// <summary>머리글 줄과 행 템플릿의 열 너비가 같아야 한다.</summary>
    [Fact]
    public void 표_보기의_머리글과_행은_같은_열_너비를_쓴다()
    {
        var xaml = ListXaml();

        // 표 보기의 ColumnDefinitions 묶음은 머리글·행 순서로 두 번 나온다.
        var blocks = Regex.Matches(xaml, @"<Grid\.ColumnDefinitions>([\s\S]*?)</Grid\.ColumnDefinitions>")
            .Select(m => Regex.Matches(m.Groups[1].Value, @"<ColumnDefinition Width=""([^""]+)""")
                              .Select(w => w.Groups[1].Value).ToArray())
            .Where(widths => widths.Length == 5)   // 번호·제목·작성자(숨김)·날짜·조회
            .ToList();

        Assert.True(blocks.Count >= 2,
            $"표 보기의 5열 정의를 두 벌 찾지 못했다(찾은 수: {blocks.Count}).");
        Assert.Equal(blocks[0], blocks[1]);
    }

    /// <summary>
    /// 행 높이는 Grid 의 Padding 하나로만 정해져야 한다.
    /// Button 기본 스타일의 <c>MinHeight</c>(32)가 바닥을 잡으면 Padding 을 줄여도 행이
    /// 낮아지지 않는다 — 왜 안 줄어드는지 알기 어려운 자리라 고정한다.
    /// </summary>
    [Fact]
    public void 행_버튼은_최소높이를_풀어_둔다()
    {
        var xaml = ListXaml();
        var rowButton = Regex.Match(xaml, @"<Button Click=""PostItem_Click""[\s\S]*?>");

        Assert.True(rowButton.Success, "표 보기의 행 버튼을 찾지 못했다.");
        Assert.Contains(@"MinHeight=""0""", rowButton.Value);
    }

    /// <summary>
    /// 목록 화면에는 제목 줄을 두지 않는다 — 왼쪽 메뉴가 이미 같은 말을 하고 있어 되풀이였다.
    /// 되살아나면 목록이 한 줄만큼 다시 좁아진다.
    /// </summary>
    [Fact]
    public void 목록_화면에는_제목_줄이_없다()
    {
        var xaml = ListXaml();

        Assert.DoesNotContain("x:Name=\"TitleText\"", xaml);
        Assert.DoesNotContain("x:Name=\"TitleRow\"", xaml);
    }
}

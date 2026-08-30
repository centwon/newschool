using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 작성자 이름 고정 (2026-08-30).
///
/// <para><see cref="SettingProperty{T}"/> 에는 <c>implicit operator T</c> 가 있어서
/// <c>Settings.UserName ?? "사용자"</c> 라고 쓰면 <c>??</c> 의 null 검사가 값이 아니라
/// <b>래퍼 객체</b>를 본다. 래퍼는 초기화 뒤 결코 null 이 아니므로 폴백은 <b>죽은 코드</b>였다.
/// 설정 화면은 빈 이름도 그대로 저장하므로, 이름을 지우면 그 뒤 글의 작성자가 빈 문자열로 남았다.</para>
///
/// <para>컴파일러도 잡지 못하고 앱도 멀쩡히 뜨는 함정이라 소스로 고정한다.</para>
/// </summary>
public class AuthorNameTests
{
    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "NewSchool.csproj")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!;
    }

    private static string[] ProductSources() => Directory
        .EnumerateFiles(RepoRoot().FullName, "*.cs", SearchOption.AllDirectories)
        .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                 && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                 && !p.Contains($"{Path.DirectorySeparatorChar}NewSchool.Tests{Path.DirectorySeparatorChar}"))
        .ToArray();

    /// <summary>
    /// 설정 값에 <c>??</c> 를 바로 붙이면 안 된다 — 값이 아니라 래퍼를 검사하게 된다.
    /// (값을 받치고 싶으면 <c>Settings.Xxx.Value</c> 를 쓰거나 전용 속성을 만든다.)
    /// </summary>
    [Fact]
    public void 설정값에_물음표둘을_바로_붙인_곳이_없다()
    {
        var offenders = ProductSources()
            .SelectMany(p => File.ReadLines(p)
                .Select((line, i) => (p, i, line))
                // 주석은 뺀다 — 이 함정을 설명하는 주석이 스스로 걸린다
                .Where(x => !x.line.TrimStart().StartsWith("//")
                         && !x.line.TrimStart().StartsWith("///")
                         && !x.line.TrimStart().StartsWith("*"))
                .Where(x => Regex.IsMatch(x.line, @"Settings\.[A-Za-z_][A-Za-z0-9_]*\s*\?\?")))
            .Select(x => $"{Path.GetFileName(x.p)}:{x.i + 1}")
            .ToList();

        Assert.True(offenders.Count == 0,
            "설정 값에 ?? 를 바로 붙였다 — SettingProperty 는 implicit 변환이 있어 폴백이 " +
            "절대 발동하지 않는다: " + string.Join(", ", offenders));
    }

    /// <summary>
    /// 글·댓글·자료를 만드는 곳은 모두 <c>Settings.AuthorName</c> 한 벌을 쓴다.
    /// 한 곳만 <c>Settings.User</c>(교사 ID)를 쓰고 있어서 자료 글의 작성자만 ID 로 남았고,
    /// 목록의 '작성자순' 정렬이 이름과 ID 두 갈래로 섞였다.
    /// </summary>
    [Theory]
    [InlineData("Board/Pages/PostEditPage.xaml.cs")]
    [InlineData("Board/ViewModels/PostDetailViewModel.cs")]
    [InlineData("Board/Controls/MemoBoard.xaml.cs")]
    [InlineData("Dialogs/LessonJournalWindow.xaml.cs")]
    [InlineData("Dialogs/MaterialEditDialog.xaml.cs")]
    public void 작성자를_적는_화면은_모두_같은_한_벌을_쓴다(string relativePath)
    {
        var path = Path.Combine(RepoRoot().FullName,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"소스를 찾지 못했다: {relativePath}");

        var source = File.ReadAllText(path);

        Assert.True(source.Contains("Settings.AuthorName"),
            $"{relativePath} 가 작성자를 제 나름대로 정한다 — Settings.AuthorName 을 쓸 것.");

        // 작성자 자리에 교사 ID(Settings.User)를 넣지 않는다
        Assert.DoesNotContain("User = Settings.User.Value", source);
    }

    /// <summary>이름을 정하지 않았을 때의 대체 이름은 한 곳에만 적혀 있어야 한다.</summary>
    [Fact]
    public void 대체_이름은_Settings에만_적혀_있다()
    {
        var settings = File.ReadAllText(Path.Combine(RepoRoot().FullName, "Settings.cs"));
        Assert.Contains("AuthorName", settings);
        Assert.Contains("\"사용자\"", settings);
    }
}

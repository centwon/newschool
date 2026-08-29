using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// <b>이 앱의 XAML 은 <c>x:Bind</c> 만 쓴다. 고전 <c>{Binding}</c> 은 쓰지 않는다.</b>
///
/// <para>취향이 아니라 <b>게시 방식</b> 때문이다. 이 프로젝트는 <c>PublishTrimmed</c> 와
/// <c>PublishAot</c> 를 켜고 낸다(<c>NewSchool.csproj</c>). 그 아래에서 <c>{Binding}</c> 은
/// 반사로 속성을 찾으므로, 대상 타입에 <c>[Microsoft.UI.Xaml.Data.Bindable]</c> 이나
/// <c>[WinRT.GeneratedBindableCustomProperty]</c> 가 붙어 있지 않으면
/// <b>빌드는 통과하고 실행할 때 값만 안 나온다.</b> 오류도 경고도 없다.</para>
///
/// <para><c>x:Bind</c> 는 컴파일 시점에 코드를 만들어 내므로 그 함정이 없고, 이름을 잘못
/// 적으면 <b>빌드가 깨진다</b>. 그래서 이쪽으로 통일한다.</para>
///
/// <para>이 규칙을 정하면서, 아무도 쓰지 않던 위 두 특성 13개를 걷어냈다(2026-08-30).
/// <c>{Binding}</c> 이 정말 필요해지면 이 테스트가 먼저 걸리므로, 그때 특성도 함께
/// 되살리면 된다 — <b>모르고 지나가는 일만 막으면 된다.</b></para>
/// </summary>
public class XBindOnlyTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "NewSchool.csproj")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static bool IsGenerated(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}");

    [Fact]
    public void XAML_은_고전_Binding_을_쓰지_않는다()
    {
        string root = FindRepoRoot();

        // "{Binding" — {x:Bind 나 {StaticResource 등과 섞이지 않는다.
        var classicBinding = new Regex(@"\{\s*Binding\b");
        var offenders = new List<string>();

        foreach (var xaml in Directory.EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories))
        {
            if (IsGenerated(xaml)) continue;

            var lines = File.ReadAllLines(xaml);
            for (int i = 0; i < lines.Length; i++)
            {
                if (classicBinding.IsMatch(lines[i]))
                    offenders.Add($"{Path.GetFileName(xaml)}:{i + 1}  {lines[i].Trim()}");
            }
        }

        Assert.True(offenders.Count == 0,
            "고전 {Binding} 이 들어왔습니다. 트리밍·AOT 게시에서 대상 타입에 [Bindable] 계열 특성이 " +
            "없으면 **실행할 때 값만 조용히 안 나옵니다**. x:Bind 로 바꾸거나, 정말 필요하면 " +
            "대상 타입에 특성을 붙이고 이 테스트를 함께 고치세요:\n"
            + string.Join("\n", offenders));
    }

    /// <summary>
    /// 코드에서 문자열 경로로 거는 바인딩도 같은 함정이다 —
    /// <c>new Binding { Path = new PropertyPath("X") }</c> 는 컴파일러가 이름을 봐 주지 않는다.
    /// </summary>
    [Fact]
    public void 코드에서_문자열_경로_바인딩을_걸지_않는다()
    {
        string root = FindRepoRoot();

        var pathBinding = new Regex(@"\bnew\s+Binding\s*[({]|\bPropertyPath\s*\(");
        var offenders = new List<string>();

        foreach (var cs in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (IsGenerated(cs)) continue;
            if (Path.GetFileName(cs) == nameof(XBindOnlyTests) + ".cs") continue;

            var lines = File.ReadAllLines(cs);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.TrimStart().StartsWith("//")) continue;   // 주석은 제외
                if (pathBinding.IsMatch(line))
                    offenders.Add($"{Path.GetFileName(cs)}:{i + 1}  {line.Trim()}");
            }
        }

        Assert.True(offenders.Count == 0,
            "코드에서 문자열 경로 바인딩을 걸고 있습니다 — 트리밍·AOT 에서 조용히 안 돕니다:\n"
            + string.Join("\n", offenders));
    }
}

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 인쇄물이 <b>형식마다 다른 말을 하지 않게</b> 못박는다 — 53차(인쇄·미리보기 축).
///
/// <para>좌석배정표 하나가 PDF·HTML·Excel 세 형식으로 나간다. 규칙이 세 곳에 흩어지면
/// 반드시 갈라진다 — 실제로 <b>명렬표에 실을 학생</b>을 고르는 규칙이 Excel 만 달라서,
/// 안 보이게 해 둔 자리에 앉은 학생이 xlsx 명단에서만 사라졌다. 배치 그림에도 안 나오는
/// 학생이라 그 종이 어디에도 없게 된다.</para>
/// </summary>
public class PrintOutputGuardTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "NewSchool.csproj")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot(), relativePath));

    /// <summary>
    /// 세 형식이 <c>BuildRoster</c> 하나만 쓴다. 어느 한 곳이 제 규칙을 따로 적기 시작하면
    /// 여기서 걸린다.
    /// </summary>
    [Fact]
    public void 명렬표_규칙은_한_곳에서만_정한다()
    {
        string source = Read("Services/SeatsPrintService.cs");

        // 학생을 고르는 LINQ 가 BuildRoster 밖에 또 있으면 규칙이 둘이 된 것이다.
        var picks = Regex.Matches(source, @"\.Where\(c => c\.StudentData != null");
        Assert.True(picks.Count == 1,
            $"명렬표 학생을 고르는 자리가 {picks.Count} 곳이다 — BuildRoster 하나로 모을 것.");

        Assert.Equal(3, Regex.Matches(source, @"BuildRoster\(").Count - 1);   // 정의 1 + 호출 3
    }

    /// <summary>
    /// 만든 파일을 여는 길은 <c>ExportPaths.TryOpen</c> 하나다(45차 규칙).
    ///
    /// <para>예전에는 인쇄 화면들이 각자 <c>new Uri($"file:///…")</c> 를 만들어 열었다.
    /// 그 길은 경로에 <c>#</c> 같은 글자가 있으면 조각으로 잘리고, 실패해도 로그가 없다.</para>
    /// </summary>
    [Fact]
    public void 만든_파일을_여는_길은_한_곳이다()
    {
        string root = RepoRoot();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
            if (rel.Contains("/obj/") || rel.Contains("/bin/") || rel.StartsWith("NewSchool.Tests/"))
                continue;

            // 주석은 세지 않는다 — ExportPaths 의 설명이 "예전에는 이렇게 열었다" 며
            // 그 꼴을 인용하고 있다.
            bool inCode = File.ReadLines(file).Any(line =>
                !line.TrimStart().StartsWith("//", System.StringComparison.Ordinal) &&
                Regex.IsMatch(line, @"new Uri\(\$?""file:///"));

            if (inCode) offenders.Add(rel);
        }

        Assert.True(offenders.Count == 0,
            "만든 파일을 file:/// URI 로 직접 여는 자리가 있다 — Helpers.ExportPaths.TryOpen 을 쓸 것:\n  " +
            string.Join("\n  ", offenders));
    }

    /// <summary>
    /// 인쇄물에만 있고 화면에는 없는 표시가 있으면 안 된다. 지정 좌석(📌)이 그랬다 —
    /// 종이에는 붙는데 화면에는 아무 표시가 없어, 무엇이 고정인지 뽑아 봐야 알았다.
    /// </summary>
    [Fact]
    public void 지정_좌석_표식은_화면에도_있다()
    {
        Assert.Contains("📌", Read("Services/SeatsPrintService.cs"));
        Assert.Contains("📌", Read("Controls/PhotoCard.xaml"));
        Assert.Contains("SetFixedStyle", Read("Controls/PhotoCard.xaml.cs"));
    }
}

using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// <b>DB 경로를 돌려주는 속성은 경로만 만든다 — 폴더를 만들거나 파일을 건드리지 않는다.</b>
///
/// <para><see cref="NewSchool.SchoolDatabase"/> 의 <c>DbPath</c> 는 오래도록 getter 안에서
/// 데이터 폴더를 만들었다. 읽는 곳이 130군데인데 거의 전부 화면 이벤트 한복판
/// (<c>using var repo = new X(SchoolDatabase.DbPath)</c>)이라, 폴더를 만들지 못하면
/// <see cref="UnauthorizedAccessException"/> 이 <b>속성을 읽는 그 자리</b>에서 튀었다.
/// 130군데를 전부 감쌀 수는 없으므로, 폴더를 만드는 일은 <c>InitAsync</c> 처럼
/// <b>실패를 사용자에게 설명할 수 있는 한 자리</b>에만 둔다(49차·50차의 교훈).</para>
///
/// <para>형제 둘(<c>Board</c>·<c>Scheduler</c>)은 처음부터 그렇게 되어 있었다. 셋이 다시
/// 어긋나지 않도록 여기서 함께 지킨다 — 늘어나는 것만 막으면 된다.</para>
/// </summary>
public class PathGetterPurityTests
{
    public static TheoryData<string> DbPathOwners => new()
    {
        "SchoolDatabase.cs",
        "Board/Board.cs",
        "Scheduler/Scheduler.cs",
    };

    [Theory]
    [MemberData(nameof(DbPathOwners))]
    public void DbPath_게터는_파일을_건드리지_않는다(string relativePath)
    {
        string source = File.ReadAllText(Path.Combine(RepoRoot(), relativePath));
        string body = MemberBody(source, "DbPath");

        // 제대로 된 자리를 찾았는지부터 확인한다(못 찾으면 이 시험은 아무것도 안 지킨다).
        Assert.Contains("Path.Combine", body);

        Assert.DoesNotContain("Directory.", body);
        Assert.DoesNotContain("File.", body);
    }

    /// <summary>
    /// <c>static string 이름</c> 선언을 찾아 그 본문만 잘라 온다.
    /// 식 본문(<c>=&gt; ...;</c>)이면 세미콜론까지, 블록이면 중괄호 짝을 세어 자른다.
    /// </summary>
    private static string MemberBody(string source, string memberName)
    {
        var declaration = Regex.Match(source, @"static\s+string\s+" + Regex.Escape(memberName) + @"\b");
        Assert.True(declaration.Success, $"{memberName} 선언을 찾지 못했다 — 시험이 낡았다.");

        int i = declaration.Index + declaration.Length;
        while (i < source.Length && char.IsWhiteSpace(source[i])) i++;

        if (source[i] == '=')   // => Path.Combine(...);
        {
            int end = source.IndexOf(';', i);
            Assert.True(end > 0, $"{memberName} 의 식 본문이 끝나지 않는다.");
            return source[i..end];
        }

        Assert.Equal('{', source[i]);
        int depth = 0, j = i;
        while (j < source.Length)
        {
            if (source[j] == '{') depth++;
            else if (source[j] == '}' && --depth == 0) return source[i..(j + 1)];
            j++;
        }

        Assert.Fail($"{memberName} 의 블록 본문이 닫히지 않는다.");
        return string.Empty;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "NewSchool.csproj")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}

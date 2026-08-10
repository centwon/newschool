using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// XAML 에 <c>Tag="1"</c> 로 쓰면 그 값은 <b>문자열</b>이다. 코드에서 <c>(int)item.Tag</c> 로
/// 언박싱하면 고를 때마다 <c>InvalidCastException</c> 이 나고, 그 줄에서 멈춰 뒤따르는 저장이
/// 아예 실행되지 않는다 — 실제로 학교 설정의 <b>학기가 영영 저장되지 않았다</b>.
///
/// 컴파일러가 못 잡는 런타임 캐스트라 소스를 훑어 고정한다.
/// 검사 대상은 <b>같은 화면 안에 위험한 조합이 있는 경우</b>다:
/// XAML 이 <c>Tag</c> 를 문자열 리터럴로 쓰는데 코드비하인드가 <c>(int)</c> 로 언박싱하는 것.
///
/// int 로 넣고 싶으면 XAML 에서 <c>&lt;x:Int32&gt;1&lt;/x:Int32&gt;</c> 를 쓰면 되고
/// (TeacherTimetablePage 가 그렇게 한다), 아니면 코드에서
/// <c>int.Parse(item.Tag.ToString())</c> 로 읽으면 된다.
/// </summary>
public class ComboBoxTagTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "NewSchool.csproj")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Fact]
    public void XAML_문자열_Tag_를_int_로_언박싱하는_화면이_없어야_한다()
    {
        string root = FindRepoRoot();

        // XAML 속성 문법의 Tag(= 문자열). <ComboBoxItem.Tag><x:Int32>…는 걸리지 않는다.
        var stringTagInXaml = new Regex(@"<ComboBoxItem[^>]*\bTag\s*=\s*""[^""]*""");
        // (int)xxx.Tag / (int)((ComboBoxItem)xxx).Tag
        var unboxingInCode = new Regex(@"\(int\)\s*\(?\(?[A-Za-z_][A-Za-z0-9_.\[\]()]*\)?\s*\.Tag\b");

        var offenders = new List<string>();

        foreach (var xaml in Directory.EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories))
        {
            if (xaml.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                xaml.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                continue;

            if (!stringTagInXaml.IsMatch(File.ReadAllText(xaml)))
                continue;

            string codeBehind = xaml + ".cs";
            if (!File.Exists(codeBehind)) continue;

            var lines = File.ReadAllLines(codeBehind);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.TrimStart().StartsWith("//")) continue;   // 주석은 제외
                if (unboxingInCode.IsMatch(line))
                    offenders.Add($"{Path.GetFileName(codeBehind)}:{i + 1}  {line.Trim()}");
            }
        }

        Assert.True(offenders.Count == 0,
            "XAML 에서 Tag 를 문자열로 쓰는 화면인데 코드가 (int) 로 언박싱합니다 — 고르는 순간 터집니다:\n"
            + string.Join("\n", offenders));
    }
}

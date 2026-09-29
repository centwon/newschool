using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// <b>열린 대화상자(ContentDialog) 안에서는 <c>MessageBox</c> 를 부르지 않는다.</b>
///
/// <para>대화상자는 한 번에 하나만 뜨게 <c>MessageBox._dialogGate</c> 가 줄을 세운다. 대화상자 안에서
/// <c>MessageBox</c>·<c>UserErrorReporter</c> 를 부르면 그 확인·알림은 바깥 창이 닫히기를 기다린다 —
/// 수업 변경 목록의 [삭제]·수업 편집의 [강의실 다시 정하기] 는 눌러도 반응이 없다가 창을 닫은 뒤에야
/// 확인이 떴고, 수강생 등록은 저장 실패 알림과 서로 기다려 [저장] 이 멈췄다(2026-09-30).
/// 확인은 <c>InlineConfirm</c>(단추 옆 팝업), 알림은 창 안 InfoBar 로 한다.</para>
/// </summary>
public class DialogNestingGuardTests
{
    private static readonly Regex NestedDialog = new(@"\bMessageBox\.\w+\(|\bUserErrorReporter\.\w+\(");

    [Fact]
    public void 대화상자_안에서_MessageBox_를_부르지_않는다()
    {
        string root = RepoRoot();
        var offenders = new List<string>();
        int dialogs = 0;

        foreach (var file in Directory.EnumerateFiles(root, "*.xaml.cs", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
            if (rel.StartsWith("obj/") || rel.StartsWith("bin/") || rel.Contains("/obj/") || rel.Contains("/bin/"))
                continue;

            string[] lines = File.ReadAllLines(file);
            if (!Regex.IsMatch(string.Join('\n', lines), @"partial class \w+ : ContentDialog\b")) continue;

            dialogs++;
            for (int i = 0; i < lines.Length; i++)
            {
                // 주석은 뺀다 — 왜 부르지 않는지 적어 둔 주석이 스스로 걸리면 안 된다.
                string code = lines[i];
                int comment = code.IndexOf("//", System.StringComparison.Ordinal);
                if (comment >= 0) code = code[..comment];

                if (NestedDialog.IsMatch(code))
                    offenders.Add($"{rel}:{i + 1}");
            }
        }

        Assert.True(dialogs >= 15, $"대화상자가 {dialogs}개뿐이다 — 검색이 빗나갔다");
        Assert.True(offenders.Count == 0,
            "열린 대화상자 안에서 MessageBox 를 부르는 곳이 있다 — 바깥 창이 닫힐 때까지 뜨지 않는다. " +
            "확인은 InlineConfirm, 알림은 창 안 InfoBar 로 할 것:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// <b>대화상자에 담겨 뜨는 컨트롤도 MessageBox 를 부르지 않는다.</b>
    ///
    /// <para>위 시험은 ContentDialog 를 물려받은 클래스만 본다. 학생카드는 [학생 정보 보기](학급 일지·
    /// 누가 기록·동아리·수업 활동)에서 <c>new ContentDialog { Content = card }</c> 로 담겨 떠서 그 그물을
    /// 빠져나갔다 — 사진 [삭제] 확인이 창을 닫은 뒤에야 떴다(2026-09-30). 대화상자에 담아 띄우는
    /// 컨트롤이 늘면 여기 이름을 더할 것.</para>
    /// </summary>
    [Theory]
    [InlineData("Controls/StudentCard.xaml.cs")]
    public void 대화상자에_담겨_뜨는_컨트롤은_MessageBox_를_부르지_않는다(string relativePath)
    {
        string path = Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"소스를 찾지 못했다: {relativePath}");

        string[] lines = File.ReadAllLines(path);
        var offenders = new List<string>();
        for (int i = 0; i < lines.Length; i++)
        {
            string code = lines[i];
            int comment = code.IndexOf("//", System.StringComparison.Ordinal);
            if (comment >= 0) code = code[..comment];
            if (NestedDialog.IsMatch(code)) offenders.Add($"{relativePath}:{i + 1}");
        }

        Assert.True(offenders.Count == 0,
            "대화상자에 담겨 뜨는 컨트롤이 MessageBox 를 부른다 — 그 창이 닫힐 때까지 뜨지 않는다:\n  " +
            string.Join("\n  ", offenders));
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// <b>[저장] 을 눌러야 저장되는 화면은, 나가는 모든 길에서 물어야 한다.</b> — 52차(닫을 때 축).
///
/// <para>이 앱에는 저장 방식이 둘 있다. 학생카드·학급일지·메모판은 <b>스스로 저장</b>하고
/// (3초 디바운스 + 화면을 떠날 때 마무리), 수업 일지 창·메모 편집 창·누가기록 창·학생부
/// 일괄 입력·게시글 작성·자리 배치·학생 추가는 <b>[저장] 을 눌러야</b> 저장된다.</para>
///
/// <para>뒤쪽에서 나가는 길은 여럿인데(닫기 버튼, 제목표시줄 X, 왼쪽 메뉴로 이동, 앱 종료)
/// <b>길마다 다르게 굴었다</b> — 학생부 일괄 입력은 [닫기] 버튼에서만 물었고 X 로 닫으면
/// 그대로 사라졌다. 게시글 작성은 [취소] 버튼에서만 물었고 메뉴를 누르면 사라졌다.
/// 수업 일지 창은 어느 길로도 묻지 않았다.</para>
///
/// <para>그래서 규칙을 세운다: 저장 버튼이 있는 화면은 <see cref="NewSchool.Controls.IUnsavedWork"/>
/// 를 구현하거나(페이지 — 메뉴 이동·앱 종료가 이것을 본다), <c>UnsavedWorkGuard.AskBeforeClosing</c>
/// 을 걸거나(창 — X 를 막는다), 스스로 저장해야 한다. 새 화면이 늘면 이 시험이 먼저 걸린다.</para>
/// </summary>
public class UnsavedWorkGuardTests
{
    /// <summary>저장 버튼이 있어도 지킬 것이 없는 화면과 그 이유.</summary>
    private static readonly Dictionary<string, string> Allowed = new()
    {
        // 학생 정보 화면의 편집은 StudentCard 가 스스로 저장한다(3초 디바운스 + Unloaded 마무리).
        // 이 페이지의 [저장] 은 "지금 바로" 를 위한 버튼이라 나갈 때 잃을 것이 없다.
        ["Pages/PageStudentInfo.xaml.cs"] = "학생카드가 스스로 저장한다",
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "NewSchool.csproj")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>저장 버튼을 가진 화면.</summary>
    private static readonly Regex SaveButton = new(@"\b(BtnSave|SaveButton|BtnSaveAll)_Click\b");

    /// <summary>나가는 길을 지키고 있다는 표시.</summary>
    private static readonly Regex Guarded = new(@"IUnsavedWork|AskBeforeClosing|_autoSaveTimer|AutoSaveDelayMs");

    [Fact]
    public void 저장_버튼이_있는_화면은_나갈_때_묻는다()
    {
        string root = RepoRoot();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(root, "*.xaml.cs", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
            if (rel.Contains("/obj/") || rel.Contains("/bin/")) continue;

            string source = File.ReadAllText(file);
            if (!SaveButton.IsMatch(source)) continue;
            if (Guarded.IsMatch(source)) continue;
            if (Allowed.ContainsKey(rel)) continue;

            offenders.Add(rel);
        }

        Assert.True(offenders.Count == 0,
            "[저장] 을 눌러야 저장되는데 나갈 때 아무것도 묻지 않는 화면이 있다.\n" +
            "페이지면 IUnsavedWork 를 구현하고(메뉴 이동·앱 종료가 본다), 창이면 " +
            "UnsavedWorkGuard.AskBeforeClosing 으로 X 를 막을 것. 잃을 것이 없으면 이 시험의 " +
            "Allowed 에 이유와 함께 적을 것:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// 창을 닫는 길이 둘(버튼·X)이므로, <b>버튼 쪽에서 결과를 먼저 확정하면 안 된다</b>.
    ///
    /// <para>닫기 확인에서 [계속 편집] 을 골랐는데 이미 <c>TrySetResult(false)</c> 를 해 뒀다면,
    /// 창은 열려 있는데 기다리던 쪽은 "취소" 를 받고 돌아가 버린다 — 창 하나에 주인이 둘이 된다.
    /// 결과는 실제로 닫힌 뒤(<c>OnWindowClosed</c>)에만 넣는다.</para>
    /// </summary>
    [Theory]
    [InlineData("Dialogs/LessonJournalWindow.xaml.cs")]
    [InlineData("Board/Dialogs/MemoEditDialog.xaml.cs")]
    [InlineData("Controls/RichTextEditorWin.xaml.cs")]
    public void 취소_버튼은_결과를_먼저_확정하지_않는다(string relativePath)
    {
        string source = File.ReadAllText(Path.Combine(RepoRoot(), relativePath));

        var cancel = Regex.Match(source,
            @"private void BtnCancel_Click\([^)]*\)\s*\{(?<body>.*?)\n    \}", RegexOptions.Singleline);

        Assert.True(cancel.Success, $"{relativePath} 에서 BtnCancel_Click 을 찾지 못했다");

        string body = cancel.Groups["body"].Value;
        Assert.DoesNotContain("TrySetResult", body);
        Assert.Contains("Close()", body);
    }
}

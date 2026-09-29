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

        // 컨트롤은 놓인 곳이 지킨다. 누가기록 상자는 누가기록 창(StudentLogDialog)이
        // AskBeforeClosing 으로, 학생부 상자는 놓인 화면이 LeaveAsync 로 묻는다(아래 시험이 본다).
        ["Controls/StudentLogBox.xaml.cs"] = "누가기록 창이 X·[닫기] 를 지킨다",
        ["Controls/StudentSpecBox.xaml.cs"] = "놓인 화면이 떠날 때 LeaveAsync 로 묻는다",
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "NewSchool.csproj")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>
    /// 빌드 산출물 경로인가. 저장소 뿌리 바로 아래의 <c>obj/</c> 는 앞에 <c>/</c> 가 없어
    /// <c>Contains("/obj/")</c> 로는 걸러지지 않는다 — 빌드가 복사해 둔 .xaml 을 세게 된다.
    /// </summary>
    private static bool IsBuildOutput(string rel) =>
        rel.StartsWith("obj/") || rel.StartsWith("bin/") || rel.Contains("/obj/") || rel.Contains("/bin/");

    /// <summary>
    /// 이 XAML 의 뿌리가 Page 인가. 앞에 <c>&lt;?xml ...?&gt;</c> 선언이 붙은 파일이 있어
    /// "&lt;Page 로 시작하는가" 로는 동아리·수업 활동 화면을 놓친다.
    /// </summary>
    private static bool IsPage(string markup) =>
        Regex.IsMatch(markup, @"^\s*(<\?xml[^>]*\?>\s*)?(<!--.*?-->\s*)*<Page\s", RegexOptions.Singleline);

    /// <summary>
    /// 저장 버튼을 가진 화면. <c>OnSave…Click</c> 도 센다 — 이 이름을 쓴 학사일정 관리·학생부 기록·
    /// 교과 세특 화면이 빠져서, 고친 채 떠나면 묻지 않고 버리는 것을 이 시험이 못 잡았다(2026-09-29·30).
    /// </summary>
    private static readonly Regex SaveButton = new(@"\b(BtnSave|SaveButton|BtnSaveAll)_Click\b|\bOnSave\w*Click\b");

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
            if (IsBuildOutput(rel)) continue;

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
    /// <b>안에 Frame 을 품은 페이지는, 그 Frame 에 놓인 화면의 판정을 넘긴다.</b>
    ///
    /// <para>메뉴 이동·앱 닫기는 메인 창 Frame 에 놓인 페이지만 본다. 업무 관리 화면은 업무 게시판을
    /// 제 Frame 에 품는데, 거기서 [새 글 쓰기] 로 연 편집 화면은 보이지 않아 쓰던 글을 두고 메뉴를
    /// 눌러도 묻지 않고 사라졌다(2026-09-25).</para>
    /// </summary>
    [Fact]
    public void Frame_을_품은_페이지는_안쪽_화면의_판정을_넘긴다()
    {
        string root = RepoRoot();
        var offenders = new List<string>();
        int hosts = 0;

        foreach (var xaml in Directory.EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(root, xaml).Replace(Path.DirectorySeparatorChar, '/');
            if (IsBuildOutput(rel)) continue;
            if (rel == "MainWindow.xaml") continue;   // 판정하는 쪽이다

            string markup = File.ReadAllText(xaml);
            if (!IsPage(markup) || !Regex.IsMatch(markup, @"<Frame\b")) continue;

            hosts++;
            string code = File.Exists(xaml + ".cs") ? File.ReadAllText(xaml + ".cs") : "";
            if (!code.Contains("IUnsavedWork")) offenders.Add(rel);
        }

        Assert.True(hosts > 0, "Frame 을 품은 페이지를 하나도 찾지 못했다 — 검색이 깨졌다");
        Assert.True(offenders.Count == 0,
            "Frame 을 품었는데 IUnsavedWork 로 안쪽 화면의 판정을 넘기지 않는 페이지:\n  " +
            string.Join("\n  ", offenders));
    }

    /// <summary>
    /// <b>편집 창이 닫힐 때 목록을 다시 읽는 페이지는, 그 사이 페이지를 떠났으면 읽지 않는다.</b>
    ///
    /// <para>편집 창(누가기록·일괄 입력)은 독립 창이라 연 채로 메뉴를 옮길 수 있다. 그 뒤 창에서
    /// 저장하면 떠난 페이지가 이미 닫힌 서비스로 다시 읽다가, 저장은 됐는데 "로그 로드 중 오류 …
    /// connection is open" 을 띄웠다(2026-09-26 실측).</para>
    /// </summary>
    [Fact]
    public void 창이_닫힐_때_다시_읽는_페이지는_떠났으면_읽지_않는다()
    {
        string root = RepoRoot();
        var offenders = new List<string>();
        int handlers = 0;

        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "Pages"), "*.xaml.cs"))
        {
            string source = File.ReadAllText(file);
            var matches = Regex.Matches(source,
                @"async void (?<name>\w+)\(object\?? \w+, (Microsoft\.UI\.Xaml\.)?WindowEventArgs \w+\)\s*\{(?<body>.*?)\n\s*\}",
                RegexOptions.Singleline);

            foreach (Match m in matches)
            {
                if (!m.Groups["body"].Value.Contains("Load")) continue;   // 다시 읽는 처리기만
                handlers++;
                if (!m.Groups["body"].Value.Contains("IsLoaded"))
                    offenders.Add($"{Path.GetFileName(file)}: {m.Groups["name"].Value}");
            }
        }

        Assert.True(handlers >= 5, $"창이 닫힐 때 다시 읽는 처리기를 {handlers}개밖에 못 찾았다 — 검색이 빗나갔다");
        Assert.True(offenders.Count == 0,
            "창이 닫힐 때 다시 읽는데, 그 사이 페이지를 떠났는지(IsLoaded) 보지 않는 처리기:\n  " +
            string.Join("\n  ", offenders));
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
            @"private (async )?void BtnCancel_Click\([^)]*\)\s*\{(?<body>.*?)\n    \}", RegexOptions.Singleline);

        Assert.True(cancel.Success, $"{relativePath} 에서 BtnCancel_Click 을 찾지 못했다");

        string body = cancel.Groups["body"].Value;
        Assert.DoesNotContain("TrySetResult", body);
        Assert.Contains("UnsavedWorkGuard.CloseAsync(this)", body);
    }

    /// <summary>
    /// <b>누가기록 목록을 놓은 화면은, 목록을 다시 읽기 전에 고친 기록을 묻는다.</b>
    ///
    /// <para>누가기록 목록(<c>LogListViewer</c>)은 칸을 고치면 [저장] 을 눌러야 들어간다. 누가기록
    /// 화면만 학생·학급을 바꾸기 전에 물었고, 같은 목록을 쓰는 동아리·수업 활동·학생 정보 화면은
    /// 묻지 않고 다시 읽어 고친 기록이 사라졌다. 학급 일지는 저장할 길조차 없었다(2026-09-24).
    /// 묻는 일은 한 벌(<c>LogListViewer.AskSaveModifiedAsync</c>)이다.</para>
    /// </summary>
    [Fact]
    public void 누가기록_목록을_놓은_화면은_다시_읽기_전에_고친_기록을_묻는다()
    {
        string root = RepoRoot();
        var offenders = new List<string>();
        int pages = 0;

        foreach (var xaml in Directory.EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(root, xaml).Replace(Path.DirectorySeparatorChar, '/');
            if (IsBuildOutput(rel)) continue;
            if (!File.ReadAllText(xaml).Contains("<controls:LogListViewer")) continue;

            string code = xaml + ".cs";
            if (!File.Exists(code)) continue;
            string source = File.ReadAllText(code);

            pages++;
            if (!source.Contains("AskSaveModifiedAsync("))
                offenders.Add(rel + ".cs");
        }

        Assert.True(pages >= 5, $"누가기록 목록을 놓은 화면이 {pages}개뿐이다 — 검색이 빗나갔다");
        Assert.True(offenders.Count == 0,
            "목록을 다시 읽으면 고친 누가기록이 묻지 않고 사라지는 화면이 있다. " +
            "목록을 다시 읽기 전에 LogList.AskSaveModifiedAsync 를 부를 것:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// <b>누가기록 목록을 놓은 페이지는 앱을 닫기 전에도 묻는다.</b>
    ///
    /// <para>이 화면들은 떠날 때(<c>Unloaded</c>) 고친 기록을 묻는데, 앱을 닫는 길에서는 그때 창이
    /// 이미 닫혀 물을 곳이 없다 — 누가기록 한 줄을 고치고 X 를 누르면 묻지 않고 꺼졌다(2026-09-25
    /// 실측). 닫기 확인이 창을 닫기 전에 부르는 <c>IAsksBeforeLeaving</c> 을 구현해야 한다.</para>
    /// </summary>
    [Fact]
    public void 누가기록_목록을_놓은_페이지는_앱을_닫기_전에_묻는다()
    {
        string root = RepoRoot();
        var offenders = new List<string>();
        int pages = 0;

        foreach (var xaml in Directory.EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(root, xaml).Replace(Path.DirectorySeparatorChar, '/');
            if (IsBuildOutput(rel)) continue;

            string markup = File.ReadAllText(xaml);
            if (!IsPage(markup) || !markup.Contains("<controls:LogListViewer")) continue;

            pages++;
            string code = File.Exists(xaml + ".cs") ? File.ReadAllText(xaml + ".cs") : "";
            if (!code.Contains("IAsksBeforeLeaving")) offenders.Add(rel + ".cs");
        }

        Assert.True(pages >= 5, $"누가기록 목록을 놓은 페이지가 {pages}개뿐이다 — 검색이 빗나갔다");
        Assert.True(offenders.Count == 0,
            "앱을 닫으면 고친 누가기록이 묻지 않고 사라지는 페이지가 있다. " +
            "NewSchool.Controls.IAsksBeforeLeaving 을 구현할 것:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// <b>누가기록 창을 여는 자리는 열기 전에 목록에서 고치던 기록을 묻는다.</b>
    ///
    /// <para>그 창이 닫히면 화면이 목록을 다시 읽는데, 예전에는 묻지 않아 목록에서 고치던 줄이
    /// 사라졌다(학생 정보·누가 기록·동아리·수업 활동·학급 일지, 2026-09-30). 닫을 때가 아니라 열 때
    /// 묻는다 — 닫을 때 "저장" 하면 창에서 방금 넣은 것을 옛 줄로 덮을 수 있다.</para>
    /// </summary>
    [Fact]
    public void 누가기록_창을_열기_전에_고친_기록을_묻는다()
    {
        string root = RepoRoot();
        var offenders = new List<string>();
        int sites = 0;

        foreach (var xaml in Directory.EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(root, xaml).Replace(Path.DirectorySeparatorChar, '/');
            if (IsBuildOutput(rel)) continue;

            string markup = File.ReadAllText(xaml);
            if (!IsPage(markup) || !markup.Contains("<controls:LogListViewer")) continue;
            if (!File.Exists(xaml + ".cs")) continue;

            string code = File.ReadAllText(xaml + ".cs");
            foreach (Match m in Regex.Matches(code, @"new (Dialogs\.)?StudentLogDialog\("))
            {
                sites++;
                // 여는 자리가 든 메서드 — 앞쪽 가장 가까운 메서드 선언부터 여기까지
                int start = code.LastIndexOf("private ", m.Index, StringComparison.Ordinal);
                string before = start >= 0 ? code[start..m.Index] : string.Empty;
                if (!before.Contains("CheckUnSaved"))
                {
                    int line = code[..m.Index].Count(c => c == '\n') + 1;
                    offenders.Add($"{rel}.cs:{line}");
                }
            }
        }

        Assert.True(sites >= 9, $"누가기록 창을 여는 자리가 {sites}곳뿐이다 — 검색이 빗나갔다");
        Assert.True(offenders.Count == 0,
            "누가기록 창을 열기 전에 목록에서 고치던 기록을 묻지 않는 자리가 있다(창을 닫으면 목록을 " +
            "다시 읽어 사라진다):\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// <b>학생부 상자를 놓은 페이지는 떠날 때·앱을 닫기 전에 그 상자도 묻는다.</b>
    ///
    /// <para>학생부 상자(<c>StudentSpecBox</c>)는 [저장] 을 눌러야 들어가는데, 놓인 화면들이 학생을
    /// 바꿀 때만 물었다. 고친 채 다른 메뉴로 가거나 앱을 닫으면 말없이 버렸다(2026-09-30).
    /// 떠날 때(Unloaded)와 닫기 전(AskBeforeLeavingAsync) 모두 <c>AskUnsavedOnLeaveAsync</c> 를
    /// 지나고, 그 안에서 <c>SpecBox.LeaveAsync</c> 를 불러야 한다.</para>
    /// </summary>
    [Fact]
    public void 학생부_상자를_놓은_페이지는_떠날_때_묻는다()
    {
        string root = RepoRoot();
        var offenders = new List<string>();
        int pages = 0;

        foreach (var xaml in Directory.EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(root, xaml).Replace(Path.DirectorySeparatorChar, '/');
            if (IsBuildOutput(rel)) continue;

            string markup = File.ReadAllText(xaml);
            if (!IsPage(markup) || !markup.Contains("<controls:StudentSpecBox")) continue;

            pages++;
            string code = File.Exists(xaml + ".cs") ? File.ReadAllText(xaml + ".cs") : "";

            var leave = Regex.Match(code, @"Task AskUnsavedOnLeaveAsync\(\)\s*\{(?<body>[^}]*)\}");
            bool leaveAsksSpec = leave.Success && leave.Groups["body"].Value.Contains("SpecBox.LeaveAsync()");
            var close = Regex.Match(code, @"Task AskBeforeLeavingAsync\(\)\s*\{(?<body>[^}]*)\}");
            bool closeUsesLeave = close.Success && close.Groups["body"].Value.Contains("AskUnsavedOnLeaveAsync()");
            // 떠날 때(Unloaded) 경로: 불러 놓기만 하는 `_ = AskUnsavedOnLeaveAsync()` 가 있어야 한다
            bool unloadUsesLeave = code.Contains("_ = AskUnsavedOnLeaveAsync()");

            if (!leaveAsksSpec || !closeUsesLeave || !unloadUsesLeave)
                offenders.Add(rel + ".cs");
        }

        Assert.True(pages >= 3, $"학생부 상자를 놓은 페이지가 {pages}개뿐이다 — 검색이 빗나갔다");
        Assert.True(offenders.Count == 0,
            "고친 학생부를 두고 떠나거나 앱을 닫으면 묻지 않고 버리는 페이지가 있다:\n  " +
            string.Join("\n  ", offenders));
    }

    /// <summary>
    /// <b>창 안의 [닫기]·[취소] 도 X 와 같은 확인을 지난다.</b>
    ///
    /// <para><c>AskBeforeClosing</c> 이 거는 <c>AppWindow.Closing</c> 은 X·Alt+F4 처럼 시스템이
    /// 닫을 때만 오고, 코드로 부른 <c>Window.Close()</c> 에는 오지 않는다. 52차에 버튼을
    /// <c>Close()</c> 하나로 줄여 두었더니, 실제로 몰아 보니 학생부 일괄 입력에서 X 는 묻고
    /// [닫기] 는 묻지 않고 닫아 고치던 것이 사라졌다(2026-09-24). 저장한 뒤의 <c>Close()</c> 는
    /// 물을 것이 없으니 그대로 둬도 된다 — 여기서는 "버튼 길이 하나라도 확인을 지나는가" 만 본다.</para>
    /// </summary>
    [Fact]
    public void 창의_닫기_버튼은_X_와_같은_확인을_지난다()
    {
        string root = RepoRoot();
        var offenders = new List<string>();
        int windows = 0;

        foreach (var file in Directory.EnumerateFiles(root, "*.xaml.cs", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
            if (IsBuildOutput(rel)) continue;
            if (rel == "MainWindow.xaml.cs") continue;   // 앱 창에는 닫기 버튼이 없다(X 뿐)

            string source = File.ReadAllText(file);
            if (!source.Contains("AskBeforeClosing(")) continue;

            windows++;
            if (!source.Contains("UnsavedWorkGuard.CloseAsync(this)"))
                offenders.Add(rel);
        }

        Assert.True(windows >= 5, $"닫기 확인을 거는 창이 {windows}개뿐이다 — 검색이 빗나갔다");
        Assert.True(offenders.Count == 0,
            "X 는 묻는데 창 안의 [닫기]·[취소] 가 Close() 를 바로 불러 묻지 않고 닫는 창이 있다. " +
            "버튼에서는 UnsavedWorkGuard.CloseAsync(this) 를 부를 것:\n  " + string.Join("\n  ", offenders));
    }
}

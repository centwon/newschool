using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// <b>키보드만으로 쓸 때</b> 다시 막히지 않게 못박는다 — 54차.
///
/// <para>이 축은 정적으로는 거의 보이지 않는다. 실제로 앱을 띄워 Tab 을 눌러 보고서야
/// 알았다 — 홈 화면에서 열여섯 번째 Tab 이 메모 편집기로 들어가면 <b>Tab·Shift+Tab·
/// Ctrl+Tab·Esc 어느 것으로도 나올 수 없었고</b>, 자리 배정 화면의 탭 정지 열다섯 곳
/// 가운데 <b>자리는 하나도 없었다</b>. 그래서 여기 있는 시험은 "그때 무엇을 고쳤는지"를
/// 소스에 남겨, 모르고 되돌리는 일만 막는 용도다.</para>
///
/// <para>⚠ 이 시험들은 UI 를 실제로 몰지 못한다. 진짜 확인은
/// <c>.claude/skills/run-newschool/driver.ps1 tabs</c> 로 Tab 을 눌러 보는 것이다.</para>
/// </summary>
public class KeyboardOnlyGuardTests
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

    private static IEnumerable<string> XamlFiles()
    {
        string root = RepoRoot();
        foreach (var file in Directory.EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');

            // ⚠ obj·bin 은 저장소 **뿌리**에 있다. "/obj/" 로만 거르면 "obj/Debug/…" 가
            //   그대로 통과해, 빌드가 복사해 둔 XAML 사본까지 검사 대상이 된다(실제로 겪었다).
            if (rel.StartsWith("obj/") || rel.StartsWith("bin/") ||
                rel.Contains("/obj/") || rel.Contains("/bin/") || rel.StartsWith("NewSchool.Tests/"))
                continue;
            yield return rel;
        }
    }

    /// <summary>주석을 지운 본문. 주석이 옛 모습을 인용하고 있어 그대로 세면 오검출이 난다(53차에 겪었다).</summary>
    private static string WithoutComments(string xaml) =>
        Regex.Replace(xaml, "<!--.*?-->", string.Empty, RegexOptions.Singleline);

    /// <summary>
    /// 리치 편집기에서 <b>빠져나가는 길</b>이 살아 있어야 한다.
    ///
    /// <para>이 편집기는 앱 안 열 곳에서 쓰인다. Tab 을 편집기가 삼키므로(실측: Tab 은
    /// 실제로 탭 문자를 넣는다 — 그래서 뺏지 않았다) <c>Esc</c> 가 유일한 탈출구다.</para>
    /// </summary>
    [Fact]
    public void 리치_편집기에서_Esc_로_나올_수_있다()
    {
        string source = Read("Controls/RichTextEditor.xaml.cs");

        Assert.Contains("VirtualKey.Escape", source);

        // ⚠ handledEventsToo 가 없으면 편집기가 이미 Handled 로 표시해 넘겨 영영 안 온다.
        Assert.Contains("handledEventsToo: true", source);

        // ⚠ 인자 없는 TryMoveFocus 는 데스크톱 앱에서 던진다. SearchRoot 를 준 과부하여야 한다.
        Assert.Contains("FindNextElementOptions", source);
        Assert.Contains("SearchRoot", source);
    }

    /// <summary>
    /// <c>ItemsRepeater</c> 항목을 <b>키보드로 고를 수 있어야</b> 한다.
    ///
    /// <para><c>ListView</c> 와 달리 항목 컨테이너를 만들어 주지 않으므로, 항목이 스스로
    /// 탭 정지가 되지 않으면 마우스 말고는 고를 길이 없다. 학급일지 목록이 그랬다.</para>
    /// </summary>
    [Fact]
    public void 학급일지_목록을_키보드로_고를_수_있다()
    {
        string xaml = WithoutComments(Read("Controls/ClassDiaryListWin.xaml"));

        Assert.Contains("IsTabStop=\"True\"", xaml);
        Assert.Contains("DiaryItem_KeyDown", xaml);
        Assert.Contains("DiaryItem_KeyDown", Read("Controls/ClassDiaryListWin.xaml.cs"));
    }

    /// <summary>
    /// 좌석 카드가 탭 정지여야 한다 — 이것이 없으면 <b>자리 배정 화면에서 자리에 닿을 수 없다</b>.
    /// 컨텍스트 메뉴(미사용·지정·미표시 좌석)도 함께 닫힌다.
    /// </summary>
    [Fact]
    public void 좌석_카드에_키보드로_닿을_수_있다()
    {
        string xaml = WithoutComments(Read("Controls/PhotoCard.xaml"));
        Assert.Contains("IsTabStop=\"True\"", xaml);

        // 닿았을 때 무엇인지 말해야 한다.
        Assert.Contains("UpdateAutomationName", Read("Controls/PhotoCard.xaml.cs"));
    }

    /// <summary>
    /// <c>Grid</c>·<c>Border</c> 에 <c>KeyDown</c> 만 달고 <c>IsTabStop</c> 을 빠뜨리면
    /// <b>그 핸들러는 죽은 코드가 된다</b>. 포커스를 못 받으니 키가 오지 않는다.
    ///
    /// <para>MonthPicker 가 정확히 그랬다 — 방향키·Enter·Esc 처리를 다 적어 놓고
    /// <c>GridMonth.Focus()</c> 가 조용히 실패해, 팝업을 키보드로 열어도 열두 달 중
    /// 아무것도 고를 수 없었다(54차 실측).</para>
    /// </summary>
    [Fact]
    public void KeyDown_을_단_비컨트롤은_탭_정지여야_한다()
    {
        // 여는 태그 하나를 통째로 잡는다(속성이 여러 줄에 걸친다).
        var element = new Regex(@"<(Grid|Border|StackPanel|Canvas)\b(?<attrs>(?:[^>""]|""[^""]*"")*?)/?>",
                                RegexOptions.Singleline);
        var offenders = new List<string>();

        foreach (var rel in XamlFiles())
        {
            string xaml = WithoutComments(Read(rel));
            foreach (Match m in element.Matches(xaml))
            {
                string attrs = m.Groups["attrs"].Value;
                if (!attrs.Contains("KeyDown=")) continue;
                if (attrs.Contains("IsTabStop=\"True\"")) continue;

                int line = xaml.Take(m.Index).Count(c => c == '\n') + 1;
                offenders.Add($"{rel}:{line}");
            }
        }

        Assert.True(offenders.Count == 0,
            "KeyDown 을 달았는데 IsTabStop=\"True\" 가 없다 — 포커스를 못 받아 그 핸들러는 " +
            "영영 불리지 않는다:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// 컨텍스트 메뉴는 <c>ContextRequested</c> 로 연다. <c>RightTapped</c> 는 포인터에만 오고
    /// 키보드의 메뉴 키·Shift+F10 에는 오지 않는다.
    ///
    /// <para>⚠ <c>PageSeats</c> 의 <c>BtnArrange</c> 는 <b>일부러</b> RightTapped 다 —
    /// 학생들 앞에서 돌리는 시연용 숨은 기능이라 화면에 단서를 두지 않기로 한 것이고,
    /// 같은 단추의 왼쪽 누르기로 진짜 기능이 따로 있다. 그래서 여기서만 허용한다.</para>
    /// </summary>
    [Fact]
    public void 컨텍스트_메뉴는_키보드로도_열린다()
    {
        var allowed = new HashSet<string> { "Pages/PageSeats.xaml" };
        var offenders = new List<string>();

        foreach (var rel in XamlFiles())
        {
            if (allowed.Contains(rel)) continue;
            if (WithoutComments(Read(rel)).Contains("RightTapped=")) offenders.Add(rel);
        }

        Assert.True(offenders.Count == 0,
            "RightTapped 는 키보드 메뉴 키에 오지 않는다 — ContextRequested 를 쓸 것:\n  " +
            string.Join("\n  ", offenders));
    }

    /// <summary>
    /// <c>ContentDialog</c> 는 모두 <c>DefaultButton</c> 을 정해야 한다. 없으면 Enter 가
    /// 아무 일도 하지 않는다 — 스물한 개 중 <c>UnifiedItemDialog</c> 하나만 비어 있었다.
    /// </summary>
    [Fact]
    public void 모든_대화상자에서_Enter_가_듣는다()
    {
        var offenders = XamlFiles()
            .Where(rel => Read(rel).Contains("<ContentDialog"))
            .Where(rel => !Read(rel).Contains("DefaultButton="))
            .ToList();

        Assert.True(offenders.Count == 0,
            "DefaultButton 이 없어 Enter 가 아무 일도 하지 않는다:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// 저장 단추가 있으면 <c>Ctrl+S</c> 도 받아야 한다. 페이지 열 곳에는 있고 편집기 네 곳에는
    /// 없었다 — 앱이 한쪽에서 가르쳐 놓고 다른 쪽에서 안 받으면 그게 더 나쁘다.
    /// </summary>
    [Fact]
    public void 저장이_있는_곳은_Ctrl_S_도_받는다()
    {
        // 글을 쓰다 저장하는 자리가 아닌 곳은 뺀다.
        var allowed = new Dictionary<string, string>
        {
            // 설정 대화상자 안, 구글 연결 뒤에야 보이는(기본 Collapsed) 단추다.
            // 여러 묶음이 한 화면에 있어 Ctrl+S 가 무엇을 저장하는지 가리킬 수 없고,
            // 평소 보이지도 않는 단추에 전역 단축키를 걸면 오히려 헷갈린다.
            ["Dialogs/CalendarSettingsDialog.xaml"] = "설정 대화상자의 숨은 단추",
        };

        var saveButton = new Regex(@"x:Name=""[A-Za-z]*(?:BtnSave|SaveButton)[A-Za-z]*""");
        var offenders = XamlFiles()
            .Where(rel => !allowed.ContainsKey(rel))
            .Where(rel => saveButton.IsMatch(Read(rel)))
            .Where(rel => !Read(rel).Contains("Key=\"S\""))
            .ToList();

        Assert.True(offenders.Count == 0,
            "저장 단추가 있는데 Ctrl+S 가 없다:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// 목록 항목은 <b>자기 이름을 스스로 대야</b> 한다. 없으면 <c>ListView</c> 가 항목 객체의
    /// <c>ToString()</c> 을 읽어, 낭독기가 "NewSchool.Scheduler.AgendaItem" 이라거나
    /// 학생의 내부 ID 15자리를 통째로 소리 내어 읽는다(54차 실측).
    /// </summary>
    [Fact]
    public void 목록_항목이_타입_이름을_읽지_않는다()
    {
        Assert.Contains("AutomationProperties.Name", Read("Scheduler/KAgendaControl.xaml"));
        Assert.Contains("AccessibleText", Read("Scheduler/KAgendaControl.xaml.cs"));

        Assert.Contains("AutomationProperties.Name", Read("Controls/ListStudent.xaml"));
        Assert.Contains("AccessibleText", Read("Models/Enrollment.cs"));

        Assert.Contains("AutomationProperties.Name", Read("Controls/SchoolScheduleListControl.xaml"));
    }

    /// <summary>
    /// 그림뿐인 단추에는 UIA 이름이 있어야 한다. <b>툴팁은 이름이 되지 않는다</b> —
    /// 눈으로만 보이고 낭독기는 "단추"라고만 읽는다.
    ///
    /// <para>같은 이유로 <b>글자가 패널 안에 들어 있어도 이름이 되지 않는다</b>.
    /// <c>Content="저장"</c> 처럼 문자열일 때만 자동으로 이름이 된다(54차 실측).</para>
    /// </summary>
    [Fact]
    public void 그림뿐인_단추에_이름이_있다()
    {
        // 상태에 따라 코드에서 이름을 붙이는 곳은 정적으로 볼 수 없다.
        var namedInCode = new HashSet<string> { "Controls/StudentSpecBox.xaml" };

        var element = new Regex(@"<(?<tag>Button|ToggleButton|HyperlinkButton|AppBarButton|SplitButton|DropDownButton)\b(?<attrs>(?:[^>""]|""[^""]*"")*?)>(?<inner>.*?)</\k<tag>>",
                                RegexOptions.Singleline);
        var offenders = new List<string>();

        foreach (var rel in XamlFiles())
        {
            if (namedInCode.Contains(rel)) continue;
            string xaml = WithoutComments(Read(rel));

            foreach (Match m in element.Matches(xaml))
            {
                string attrs = m.Groups["attrs"].Value;
                string inner = m.Groups["inner"].Value;

                if (attrs.Contains("AutomationProperties.Name")) continue;
                if (Regex.IsMatch(attrs, @"\bContent=""[^""]+""")) continue;   // 문자열 Content 는 곧 이름이다

                bool hasIcon = Regex.IsMatch(inner, @"<(FontIcon|SymbolIcon|PathIcon|BitmapIcon|ImageIcon|AnimatedIcon)\b");
                if (!hasIcon) continue;

                // 글자가 문자열 Content 로 있지 않은 채 아이콘만 보이는 단추
                int line = xaml.Take(m.Index).Count(c => c == '\n') + 1;
                offenders.Add($"{rel}:{line}");
            }
        }

        Assert.True(offenders.Count == 0,
            "그림뿐인 단추에 AutomationProperties.Name 이 없다 — 툴팁도 패널 안 글자도 " +
            "UIA 이름이 되지 않는다:\n  " + string.Join("\n  ", offenders));
    }
}

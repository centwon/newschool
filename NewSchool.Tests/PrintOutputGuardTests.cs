using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using QuestPDF.Fluent;
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
    /// 앱과 같은 설정(<see cref="NewSchool.Helpers.PdfLibrarySetup"/>)으로 <b>한글이 든 PDF 가 실제로
    /// 만들어지는지</b> 본다.
    ///
    /// <para>QuestPDF 2026.9 로 올리자 시스템 글꼴을 안 쓰는 것이 기본이 되어 한글 글리프가 없다며
    /// 앱의 PDF 인쇄가 전부 멈췄다. 그때 시험은 모두 통과했다 — PDF 를 실제로 만드는 시험이 하나도
    /// 없었기 때문이다. 이모지·드문 한자도 넣는다: 사람이 쓴 메모에 섞이는 그 한 글자 때문에 학급
    /// 전체 인쇄가 실패하면 안 된다(<c>ThrowOnMissingTextGlyphs = false</c>).</para>
    /// </summary>
    [Fact]
    public void 한글이_든_PDF_가_만들어진다()
    {
        NewSchool.Helpers.PdfLibrarySetup.Apply();

        byte[] pdf = Document.Create(container => container.Page(page =>
        {
            page.Content().Column(col =>
            {
                col.Item().Text("학생 정보 카드 — 2026학년도 3학년 1반 5번");
                col.Item().Text("지정 좌석 📌 · 중요 ★ · 완료 ✓ · 더 있음 ▾ 12");
                col.Item().Text("드문 한자 龘 와 이모지 😀 가 섞인 학생 메모");
            });
        })).GeneratePdf();

        Assert.True(pdf.Length > 1000, $"PDF 가 비정상적으로 작다({pdf.Length}바이트).");

        // 한글이 빈 칸이 아니라 한글 글꼴로 찍혔는지 — 글꼴 이름은 PDF 안에 글자 그대로 남는다.
        string raw = System.Text.Encoding.ASCII.GetString(pdf);
        Assert.Contains("Malgun", raw);
    }

    /// <summary>
    /// 게시본에서 Lato 를 빼는 것(csproj)과 "없는 글꼴이면 멈춤" 을 끄는 것(PdfLibrarySetup)은 짝이다.
    ///
    /// <para>시험 폴더에는 Lato 가 그대로 복사되므로 위 시험은 Lato 가 없을 때를 재현하지 못한다 —
    /// 그래서 짝이 맞는지를 따로 본다. 한쪽만 바뀌면: Lato 를 넣으면 PDF 의 영문·숫자가 지금까지
    /// 게시한 판(Segoe UI)과 달라지고, 멈춤을 켜면 게시본의 PDF 가 전부 실패한다(2026-09-24 실측).</para>
    /// </summary>
    [Fact]
    public void Lato_를_빼는_것과_글꼴_멈춤_해제는_짝이다()
    {
        string csproj = Read("NewSchool.csproj");
        Assert.Contains("StartsWith('QuestPDF.Fonts.Lato')", csproj);

        NewSchool.Helpers.PdfLibrarySetup.Apply();
        Assert.False(QuestPDF.Settings.ThrowOnMissingFontFamilies,
            "게시본에는 Lato 가 없다 — 없는 글꼴에서 멈추면 PDF 가 전부 실패한다.");
    }

    /// <summary>
    /// 같은 규칙의 다른 꼴 — <c>Process.Start</c> 를 직접 부르는 자리도 정해진 곳뿐이어야 한다.
    ///
    /// <para>위 검사는 <c>file:///</c> 꼴만 봐서, 누가기록 인쇄·누가기록/학생부 일괄 출력 세 곳이
    /// <c>Process.Start</c> 로 직접 여는 것을 놓쳤다. 뷰어가 없으면 던진 예외가 바깥 catch 로 가서
    /// <b>파일은 만들어 놓고 "오류" 로 끝났고</b> 저장 위치 안내도 건너뛰었다.</para>
    ///
    /// <para>허용: 여는 길 자체(<c>ExportPaths</c>), 브라우저로 로그인 주소 열기(구글), 앱 설정의
    /// 폴더 열기, 게시판 첨부 열기(<c>AttachmentPolicy</c> 가 따로 다룬다).</para>
    /// </summary>
    [Fact]
    public void Process_Start_를_직접_부르는_자리는_정해져_있다()
    {
        var allowed = new HashSet<string>
        {
            "Helpers/ExportPaths.cs",
            "Google/GoogleAuthService.cs",
            "Pages/AppSettingsPage.xaml.cs",
            "Board/Controls/FileItemBox.xaml.cs",
            "Board/Controls/PostFileListBox.xaml.cs",
            "Board/Pages/PostDetailPage.xaml.cs",
        };

        string root = RepoRoot();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
            if (rel.Contains("/obj/") || rel.Contains("/bin/") || rel.StartsWith("obj/") || rel.StartsWith("bin/")
                || rel.StartsWith("NewSchool.Tests/") || allowed.Contains(rel))
                continue;

            bool inCode = File.ReadLines(file).Any(line =>
                !line.TrimStart().StartsWith("//", System.StringComparison.Ordinal) &&
                Regex.IsMatch(line, @"\bProcess\.Start\("));

            if (inCode) offenders.Add(rel);
        }

        Assert.True(offenders.Count == 0,
            "Process.Start 를 직접 부르는 자리가 있다 — 만든 파일을 열 거면 Helpers.ExportPaths.TryOpen 을 쓸 것:\n  " +
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

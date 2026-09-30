using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 게시글을 고치는 화면 넷(게시글 편집·메모 창·메모판·수업 일지 창)이 지켜야 할 규칙(2026-10-01 board 조사).
/// </summary>
public class BoardEditorRulesTests
{
    private static readonly string[] Editors =
    [
        "Board/Pages/PostEditPage.xaml.cs",
        "Board/Dialogs/MemoEditDialog.xaml.cs",
        "Board/Controls/MemoBoard.xaml.cs",
        "Dialogs/LessonJournalWindow.xaml.cs",
    ];

    /// <summary>
    /// <b>작성일시는 새 글일 때만 찍는다.</b> 게시글 편집·메모 쪽은 고칠 때마다 <c>DateTime.Now</c> 로 밀어
    /// 목록 날짜가 수정일이 됐고, 게시판에서 고친 수업 일지는 해 추정(<c>LessonJournalTitle.DateOf</c>)이
    /// 틀어졌다. 수업 일지 창만 <c>if (_isNew)</c> 로 지키고 있었다.
    /// </summary>
    [Fact]
    public void 고칠_때_작성일시를_밀지_않는다()
    {
        var assign = new Regex(@"\.DateTime\s*=\s*DateTime\.Now\s*;");
        var offenders = new List<string>();

        foreach (var rel in Editors)
        {
            string[] lines = File.ReadAllLines(Path.Combine(RepoRoot(), rel));
            for (int i = 0; i < lines.Length; i++)
            {
                string code = StripComment(lines[i]);
                if (assign.IsMatch(code) && !code.TrimStart().StartsWith("if "))
                    offenders.Add($"{rel}:{i + 1}");
            }
        }

        Assert.True(offenders.Count == 0,
            "작성일시를 조건 없이 지금으로 바꾸는 곳 — 새 글일 때만 찍을 것:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// <b>첨부를 받는 편집 창은 저장하지 않은 편집 판정에 첨부 변경을 센다.</b> 제목·본문만 보던 때는
    /// 파일만 붙이고 나가면 묻지 않고 사라졌다.
    /// </summary>
    [Fact]
    public void 미저장_판정이_첨부_변경을_센다()
    {
        var offenders = new List<string>();
        foreach (var rel in new[] { Editors[0], Editors[1], Editors[3] })
        {
            string code = File.ReadAllText(Path.Combine(RepoRoot(), rel));
            var m = Regex.Match(code, @"bool HasUnsavedWork\s*=>(?<body>[^;]*);");
            if (!m.Success || !m.Groups["body"].Value.Contains(".HasChanges"))
                offenders.Add(rel);
        }

        Assert.True(offenders.Count == 0,
            "HasUnsavedWork 가 첨부 목록의 HasChanges 를 보지 않는다:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// <b>게시판 상세의 [수정] 은 수업 일지를 전용 창으로 보낸다.</b> 일반 편집 페이지로 가면 제목 규칙이
    /// 깨져 시간표·진도표에서 그 일지를 못 알아본다.
    /// </summary>
    [Fact]
    public void 상세의_수정은_수업_일지를_전용_창으로_연다()
    {
        string code = File.ReadAllText(Path.Combine(RepoRoot(), "Board/Pages/PostDetailPage.xaml.cs"));
        var m = Regex.Match(code, @"void EditButton_Click\([^)]*\)\s*\{(?<body>.*?)Frame\.Navigate", RegexOptions.Singleline);

        Assert.True(m.Success, "EditButton_Click 을 찾지 못했다 — 검색이 빗나갔다");
        Assert.Contains("LessonJournalComposer.OpenPostAsync", m.Groups["body"].Value);
    }

    private static string StripComment(string line)
    {
        int comment = line.IndexOf("//", System.StringComparison.Ordinal);
        return comment >= 0 ? line[..comment] : line;
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

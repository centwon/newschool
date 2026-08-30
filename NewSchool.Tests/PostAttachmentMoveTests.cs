using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NewSchool.Board;
using NewSchool.Board.Services;
using NewSchool.Tests.Infrastructure;
using BoardApi = NewSchool.Board.Board;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 카테고리 이동 시 첨부 파일명 충돌 회귀 테스트 (2026-08-25, 40차).
///
/// 예전에는 대상 폴더에 같은 이름이 있으면 <b>조용히 건너뛰었다</b>. 경로를 만드는
/// <c>Board.GetFilePath</c> 는 언제나 글의 현재 카테고리를 쓰므로, 그 글의 첨부를 열면
/// 그 자리에 있던 <b>남의 파일</b>이 열리고 첨부를 지우면 그것이 지워졌다.
/// </summary>
public sealed class PostAttachmentMoveTests : IClassFixture<BoardTestFixture>, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ns_attach_{Guid.NewGuid():N}");
    private readonly string _originalDataDir = BoardApi.Data_Dir;
    private readonly BoardTestFixture _db;

    public PostAttachmentMoveTests(BoardTestFixture db)
    {
        _db = db;
        Directory.CreateDirectory(_root);
        BoardApi.Data_Dir = _root;   // 테스트는 순차 실행이라 전역 교체가 안전하다(AssemblyInfo 참고)
    }

    public void Dispose()
    {
        BoardApi.Data_Dir = _originalDataDir;
        try { Directory.Delete(_root, true); } catch { /* 임시 폴더 정리 실패 무시 */ }
    }

    private void WriteFile(string category, string fileName, string content)
    {
        BoardApi.EnsureCategoryDirectory(category);
        File.WriteAllText(BoardApi.GetFilePath(fileName, category), content);
    }

    [Fact]
    public async Task 이름이_겹치지_않으면_그대로_옮긴다()
    {
        WriteFile("수업", "20260825_143012_계획.hwp", "내 파일");

        string? renamed = null;
        bool ok = await PostAttachments.MoveToCategoryAsync(
            "20260825_143012_계획.hwp", "수업", "학급",
            n => { renamed = n; return Task.FromResult(true); });

        Assert.True(ok);
        Assert.Null(renamed);   // 이름을 바꿀 일이 없으니 DB 도 건드리지 않는다
        Assert.False(File.Exists(BoardApi.GetFilePath("20260825_143012_계획.hwp", "수업")));
        Assert.Equal("내 파일", File.ReadAllText(BoardApi.GetFilePath("20260825_143012_계획.hwp", "학급")));
    }

    [Fact]
    public async Task 이름이_겹치면_새_이름으로_옮기고_남의_파일을_건드리지_않는다()
    {
        WriteFile("수업", "20260825_143012_계획.hwp", "내 파일");
        WriteFile("학급", "20260825_143012_계획.hwp", "남의 파일");

        string? renamed = null;
        bool ok = await PostAttachments.MoveToCategoryAsync(
            "20260825_143012_계획.hwp", "수업", "학급",
            n => { renamed = n; return Task.FromResult(true); });

        Assert.True(ok);
        Assert.Equal("20260825_143012_계획 (2).hwp", renamed);

        // 남의 파일은 그대로, 내 파일은 새 이름으로 도착
        Assert.Equal("남의 파일", File.ReadAllText(BoardApi.GetFilePath("20260825_143012_계획.hwp", "학급")));
        Assert.Equal("내 파일", File.ReadAllText(BoardApi.GetFilePath(renamed!, "학급")));
        Assert.False(File.Exists(BoardApi.GetFilePath("20260825_143012_계획.hwp", "수업")));
    }

    [Fact]
    public async Task DB_이름_변경이_실패하면_파일을_옮기지_않는다()
    {
        // 실물만 옮기고 DB 가 옛 이름을 들고 있으면, 그 이름이 가리키는 남의 파일이 열린다.
        WriteFile("수업", "겹치는이름.txt", "내 파일");
        WriteFile("학급", "겹치는이름.txt", "남의 파일");

        bool ok = await PostAttachments.MoveToCategoryAsync(
            "겹치는이름.txt", "수업", "학급", _ => Task.FromResult(false));

        Assert.False(ok);
        Assert.Equal("내 파일", File.ReadAllText(BoardApi.GetFilePath("겹치는이름.txt", "수업")));
        Assert.Equal("남의 파일", File.ReadAllText(BoardApi.GetFilePath("겹치는이름.txt", "학급")));
        Assert.False(File.Exists(BoardApi.GetFilePath("겹치는이름 (2).txt", "학급")));
    }

    [Fact]
    public async Task 실물이_없으면_아무_일도_하지_않고_성공으로_친다()
    {
        bool ok = await PostAttachments.MoveToCategoryAsync(
            "없는파일.txt", "수업", "학급", _ => Task.FromResult(true));

        Assert.True(ok);   // 이 함수가 만든 문제가 아니다
    }

    [Fact]
    public void 빈_이름_찾기는_숫자를_차례로_올린다()
    {
        WriteFile("학급", "보고서.docx", "1");
        WriteFile("학급", "보고서 (2).docx", "2");

        Assert.Equal("보고서 (3).docx", PostAttachments.ResolveFreeName("보고서.docx", "학급"));
        Assert.Equal("안겹침.docx", PostAttachments.ResolveFreeName("안겹침.docx", "학급"));
    }

    // ── 글 한 편 통째로 옮기기 (MoveAllToCategoryAsync, 2026-08-30) ──────────
    //
    // 카테고리를 바꿀 수 있는 화면이 셋(게시글 편집·메모 편집 창·메모 보드)인데
    // 이 손질이 게시글 편집에만 있었다. 메모 쪽에서 카테고리를 바꾸면 첨부 실물이
    // 옛 폴더에 남아 조용히 끊겼다 — 이제 셋이 이 한 벌을 함께 쓴다.
    //
    // ⚠ 실패 경로는 여기서 시험하지 않는다. 실패하면 사용자에게 대화상자를 띄우는데
    //    테스트 호스트에는 UI 스레드가 없다. 아래는 모두 성공 경로다.

    /// <summary>글에 첨부와 댓글 첨부를 하나씩 달고 저장한 뒤, 그 글 번호를 낸다.</summary>
    private async Task<int> SeedPostWithAttachmentsAsync(
        BoardService svc, string category, string postFileName, string commentFileName)
    {
        int postNo = await svc.SavePostAsync(TestData.NewPost(category: category, title: "이사갈 글"));

        WriteFile(category, postFileName, "글 첨부");
        await svc.AddPostFileAsync(new PostFile
        {
            Post = postNo,
            FileName = postFileName,
            FileSize = 5,
            DateTime = DateTime.Now
        });

        WriteFile(category, commentFileName, "댓글 첨부");
        await svc.CreateCommentAsync(new Comment
        {
            Post = postNo,
            User = "테스트교사",
            DateTime = DateTime.Now,
            Content = "첨부 달린 댓글",
            HasFile = true,
            FileName = commentFileName,
            FileSize = 6
        });

        return postNo;
    }

    [Fact]
    public async Task 카테고리를_바꾸면_글_첨부와_댓글_첨부가_함께_따라간다()
    {
        using var svc = new BoardService(_db.DbPath);
        int postNo = await SeedPostWithAttachmentsAsync(svc, "수업", "이사_글.hwp", "이사_댓글.png");

        Assert.True(await PostAttachments.MoveAllToCategoryAsync(svc, postNo, "수업", "학급"));

        Assert.Equal("글 첨부", File.ReadAllText(BoardApi.GetFilePath("이사_글.hwp", "학급")));
        Assert.Equal("댓글 첨부", File.ReadAllText(BoardApi.GetFilePath("이사_댓글.png", "학급")));
        Assert.False(File.Exists(BoardApi.GetFilePath("이사_글.hwp", "수업")));
        Assert.False(File.Exists(BoardApi.GetFilePath("이사_댓글.png", "수업")));
    }

    /// <summary>
    /// 대상 폴더에 같은 이름이 있으면 새 이름으로 옮기고 <b>DB 도 그 이름으로</b> 고쳐야 한다.
    /// DB 가 옛 이름을 들고 있으면 그 글의 첨부를 열 때 남의 파일이 열린다.
    /// </summary>
    [Fact]
    public async Task 이름이_겹치면_DB의_첨부_이름까지_새_이름으로_바뀐다()
    {
        using var svc = new BoardService(_db.DbPath);
        int postNo = await SeedPostWithAttachmentsAsync(svc, "동아리", "겹칠글.hwp", "겹칠댓글.png");

        // 대상 폴더에 같은 이름의 남의 파일을 미리 둔다
        WriteFile("개인", "겹칠글.hwp", "남의 글 파일");
        WriteFile("개인", "겹칠댓글.png", "남의 댓글 파일");

        Assert.True(await PostAttachments.MoveAllToCategoryAsync(svc, postNo, "동아리", "개인"));

        // 남의 파일은 그대로 남아 있다
        Assert.Equal("남의 글 파일", File.ReadAllText(BoardApi.GetFilePath("겹칠글.hwp", "개인")));
        Assert.Equal("남의 댓글 파일", File.ReadAllText(BoardApi.GetFilePath("겹칠댓글.png", "개인")));

        // DB 가 가리키는 이름이 실제로 내 파일이어야 한다
        var savedFile = Assert.Single(await svc.GetPostFilesByPostAsync(postNo));
        Assert.Equal("겹칠글 (2).hwp", savedFile.FileName);
        Assert.Equal("글 첨부", File.ReadAllText(BoardApi.GetFilePath(savedFile.FileName, "개인")));

        var savedComment = Assert.Single(await svc.GetCommentsByPostAsync(postNo));
        Assert.Equal("겹칠댓글 (2).png", savedComment.FileName);
        Assert.Equal("댓글 첨부", File.ReadAllText(BoardApi.GetFilePath(savedComment.FileName, "개인")));
    }

    /// <summary>
    /// 옮길 일이 없으면 아무 것도 하지 않는다 — 부르는 쪽이 조건을 따로 걸지 않아도 되도록.
    /// </summary>
    [Theory]
    [InlineData(0, "수업", "학급")]      // 아직 저장 안 된 글
    [InlineData(1, "", "학급")]          // 옛 카테고리를 모른다(새 글)
    [InlineData(1, "수업", "수업")]      // 카테고리가 그대로
    public async Task 옮길_일이_없으면_손대지_않는다(int postNo, string oldCategory, string newCategory)
    {
        using var svc = new BoardService(_db.DbPath);
        WriteFile("수업", "그대로.txt", "안 움직임");

        Assert.True(await PostAttachments.MoveAllToCategoryAsync(svc, postNo, oldCategory, newCategory));

        Assert.Equal("안 움직임", File.ReadAllText(BoardApi.GetFilePath("그대로.txt", "수업")));
        Assert.False(Directory.Exists(Path.Combine(_root, "학급"))
                     && File.Exists(BoardApi.GetFilePath("그대로.txt", "학급")));
    }

    /// <summary>
    /// <b>글의 카테고리를 대입하는 화면은 첨부도 함께 옮겨야 한다.</b>
    ///
    /// <para>원래 결함은 이동 규칙이 틀린 게 아니라 <b>부르는 걸 빠뜨린 것</b>이었다 —
    /// 게시글 편집 페이지에만 있고 메모 쪽에는 없었다. 화면이 하나 늘거나 이 줄이 지워져도
    /// 컴파일은 되고 앱도 뜨므로, 소스로 대조해 고정한다.</para>
    /// </summary>
    [Theory]
    [InlineData("Board/Pages/PostEditPage.xaml.cs")]
    [InlineData("Board/Dialogs/MemoEditDialog.xaml.cs")]
    [InlineData("Board/Controls/MemoBoard.xaml.cs")]
    [InlineData("Dialogs/LessonJournalWindow.xaml.cs")]
    public void 카테고리를_바꾸는_화면은_모두_첨부_이동을_부른다(string relativePath)
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "NewSchool.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        var path = Path.Combine(dir!.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"소스를 찾지 못했다: {relativePath}");

        var source = File.ReadAllText(path);
        Assert.True(source.Contains("MoveAllToCategoryAsync"),
            $"{relativePath} 가 글의 카테고리를 대입하면서 첨부 이동을 부르지 않는다 — " +
            "카테고리를 바꾸면 첨부 실물이 옛 폴더에 남아 조용히 끊긴다.");
    }

    /// <summary>
    /// 이동 규칙은 한 벌만 있어야 한다. 화면마다 제 나름대로 옮기기 시작하면
    /// 이름 충돌 처리가 서로 어긋난다(그래서 PostAttachments 로 모았다).
    /// </summary>
    [Fact]
    public void 이동_규칙은_PostAttachments_한_곳에만_있다()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "NewSchool.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        var callers = Directory
            .EnumerateFiles(dir!.FullName, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}NewSchool.Tests{Path.DirectorySeparatorChar}"))
            .Where(p => File.ReadAllText(p).Contains("MoveToCategoryAsync(")
                     && !File.ReadAllText(p).Contains("MoveAllToCategoryAsync("))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(callers.Count == 0,
            "PostAttachments 밖에서 MoveToCategoryAsync 를 직접 부른다: " + string.Join(", ", callers));
    }
}

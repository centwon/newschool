using System;
using System.IO;
using System.Threading.Tasks;
using NewSchool.Board;
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
public sealed class PostAttachmentMoveTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ns_attach_{Guid.NewGuid():N}");
    private readonly string _originalDataDir = BoardApi.Data_Dir;

    public PostAttachmentMoveTests()
    {
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
}

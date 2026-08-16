using System;
using System.IO;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 포터블 판정(Settings.IsPortableLayout) 회귀.
///
/// 판정 기준을 "DB 가 어디 있느냐" 에서 표식 파일로 바꾼 지점이다. 옛 방식은
/// 동기화가 Settings.db 이름을 바꾸거나 파일이 사라지면 조용히 사용자 폴더 모드로 넘어가
/// 빈 화면을 띄웠다. 판정이 어긋나면 데이터가 통째로 안 보이므로 경계를 촘촘히 잡는다.
/// </summary>
public sealed class DataLayoutTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ns_layout_test_{Guid.NewGuid():N}");

    public DataLayoutTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* 임시 폴더 정리 실패 무시 */ }
    }

    private void WriteFile(string relative, string content = "x")
    {
        var path = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public void 표식_파일이_있으면_포터블()
    {
        WriteFile(Settings.PortableMarkerFileName);

        Assert.True(Settings.IsPortableLayout(_dir));
    }

    [Fact]
    public void 표식도_데이터도_없으면_설치본()
    {
        // 게시본을 새 폴더에 풀어 놓기만 한 상태 — 포터블로 단정하지 않는다.
        WriteFile("NewSchool.exe");

        Assert.False(Settings.IsPortableLayout(_dir));
    }

    [Fact]
    public void 옛_배치는_포터블로_인정하고_표식을_만들어_준다()
    {
        WriteFile("Settings.db");

        Assert.True(Settings.IsPortableLayout(_dir));
        Assert.True(File.Exists(Path.Combine(_dir, Settings.PortableMarkerFileName)));
    }

    [Fact]
    public void Data로_옮긴_폴더는_표식_없이도_포터블로_인정()
    {
        // 손으로 Data 폴더에 옮겨 놓기만 한 경우 — 표식까지 만들라고 요구하지 않는다.
        WriteFile(Path.Combine("Data", "Settings.db"));

        Assert.True(Settings.IsPortableLayout(_dir));
        Assert.True(File.Exists(Path.Combine(_dir, Settings.PortableMarkerFileName)));
    }

    [Fact]
    public void 데이터가_사라져도_표식만_있으면_포터블_유지()
    {
        // 옛 방식이 무너지던 지점: DB 가 없다고 사용자 폴더로 넘어가면 안 된다.
        WriteFile(Settings.PortableMarkerFileName);
        Assert.False(File.Exists(Path.Combine(_dir, "Data", "Settings.db")));

        Assert.True(Settings.IsPortableLayout(_dir));
    }
}

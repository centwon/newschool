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

    // ──────────────────────────────────────────────────────────────
    // 실행 파일이 {app}\bin\ 아래로 내려간 배치 (자체 포함 게시본)
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void 루트에_표식이_있으면_bin_안의_exe_도_루트를_포터블_루트로_본다()
    {
        // 설치 폴더를 통째로 옮긴 경우. Data\ 는 루트에 그대로 있어야 하고,
        // 부모를 보지 않으면 사용자가 Data\ 를 bin\ 안으로 밀어 넣어야 했다.
        WriteFile(Settings.PortableMarkerFileName);
        var binDir = Path.Combine(_dir, "bin");
        Directory.CreateDirectory(binDir);

        Assert.Equal(_dir, Settings.FindPortableRoot(binDir));
    }

    [Fact]
    public void 루트의_옛_배치도_bin_안의_exe_에서_알아본다()
    {
        WriteFile(Path.Combine("Data", "Settings.db"));
        var binDir = Path.Combine(_dir, "bin");
        Directory.CreateDirectory(binDir);

        Assert.Equal(_dir, Settings.FindPortableRoot(binDir));
    }

    [Fact]
    public void exe_폴더의_표식이_부모보다_우선한다()
    {
        // 둘 다 있으면 가까운 쪽 — 기존 배치(실행 파일 옆에 데이터)의 동작을 그대로 지킨다.
        WriteFile(Settings.PortableMarkerFileName);
        var binDir = Path.Combine(_dir, "bin");
        Directory.CreateDirectory(binDir);
        File.WriteAllText(Path.Combine(binDir, Settings.PortableMarkerFileName), "x");

        Assert.Equal(binDir, Settings.FindPortableRoot(binDir));
    }

    [Fact]
    public void 표식이_어디에도_없으면_설치본()
    {
        var binDir = Path.Combine(_dir, "bin");
        Directory.CreateDirectory(binDir);
        File.WriteAllText(Path.Combine(binDir, "NewSchool.exe"), "x");

        Assert.Null(Settings.FindPortableRoot(binDir));
    }

    [Fact]
    public void 할아버지_폴더까지_거슬러_올라가지는_않는다()
    {
        // 한 단계만 본다 — 무한정 올라가면 엉뚱한 상위 폴더를 데이터 루트로 잡을 수 있다.
        WriteFile(Settings.PortableMarkerFileName);
        var deep = Path.Combine(_dir, "bin", "sub");
        Directory.CreateDirectory(deep);

        Assert.Null(Settings.FindPortableRoot(deep));
    }
}

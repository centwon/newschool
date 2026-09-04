using System;
using System.IO;
using NewSchool.Helpers;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 파일이 막혔을 때 <b>사용자에게 무엇을 말하는가</b> — 49차(권한·경로 축).
///
/// <para>예전에는 .NET 예외 문구를 그대로 보여 줬다. "다른 프로세스에서 사용 중이므로
/// 프로세스가 파일에 액세스할 수 없습니다" 는 무슨 일이 일어났는지는 적혀 있지만
/// <b>무엇을 하면 되는지가 없다</b> — 대부분은 그 파일을 엑셀에서 닫으면 끝나는 일이었다.
/// 이 표가 갈리면 교사는 다시 예외 문구를 읽게 되므로 여기서 못박는다.</para>
/// </summary>
public class FileErrorTextTests
{
    /// <summary>HRESULT 는 <c>0x8007</c> + Win32 오류 번호 꼴이다.</summary>
    private static IOException Win32Io(int code)
        => new("원문", unchecked((int)(0x80070000 | (uint)code)));

    /// <summary>
    /// <b>가장 흔한 경우</b> — 내보낸 엑셀을 열어 둔 채 다시 내보내기.
    /// 파일을 닫으라는 말이 반드시 있어야 한다.
    /// </summary>
    [Theory]
    [InlineData(32)]   // ERROR_SHARING_VIOLATION
    [InlineData(33)]   // ERROR_LOCK_VIOLATION
    public void 파일이_열려_있으면_닫으라고_말한다(int win32Code)
    {
        var text = FileErrorText.Explain(Win32Io(win32Code));

        Assert.NotNull(text);
        Assert.Contains("다른 프로그램에서 열려", text);
        Assert.Contains("닫고", text);
    }

    [Theory]
    [InlineData(39)]    // ERROR_HANDLE_DISK_FULL
    [InlineData(112)]   // ERROR_DISK_FULL
    public void 공간이_부족하면_정리하라고_말한다(int win32Code)
    {
        var text = FileErrorText.Explain(Win32Io(win32Code));

        Assert.NotNull(text);
        Assert.Contains("공간이 부족", text);
    }

    /// <summary>권한은 형(型)으로도, Win32 번호(5)로도 온다 — 두 길 다 같은 말을 해야 한다.</summary>
    [Fact]
    public void 권한이_없으면_형이든_번호든_같은_말을_한다()
    {
        var byType = FileErrorText.Explain(new UnauthorizedAccessException("원문"));
        var byCode = FileErrorText.Explain(Win32Io(5));   // ERROR_ACCESS_DENIED

        Assert.NotNull(byType);
        Assert.Contains("쓸 권한이 없습니다", byType);
        Assert.Equal(byType, byCode);
    }

    /// <summary>경로 길이도 두 길로 온다(PathTooLongException, ERROR_FILENAME_EXCED_RANGE).</summary>
    [Fact]
    public void 경로가_길면_줄이라고_말한다()
    {
        var byType = FileErrorText.Explain(new PathTooLongException("원문"));
        var byCode = FileErrorText.Explain(Win32Io(206));

        Assert.NotNull(byType);
        Assert.Contains("너무 깁니다", byType);
        Assert.Equal(byType, byCode);
    }

    [Fact]
    public void 폴더나_파일이_없으면_그렇게_말한다()
    {
        Assert.Contains("폴더를 찾을 수 없습니다",
            FileErrorText.Explain(new DirectoryNotFoundException("원문")));
        Assert.Contains("파일을 찾을 수 없습니다",
            FileErrorText.Explain(new FileNotFoundException("원문")));
    }

    /// <summary>
    /// <b>규칙: 할 말이 없으면 지어내지 않는다.</b> null 을 돌려주면 부르는 쪽이 원문을 쓴다
    /// — 엉뚱한 안내를 하는 것보다 원문이 낫다.
    /// </summary>
    [Fact]
    public void 알_수_없는_예외에는_아무_말도_하지_않는다()
    {
        Assert.Null(FileErrorText.Explain(new InvalidOperationException("원문")));
        Assert.Null(FileErrorText.Explain(new IOException("번호 없는 입출력 오류")));
        Assert.Null(FileErrorText.Explain(null));
    }

    /// <summary>Win32 에서 온 것이 아닌 HRESULT 를 오류 번호로 착각하면 안 된다.</summary>
    [Fact]
    public void Win32_가_아닌_HRESULT_는_해석하지_않는다()
    {
        // 0x80040020 — 하위 16비트는 32(공유 위반)와 같지만 Win32 가 아니다.
        var notWin32 = new IOException("원문", unchecked((int)0x80040020));

        Assert.Null(FileErrorText.Explain(notWin32));
    }
}

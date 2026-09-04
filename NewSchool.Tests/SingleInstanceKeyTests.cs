using NewSchool.Helpers;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 단일 인스턴스 잠금을 <b>무엇으로 가르는가</b> — 데이터 폴더다.
///
/// <para>같은 폴더를 보는 두 프로그램이 함께 뜨면 설정이 서로를 덮는다
/// (<c>SettingsDb</c> 가 설정을 프로세스 메모리에 캐시한다). 반대로 <b>다른</b> 폴더를 보는
/// 설치본과 포터블본까지 막으면, 자료를 옮겨 보려고 둘을 나란히 띄우는 정상적인 사용이 막힌다.
/// 그 경계를 이 테스트가 못박는다.</para>
/// </summary>
public class SingleInstanceKeyTests
{
    [Fact]
    public void 같은_폴더는_같은_잠금이다()
        => Assert.Equal(
            SingleInstance.KeyFor(@"C:\Users\사람\NewSchool\Data"),
            SingleInstance.KeyFor(@"C:\Users\사람\NewSchool\Data"));

    /// <summary>설치본(사용자 폴더)과 포터블본(실행 파일 옆)은 서로를 막지 않는다.</summary>
    [Fact]
    public void 다른_폴더는_다른_잠금이다()
        => Assert.NotEqual(
            SingleInstance.KeyFor(@"C:\Users\사람\NewSchool\Data"),
            SingleInstance.KeyFor(@"D:\포터블\NewSchool\Data"));

    /// <summary>
    /// 같은 폴더를 다르게 적었다고 잠금이 갈리면 안 된다 — 그러면 두 개가 떠 버린다.
    /// 윈도우 경로는 대소문자를 가리지 않고, 끝의 구분자도 있으나 없으나 같은 곳이다.
    /// </summary>
    [Theory]
    [InlineData(@"C:\Users\사람\NewSchool\Data", @"c:\users\사람\newschool\data")]
    [InlineData(@"C:\Users\사람\NewSchool\Data", @"C:\Users\사람\NewSchool\Data\")]
    [InlineData(@"C:\Users\사람\NewSchool\Data", @"  C:\Users\사람\NewSchool\Data  ")]
    public void 같은_폴더를_다르게_적어도_같은_잠금이다(string a, string b)
        => Assert.Equal(SingleInstance.KeyFor(a), SingleInstance.KeyFor(b));

    /// <summary>잠금 이름에 쓰이므로 경로가 무엇이든 짧고 안전한 글자만 나와야 한다.</summary>
    [Theory]
    [InlineData(@"C:\Users\사람\NewSchool\Data")]
    [InlineData(@"\\서버\공유 폴더\NewSchool\Data")]
    [InlineData("")]
    public void 잠금_이름은_길이가_고정된_16자_16진수다(string path)
    {
        var key = SingleInstance.KeyFor(path);

        Assert.Equal(16, key.Length);
        Assert.All(key, c => Assert.Contains(c, "0123456789ABCDEF"));
    }
}

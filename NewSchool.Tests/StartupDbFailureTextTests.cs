using NewSchool.Helpers;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 시작할 때 데이터베이스를 준비하지 못하면 <b>무엇을 말하는가</b> — 50차(조용한 실패 축).
///
/// <para>예전에는 세 DB 초기화 결과를 전부 버렸다. 폴더가 읽기 전용이거나 공간이 없어
/// 테이블을 만들지 못해도 앱은 그대로 떴고, 화면마다 "자료가 하나도 없음" 으로 보였다.
/// 그 위에 새로 입력하면 예전 자료와 어긋난다. 손상 검사(<c>DbIntegrity</c>)에는 걸리지
/// 않는 실패라, 여기서 막지 않으면 아무도 못 잡는다.</para>
///
/// <para>알림 자체는 창을 띄우는 일이라 시험할 수 없으므로, <b>판정과 문구만</b> 갈라
/// 두고 그 부분을 못박는다(47차 <c>Interpret</c>, 48차 <c>ContainsRestorableDb</c> 와 같은 수법).</para>
/// </summary>
public class StartupDbFailureTextTests
{
    private const string DataPath = @"C:\Users\teacher\Documents\NewSchool";

    /// <summary>모두 성공하면 알릴 것이 없다 — 여기서 문구가 나오면 정상 시작을 막는다.</summary>
    [Fact]
    public void 실패가_없으면_아무_말도_하지_않는다()
    {
        Assert.Null(StartupDbFailureText.Describe(System.Array.Empty<string>(), DataPath));
        Assert.Null(StartupDbFailureText.Describe(null!, DataPath));
    }

    /// <summary>빈 이름이 섞여 들어와도 그것만으로 "실패했다" 고 말하지 않는다.</summary>
    [Fact]
    public void 빈_이름은_실패로_세지_않는다()
    {
        Assert.Null(StartupDbFailureText.Describe(new[] { "", "   " }, DataPath));
    }

    [Fact]
    public void 실패한_것의_이름과_폴더를_말한다()
    {
        var text = StartupDbFailureText.Describe(new[] { StartupDbFailureText.School }, DataPath);

        Assert.NotNull(text);
        Assert.Contains("학생 정보", text);
        Assert.Contains(DataPath, text);
    }

    /// <summary>
    /// 이 안내의 핵심은 <b>"자료가 없는 것이 아니다"</b> 를 알리는 것이다.
    /// 그 말이 빠지면 사용자는 자료가 날아간 줄 알고 백업 복원부터 시도한다.
    /// </summary>
    [Fact]
    public void 계속하면_어긋난다는_것과_할_일을_말한다()
    {
        var text = StartupDbFailureText.Describe(new[] { StartupDbFailureText.Board }, DataPath)!;

        Assert.Contains("자료가 하나도 없는 것처럼", text);
        Assert.Contains("권한", text);
        Assert.Contains("백업", text);
    }

    [Fact]
    public void 여러_개가_실패하면_모두_적고_한_번만_적는다()
    {
        var text = StartupDbFailureText.Describe(
            new[] { StartupDbFailureText.Board, StartupDbFailureText.Scheduler, StartupDbFailureText.Board },
            DataPath)!;

        Assert.Contains("게시판·일정", text);   // 중복은 접고 순서는 그대로
        Assert.DoesNotContain("게시판·일정·게시판", text);
    }

    /// <summary>
    /// 원인을 사람 말로 옮긴 것이 있으면 함께 보여 준다(49차 <see cref="FileErrorText"/>).
    /// 없으면 그 자리를 비워 두고, "무엇을 하면 되는지" 는 그대로 남는다.
    /// </summary>
    [Fact]
    public void 원인을_알면_함께_적고_모르면_생략한다()
    {
        var withCause = StartupDbFailureText.Describe(
            new[] { StartupDbFailureText.School }, DataPath, "폴더에 쓸 권한이 없습니다.")!;
        var without = StartupDbFailureText.Describe(
            new[] { StartupDbFailureText.School }, DataPath)!;

        Assert.Contains("폴더에 쓸 권한이 없습니다.", withCause);
        Assert.DoesNotContain("폴더에 쓸 권한이 없습니다.", without);
        Assert.Contains("백업", without);
    }
}

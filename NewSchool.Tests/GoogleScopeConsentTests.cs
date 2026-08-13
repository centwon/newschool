using NewSchool.Google;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 구글 부분 동의(granular consent) 회귀 테스트.
///
/// 세분화 스코프를 여러 개 요청하면 구글 동의 화면에 체크박스가 항목별로 뜨고,
/// 사용자가 일부만 허용할 수 있다. 그대로 토큰을 저장하면 앱은 "연동됨"인데
/// 동기화만 403 으로 조용히 실패한다 — 사용자는 이유를 알 길이 없다.
/// 그래서 토큰 교환 응답의 <c>scope</c> 를 검사해 부족하면 연동을 성립시키지 않는다.
/// </summary>
public class GoogleScopeConsentTests
{
    private const string CalendarList = "https://www.googleapis.com/auth/calendar.calendarlist.readonly";
    private const string Calendars = "https://www.googleapis.com/auth/calendar.calendars";
    private const string Events = "https://www.googleapis.com/auth/calendar.events";
    private const string FullCalendar = "https://www.googleapis.com/auth/calendar";

    [Fact]
    public void 세_스코프가_모두_허용되면_누락_없음()
    {
        var missing = GoogleAuthService.FindMissingScopes($"{CalendarList} {Calendars} {Events}");
        Assert.Empty(missing);
    }

    [Fact]
    public void 순서가_달라도_누락_없음()
    {
        // 구글은 요청 순서대로 돌려준다는 보장이 없다
        var missing = GoogleAuthService.FindMissingScopes($"{Events} {CalendarList} {Calendars}");
        Assert.Empty(missing);
    }

    [Fact]
    public void 이벤트_권한만_빠지면_그것만_보고한다()
    {
        var missing = GoogleAuthService.FindMissingScopes($"{CalendarList} {Calendars}");
        Assert.Equal([Events], missing);
    }

    [Fact]
    public void 여러_개가_빠지면_전부_보고한다()
    {
        var missing = GoogleAuthService.FindMissingScopes(CalendarList);
        Assert.Equal([Calendars, Events], missing);
    }

    [Fact]
    public void 전체_권한은_세분화_3종을_포함하므로_통과()
    {
        // 전체 권한 스코프를 요청하던 시절에 연동한 사용자가 재연동할 때
        // 구글이 상위 스코프만 돌려줄 수 있다. 이걸 부분 동의로 오판하면
        // 멀쩡한 기존 사용자의 연동이 거부된다.
        var missing = GoogleAuthService.FindMissingScopes(FullCalendar);
        Assert.Empty(missing);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void scope_필드가_없으면_판단_보류하고_통과(string? granted)
    {
        // 판단 근거가 없는데 거부하면 연동 자체가 막힌다.
        // 실제 권한 부족은 API 호출 시 403 으로 드러난다.
        var missing = GoogleAuthService.FindMissingScopes(granted);
        Assert.Empty(missing);
    }

    [Fact]
    public void 접두사가_같은_다른_스코프는_인정하지_않는다()
    {
        // calendar.events.readonly 는 calendar.events 가 아니다.
        // 문자열 Contains 로 검사하면 이걸 통과시켜 버린다.
        var missing = GoogleAuthService.FindMissingScopes(
            $"{CalendarList} {Calendars} https://www.googleapis.com/auth/calendar.events.readonly");
        Assert.Equal([Events], missing);
    }
}

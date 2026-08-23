using System;
using NewSchool.Google;
using NewSchool.Scheduler;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 구글로 올리는 이벤트 본문 회귀 테스트.
///
/// 수정 Push 는 PUT(통째 대체) 이 아니라 <b>PATCH(부분 갱신)</b> 을 쓴다. PUT 은 우리가
/// 모델링하지 않은 필드(참석자·알림·Meet 링크·첨부…)를 전부 지워 버렸다.
/// PATCH 로 바꾸면 <b>보내지 않은 필드는 그대로 남는다</b>. 직렬화 옵션이
/// <c>WhenWritingNull</c> 이라 null 은 아예 전송되지 않으므로, 이제 null 과 빈 문자열의
/// 뜻이 갈린다 — null 은 "건드리지 마라", 빈 문자열은 "지워라".
///
/// 그래서 앱이 값을 소유하는 내용·장소는 비었을 때 <b>빈 문자열</b>이어야 한다
/// (null 로 두면 앱에서 지운 내용이 구글에 그대로 남는다).
/// </summary>
public class GooglePushPayloadTests
{
    private static KEvent Sample() => new()
    {
        Title = "학년 협의회",
        Start = new DateTime(2026, 8, 24, 14, 0, 0),
        End = new DateTime(2026, 8, 24, 15, 0, 0),
        Status = "confirmed"
    };

    [Fact]
    public void 내용이_비면_빈문자열로_보낸다()
    {
        var ev = Sample();
        ev.Notes = string.Empty;

        var ge = GoogleSyncService.ConvertToGoogleEvent(ev);

        Assert.Equal(string.Empty, ge.Description);
    }

    [Fact]
    public void 장소가_비면_빈문자열로_보낸다()
    {
        var ev = Sample();
        ev.Location = string.Empty;

        var ge = GoogleSyncService.ConvertToGoogleEvent(ev);

        Assert.Equal(string.Empty, ge.Location);
    }

    [Fact]
    public void 내용과_장소는_값이_있으면_그대로_보낸다()
    {
        var ev = Sample();
        ev.Notes = "안건 정리";
        ev.Location = "3층 회의실";

        var ge = GoogleSyncService.ConvertToGoogleEvent(ev);

        Assert.Equal("안건 정리", ge.Description);
        Assert.Equal("3층 회의실", ge.Location);
    }

    /// <summary>
    /// 색만은 빈 문자열이 유효한 값이 아니다(구글이 거부한다). null 로 남겨 전송에서 빠지게 한다
    /// — 앱에서 색을 지워도 구글 쪽 색은 유지된다.
    /// </summary>
    [Fact]
    public void 색이_비면_보내지_않는다()
    {
        var ev = Sample();
        ev.ColorId = string.Empty;

        var ge = GoogleSyncService.ConvertToGoogleEvent(ev);

        Assert.Null(ge.ColorId);
    }

    [Fact]
    public void 종일_일정의_끝날짜는_하루_더해_보낸다()
    {
        var ev = Sample();
        ev.IsAllday = true;
        ev.Start = new DateTime(2026, 8, 24);
        ev.End = new DateTime(2026, 8, 26);   // 로컬은 inclusive

        var ge = GoogleSyncService.ConvertToGoogleEvent(ev);

        Assert.Equal("2026-08-24", ge.Start?.Date);
        Assert.Equal("2026-08-27", ge.End?.Date);   // 구글은 exclusive
    }
}

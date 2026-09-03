using System;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 설정을 <b>읽어 들일 때</b>의 규칙 — 47차(설정 축)에서 못박았다.
///
/// <para>예전에는 저장된 값이 없으면 현재 값을 그대로 뒀다. 그래서 [설정 초기화]가
/// (DB 를 비우고 <c>LoadAll</c> 을 부르는데도) 실행 중인 앱의 설정을 하나도 되돌리지 못했다 —
/// 화면은 옛 값을 그대로 다시 그리면서 "초기화되었습니다" 라고 알렸고, 그 뒤 설정 하나만
/// 저장돼도(창 크기를 끌기만 해도) 옛 값이 빈 DB 에 되살아났다. 복원도 같은 함수를 쓰므로
/// 백업에 없던 키가 현재 값으로 남았다.</para>
///
/// <para>깨진 값도 마찬가지로 기본값으로 떨어뜨린다. 예전에는 <c>int.Parse</c> 가 그대로 터져
/// <c>Settings.Initialize</c> → <c>OnLaunched</c>(async void) 로 예외가 새어 나가, 값 하나가
/// 깨졌을 뿐인데 앱이 안내도 없이 죽었다(시작 시 무결성 점검은 파일 손상만 본다).</para>
/// </summary>
public class SettingReloadTests
{
    private static SettingProperty<int> IntProperty(int defaultValue = 7)
        => new("TestInt", defaultValue, int.Parse, i => i.ToString());

    private static SettingProperty<string> StringProperty(string defaultValue = "school.db")
        => new("TestString", defaultValue, s => s, s => s);

    /// <summary>저장된 값이 있으면 그 값을 읽는다(평소 경로).</summary>
    [Fact]
    public void 저장된_값이_있으면_그_값을_읽는다()
    {
        Assert.Equal(2026, IntProperty().Interpret("2026"));
        Assert.Equal("board.db", StringProperty().Interpret("board.db"));
    }

    /// <summary>
    /// <b>규칙: 값이 없으면 기본값으로 돌아간다.</b> 그 전에 무엇을 들고 있었든 상관없다
    /// — 이것이 [설정 초기화]가 실제로 초기화되게 하는 한 줄이다.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void 값이_없으면_기본값으로_돌아간다(string? stored)
    {
        var prop = IntProperty(defaultValue: 7);
        prop.Value = 1999;                       // 사용자가 바꿔 놓은 상태

        Assert.Equal(7, prop.Interpret(stored)); // DB 를 비운 뒤 = 기본값
    }

    /// <summary>기본값이 비어 있지 않은 설정(DB 파일 이름 등)도 같은 규칙을 탄다.</summary>
    [Fact]
    public void 값이_없으면_문자열_설정도_기본값으로_돌아간다()
    {
        var prop = StringProperty(defaultValue: "school.db");
        prop.Value = "다른이름.db";

        Assert.Equal("school.db", prop.Interpret(null));
    }

    /// <summary><b>규칙: 읽을 수 없는 값은 앱을 죽이지 않고 기본값이 된다.</b></summary>
    [Theory]
    [InlineData("abc")]
    [InlineData("2026년")]
    [InlineData("9999999999999999999")]   // int 범위를 넘음
    public void 깨진_값은_기본값이_된다(string stored)
    {
        var prop = IntProperty(defaultValue: 7);

        var ex = Record.Exception(() => Assert.Equal(7, prop.Interpret(stored)));

        Assert.Null(ex);   // 예외가 새어 나가면 앱이 시작하지 못한다
    }

    /// <summary>
    /// 문자열 설정에는 "깨진 값"이 없다 — 파서가 그대로 돌려주므로 무엇이 들어 있든 그 값이다.
    /// (빈 값만 기본값으로 간다.)
    /// </summary>
    [Fact]
    public void 문자열_설정은_어떤_값이든_그대로_읽는다()
    {
        Assert.Equal("!@#$", StringProperty().Interpret("!@#$"));
    }
}

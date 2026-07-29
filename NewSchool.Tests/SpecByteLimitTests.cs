using System.Collections.Generic;
using NewSchool;
using NewSchool.Helpers;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 학생부 영역 정의표 + 학년도별 바이트 한도 오버라이드 규칙.
/// 지침이 학년도마다 바뀌므로 "해당 학년도 → 학년도 무관 → 코드 기본값" 우선순위가 핵심이다.
/// </summary>
public class SpecByteLimitTests
{
    #region 정의표 (NeisHelper.Areas)

    [Fact]
    public void Areas_KeysAreUnique()
    {
        var keys = new HashSet<string>();
        foreach (var a in NeisHelper.Areas)
            Assert.True(keys.Add(a.Key), $"중복 영역 키: {a.Key}");
    }

    [Fact]
    public void Areas_AllHavePositiveDefault()
    {
        foreach (var a in NeisHelper.Areas)
            Assert.True(a.DefaultBytes > 0, $"{a.Key} 기본 바이트가 0 이하");
    }

    [Fact]
    public void GetMaxBytes_ReturnsTableValue()
    {
        foreach (var a in NeisHelper.Areas)
            Assert.Equal(a.DefaultBytes, NeisHelper.GetMaxBytes(a.Key));
    }

    /// <summary>내보내기 필터에만 있고 한도표에는 빠져 있던 영역(회귀 방지).</summary>
    [Fact]
    public void Areas_Includes봉사활동()
    {
        Assert.Contains(NeisHelper.Areas, a => a.Key == "봉사활동");
    }

    [Fact]
    public void GetMaxBytes_UnknownType_FallsBackToDefault()
    {
        Assert.Equal(1500, NeisHelper.GetMaxBytes("존재하지않는영역"));
    }

    #endregion

    #region 한도 경계 (IsOverLimit) — "이하 허용"

    /// <summary>
    /// 경계 규칙: 한도와 정확히 같은 바이트는 초과가 아니다(500자=1500바이트를 온전히 사용).
    /// 2026-07-30 사용자 확인으로 확정한 의도된 동작 — 바꾸려면 이 테스트부터 고쳐야 한다.
    /// </summary>
    [Theory]
    [InlineData(1499, 1500, false)]
    [InlineData(1500, 1500, false)]   // 딱 채운 경우는 정상
    [InlineData(1501, 1500, true)]    // 1바이트만 넘어도 초과
    [InlineData(0, 1500, false)]
    [InlineData(2100, 2100, false)]
    [InlineData(2101, 2100, true)]
    public void IsOverLimit_BoundaryIsInclusive(int byteCount, int maxBytes, bool expectedOver)
    {
        Assert.Equal(expectedOver, NeisHelper.IsOverLimit(byteCount, maxBytes));
    }

    /// <summary>한글 500자를 꽉 채우면 1500바이트이고, 초과로 판정되지 않아야 한다.</summary>
    [Fact]
    public void IsOverLimit_Exactly500KoreanChars_NotOver()
    {
        string text = new string('가', 500);
        int bytes = NeisHelper.CountByte(text);
        Assert.Equal(1500, bytes);
        Assert.False(NeisHelper.IsOverLimit(bytes, 1500));
        Assert.True(NeisHelper.IsOverLimit(NeisHelper.CountByte(text + "가"), 1500));
    }

    #endregion

    #region 우선순위 (ResolveSpecMaxBytes)

    [Fact]
    public void Resolve_Empty_UsesCodeDefault()
    {
        Assert.Equal(2100, Settings.ResolveSpecMaxBytes("", "진로활동", 2026));
    }

    [Fact]
    public void Resolve_LegacyGlobalEntry_AppliesToAllYears()
    {
        // 구버전 설정(학년도 없음)은 모든 학년도에 적용돼야 한다
        const string raw = "진로활동=1800";
        Assert.Equal(1800, Settings.ResolveSpecMaxBytes(raw, "진로활동", 2025));
        Assert.Equal(1800, Settings.ResolveSpecMaxBytes(raw, "진로활동", 2026));
        Assert.Equal(1800, Settings.ResolveSpecMaxBytes(raw, "진로활동", 0));
    }

    [Fact]
    public void Resolve_YearSpecific_BeatsGlobal()
    {
        const string raw = "진로활동=1800;2026:진로활동=2100";
        Assert.Equal(2100, Settings.ResolveSpecMaxBytes(raw, "진로활동", 2026));
        Assert.Equal(1800, Settings.ResolveSpecMaxBytes(raw, "진로활동", 2025));   // 지난 학년도는 그대로
    }

    [Fact]
    public void Resolve_YearSpecificOnly_OtherYearsUseCodeDefault()
    {
        // 올해만 바꿔도 지난 학년도 기록이 갑자기 "초과"로 바뀌면 안 된다
        const string raw = "2026:진로활동=900";
        Assert.Equal(900, Settings.ResolveSpecMaxBytes(raw, "진로활동", 2026));
        Assert.Equal(2100, Settings.ResolveSpecMaxBytes(raw, "진로활동", 2025));
    }

    [Fact]
    public void Resolve_OtherTypesUnaffected()
    {
        const string raw = "2026:진로활동=900";
        Assert.Equal(1500, Settings.ResolveSpecMaxBytes(raw, "자율활동", 2026));
    }

    [Theory]
    [InlineData("깨진값")]
    [InlineData("진로활동=")]
    [InlineData("진로활동=0")]
    [InlineData("진로활동=-5")]
    [InlineData("=1500")]
    public void Resolve_MalformedEntry_Ignored(string raw)
    {
        Assert.Equal(2100, Settings.ResolveSpecMaxBytes(raw, "진로활동", 2026));
    }

    #endregion

    #region 저장 (ApplySpecByteOverride)

    [Fact]
    public void Apply_DefaultValue_RemovesEntry()
    {
        var raw = Settings.ApplySpecByteOverride("진로활동=1800", "진로활동", 2100, 0);
        Assert.Equal("", raw);
    }

    [Fact]
    public void Apply_NonDefault_StoresGlobalEntry()
    {
        var raw = Settings.ApplySpecByteOverride("", "진로활동", 1800, 0);
        Assert.Equal("진로활동=1800", raw);
        Assert.Equal(1800, Settings.ResolveSpecMaxBytes(raw, "진로활동", 2026));
    }

    [Fact]
    public void Apply_YearSpecific_StoresWithYearPrefix()
    {
        var raw = Settings.ApplySpecByteOverride("", "진로활동", 900, 2026);
        Assert.Equal("2026:진로활동=900", raw);
    }

    [Fact]
    public void Apply_Replaces_SameYearAndType()
    {
        var raw = Settings.ApplySpecByteOverride("2026:진로활동=900", "진로활동", 1200, 2026);
        Assert.Equal("2026:진로활동=1200", raw);
    }

    /// <summary>
    /// 학년도 지정값이 코드 기본값과 같더라도, 학년도 무관 오버라이드가 다른 값이면
    /// 항목을 남겨야 "그 학년도만 기본값으로" 라는 의도가 유지된다.
    /// </summary>
    [Fact]
    public void Apply_YearSpecificEqualToCodeDefault_KeptWhenGlobalDiffers()
    {
        var raw = Settings.ApplySpecByteOverride("진로활동=1800", "진로활동", 2100, 2026);
        Assert.Contains("2026:진로활동=2100", raw);
        Assert.Equal(2100, Settings.ResolveSpecMaxBytes(raw, "진로활동", 2026));
        Assert.Equal(1800, Settings.ResolveSpecMaxBytes(raw, "진로활동", 2025));
    }

    [Fact]
    public void Apply_YearSpecificEqualToFallback_RemovedWhenNoGlobal()
    {
        var raw = Settings.ApplySpecByteOverride("2026:진로활동=900", "진로활동", 2100, 2026);
        Assert.Equal("", raw);
    }

    [Fact]
    public void Apply_DoesNotDisturbOtherEntries()
    {
        var raw = Settings.ApplySpecByteOverride("자율활동=1200;2026:진로활동=900", "진로활동", 1000, 2026);
        Assert.Contains("자율활동=1200", raw);
        Assert.Equal(1000, Settings.ResolveSpecMaxBytes(raw, "진로활동", 2026));
        Assert.Equal(1200, Settings.ResolveSpecMaxBytes(raw, "자율활동", 2026));
    }

    #endregion
}

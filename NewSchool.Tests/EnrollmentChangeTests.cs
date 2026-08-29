using System;
using NewSchool.Models;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 학적 변동 판정 회귀.
///
/// <para>코드가 학적을 두고 묻는 질문은 <see cref="EnrollmentChange.IsActive"/> 하나다 —
/// 이 학생이 명단에 들어가는가. 여기가 어긋나면 전출한 학생이 명렬표·좌석표·수업·동아리에
/// 계속 끼거나, 반대로 멀쩡한 학생이 통째로 사라진다. 후자가 훨씬 나쁘므로
/// <b>모르는 값은 명단에 넣는다</b>는 것도 함께 고정해 둔다.</para>
///
/// <para>설계 근거는 <c>docs/enrollment-redesign.md</c>.</para>
/// </summary>
public sealed class EnrollmentChangeTests
{
    [Theory]
    [InlineData(EnrollmentChange.Admitted)]       // 입학
    [InlineData(EnrollmentChange.Promoted)]       // 진급
    [InlineData(EnrollmentChange.TransferredIn)]  // 전입 — 이 학교로 왔다
    public void 명단에_들어가는_변동(string changeType)
    {
        Assert.True(EnrollmentChange.IsActive(changeType));
    }

    [Theory]
    [InlineData(EnrollmentChange.TransferredOut)] // 전출 — 다른 학교로 갔다
    [InlineData(EnrollmentChange.Graduated)]
    [InlineData(EnrollmentChange.OnLeave)]        // 휴학
    [InlineData(EnrollmentChange.Deferred)]       // 유예
    [InlineData(EnrollmentChange.OutOfQuota)]     // 정원외
    [InlineData(EnrollmentChange.Withdrawn)]
    [InlineData(EnrollmentChange.Expelled)]
    public void 명단에서_빠지는_변동(string changeType)
    {
        Assert.False(EnrollmentChange.IsActive(changeType));
    }

    [Fact]
    public void 전입과_전출은_정반대다()
    {
        Assert.True(EnrollmentChange.IsActive(EnrollmentChange.TransferredIn));
        Assert.False(EnrollmentChange.IsActive(EnrollmentChange.TransferredOut));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("전학")]        // 1.0 이전 데이터에 남아 있던 값
    [InlineData("알 수 없는 값")]
    public void 모르는_값은_명단에_넣는다(string? changeType)
    {
        // 숨겨서 잃는 것(멀쩡한 학생이 명단에서 사라짐)이 남겨서 잃는 것보다 크다.
        Assert.True(EnrollmentChange.IsActive(changeType));
    }

    [Fact]
    public void 모든_값이_활성_비활성_어느_한쪽에_속한다()
    {
        // All 목록에 값을 더하고 IsActive 를 안 고치면 여기서 걸린다.
        int active = 0, inactive = 0;
        foreach (var t in EnrollmentChange.All)
        {
            if (EnrollmentChange.IsActive(t)) active++; else inactive++;
        }

        Assert.Equal(3, active);      // 입학 · 진급 · 전입
        Assert.Equal(7, inactive);    // 전출 · 졸업 · 휴학 · 유예 · 정원외 · 자퇴 · 퇴학
        Assert.Equal(EnrollmentChange.All.Length, active + inactive);
    }

    // ── 기본값 ────────────────────────────────────────────────

    [Theory]
    [InlineData(1)]
    [InlineData(0)]   // 학년을 아직 못 정한 경우도 입학으로 본다
    public void 일학년_기본값은_입학(int grade)
    {
        Assert.Equal(EnrollmentChange.Admitted, EnrollmentChange.DefaultFor(grade));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(6)]
    public void 그_위는_진급(int grade)
    {
        Assert.Equal(EnrollmentChange.Promoted, EnrollmentChange.DefaultFor(grade));
    }

    // ── 모델의 불변식 ─────────────────────────────────────────

    [Fact]
    public void ChangeType_을_바꾸면_IsActive_가_따라온다()
    {
        // 이 둘이 갈라지면 명단에서 빠져야 할 학생이 남거나 그 반대가 된다.
        var e = new Enrollment();
        Assert.True(e.IsActive);                       // 기본값은 입학

        e.ChangeType = EnrollmentChange.TransferredOut;
        Assert.False(e.IsActive);

        e.ChangeType = EnrollmentChange.TransferredIn;
        Assert.True(e.IsActive);
    }

    [Fact]
    public void ApplyChange_는_유형과_일자와_활성을_한_번에_맞춘다()
    {
        var e = new Enrollment();

        e.ApplyChange(EnrollmentChange.TransferredOut, new DateTime(2026, 5, 10));

        Assert.Equal(EnrollmentChange.TransferredOut, e.ChangeType);
        Assert.Equal(new DateTime(2026, 5, 10), e.ChangeDate);
        Assert.False(e.IsActive);
    }

    [Fact]
    public void ApplyChange_에_날짜를_안_주면_옛_날짜를_지키지_않고_그대로_둔다()
    {
        var e = new Enrollment();
        e.ApplyChange(EnrollmentChange.Admitted, new DateTime(2026, 3, 2));

        e.ApplyChange(EnrollmentChange.Promoted);   // 날짜 생략

        Assert.Equal(EnrollmentChange.Promoted, e.ChangeType);
        Assert.Equal(new DateTime(2026, 3, 2), e.ChangeDate);
    }
}

using NewSchool.Models;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 학적 상태 판정 회귀.
///
/// 코드가 상태를 두고 묻는 질문은 <see cref="EnrollmentStatus.IsOnRoll"/> 하나뿐이다 —
/// 지금 명부에 있는가. 여기가 어긋나면 전출한 학생이 명렬표·좌석표·수업·동아리에
/// 계속 끼거나, 반대로 멀쩡한 학생이 통째로 사라진다. 후자가 훨씬 나쁘므로
/// <b>판단이 안 서는 값은 재적으로 본다</b>는 것도 함께 고정해 둔다.
/// </summary>
public sealed class EnrollmentStatusTests
{
    [Theory]
    [InlineData(EnrollmentStatus.Enrolled)]       // 재학
    [InlineData(EnrollmentStatus.TransferredIn)]  // 전입 — 이 학교로 왔다
    [InlineData(EnrollmentStatus.OnLeave)]        // 휴학 — 학적이 살아 있다
    public void 재적으로_보는_상태(string status)
    {
        Assert.True(EnrollmentStatus.IsOnRoll(status));
    }

    [Theory]
    [InlineData(EnrollmentStatus.TransferredOut)] // 전출 — 다른 학교로 갔다
    [InlineData(EnrollmentStatus.Graduated)]
    [InlineData(EnrollmentStatus.Withdrawn)]
    [InlineData(EnrollmentStatus.Expelled)]
    public void 재적이_아닌_상태(string status)
    {
        Assert.False(EnrollmentStatus.IsOnRoll(status));
    }

    [Fact]
    public void 전입과_전출은_재적_여부가_정반대다()
    {
        Assert.True(EnrollmentStatus.IsOnRoll(EnrollmentStatus.TransferredIn));
        Assert.False(EnrollmentStatus.IsOnRoll(EnrollmentStatus.TransferredOut));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(EnrollmentStatus.Transferred)]  // 1.0 이전 데이터에 남아 있는 "전학"
    [InlineData("알 수 없는 값")]
    public void 판단이_안_서는_값은_재적으로_본다(string? status)
    {
        // 숨겨서 잃는 것(멀쩡한 학생이 명부에서 사라짐)이 남겨서 잃는 것보다 크다.
        Assert.True(EnrollmentStatus.IsOnRoll(status));
    }

    [Fact]
    public void 재적_판정은_Enrollment_에서도_같다()
    {
        var 전출 = new Enrollment { Status = EnrollmentStatus.TransferredOut };
        var 전입 = new Enrollment { Status = EnrollmentStatus.TransferredIn };

        Assert.False(전출.IsOnRoll);
        Assert.True(전입.IsOnRoll);
    }
}

using NewSchool.Models;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 명렬표의 "보호자" 가 누구인가 — 45차 4단계에서 바로잡은 규칙을 못박는다.
///
/// <para><b>보호자는 보호자고 부모는 부모다. 보호자가 부모가 아닐 수 있다.</b>
/// 조부모·친척·위탁 가정·시설처럼 부모가 아닌 사람이 보호자인 경우가 실제로 있고,
/// 그럴 때 교사는 보호자 칸에 그 사람을 적어 둔다.</para>
/// </summary>
public class PrimaryGuardianTests
{
    /// <summary>
    /// 예전 순서(<c>어머니 → 아버지 → 보호자</c>)의 정확한 실패 모양이다.
    /// 보호자를 명시해 두어도 어머니 이름만 있으면 <b>그 보호자를 통째로 무시</b>했다.
    /// </summary>
    [Fact]
    public void 보호자를_적어_두었으면_부모보다_먼저다()
    {
        var detail = new StudentDetail
        {
            GuardianName = "김철수",
            GuardianRelation = "조부",
            GuardianPhone = "010-1111-1111",
            MotherName = "이영희",
            MotherPhone = "010-2222-2222",
        };

        var g = detail.ResolvePrimaryGuardian();

        Assert.Equal("김철수", g.Name);
        Assert.Equal("조부", g.Relation);
        Assert.Equal("010-1111-1111", g.Phone);
    }

    /// <summary>보호자 칸이 비어 있으면 부모가 보호자다 — 그때만 물러선다.</summary>
    [Fact]
    public void 보호자가_비면_어머니_아버지_순으로_물러선다()
    {
        var mother = new StudentDetail
        {
            MotherName = "이영희",
            MotherPhone = "010-2222-2222",
            FatherName = "박민수",
            FatherPhone = "010-3333-3333",
        };
        var m = mother.ResolvePrimaryGuardian();
        Assert.Equal("이영희", m.Name);
        Assert.Equal("모", m.Relation);
        Assert.Equal("010-2222-2222", m.Phone);

        var father = new StudentDetail
        {
            FatherName = "박민수",
            FatherPhone = "010-3333-3333",
        };
        var f = father.ResolvePrimaryGuardian();
        Assert.Equal("박민수", f.Name);
        Assert.Equal("부", f.Relation);
    }

    /// <summary>
    /// <b>이름과 연락처는 같은 사람의 것이어야 한다.</b>
    ///
    /// <para>예전에는 두 함수가 각자 훑어서, 이름은 어머니에서 연락처는 보호자에서 나오는
    /// 조합이 한 줄에 실릴 수 있었다 — 명렬표를 보고 전화를 걸면 이름과 다른 사람이 받는다.</para>
    /// </summary>
    [Fact]
    public void 이름과_연락처가_다른_사람이_되지_않는다()
    {
        // 보호자는 이름만, 어머니는 연락처만 있는 어긋난 입력
        var detail = new StudentDetail
        {
            GuardianName = "김철수",
            MotherPhone = "010-2222-2222",
        };

        var g = detail.ResolvePrimaryGuardian();

        // 보호자를 적어 두었으므로 그 사람이다. 연락처가 비어 있다고 해서
        // 어머니 번호를 가져오면 "김철수 / 어머니 번호" 가 된다.
        Assert.Equal("김철수", g.Name);
        Assert.Equal(string.Empty, g.Phone);
    }

    /// <summary>관계를 적지 않았으면 최소한 "보호자" 라고는 말해 준다.</summary>
    [Fact]
    public void 관계를_안_적었으면_보호자로_표시한다()
    {
        var detail = new StudentDetail { GuardianName = "김철수" };
        Assert.Equal("보호자", detail.ResolvePrimaryGuardian().Relation);
    }

    [Fact]
    public void 아무것도_없으면_전부_빈칸이다()
    {
        var g = new StudentDetail().ResolvePrimaryGuardian();

        Assert.Equal(string.Empty, g.Name);
        Assert.Equal(string.Empty, g.Phone);
        Assert.Equal(string.Empty, g.Relation);
    }

    /// <summary>낱개 함수 둘은 이제 같은 판단을 지나간다.</summary>
    [Fact]
    public void 낱개_함수도_같은_판단을_쓴다()
    {
        var detail = new StudentDetail
        {
            GuardianName = "김철수",
            GuardianPhone = "010-1111-1111",
            MotherName = "이영희",
            MotherPhone = "010-2222-2222",
        };

        Assert.Equal("김철수", detail.GetPrimaryGuardianName());
        Assert.Equal("010-1111-1111", detail.GetPrimaryContact());
    }
}

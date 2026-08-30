using System.Linq;
using NewSchool.Board.Models;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 게시판 페이저의 번호 고르기(<see cref="PageWindow"/>) 고정 (2026-08-30).
///
/// <para>첫 장·끝 장은 언제나 보이고, 생략표는 <b>끊기는 자리에만</b> 들어가야 한다.
/// 눈으로는 잘 안 보이는 경계(1~3페이지 근처, 끝 근처, 생략표가 한쪽만 생기는 구간)를
/// 여기서 고정한다.</para>
/// </summary>
public class PageWindowTests
{
    /// <summary>읽기 쉬운 문자열로 바꾼다 — 생략표는 "…".</summary>
    private static string Render(int current, int total, int windowSize = PageWindow.DefaultWindowSize)
        => string.Join(" ", PageWindow.Build(current, total, windowSize)
            .Select(t => t.IsEllipsis ? "…" : t.Number.ToString()));

    [Fact]
    public void 글이_없으면_그릴_칸도_없다()
        => Assert.Empty(PageWindow.Build(current: 1, total: 0));

    [Theory]
    [InlineData(1, 1, "1")]
    [InlineData(1, 5, "1 2 3 4 5")]
    [InlineData(4, 7, "1 2 3 4 5 6 7")]
    public void 창에_다_들어가면_생략표_없이_전부_편다(int current, int total, string expected)
        => Assert.Equal(expected, Render(current, total));

    /// <summary>총 8페이지부터 생략표가 의미를 갖는다 (첫 장 + 창 5 + 끝 장 = 7).</summary>
    [Theory]
    [InlineData(1, 8, "1 2 3 4 5 6 … 8")]
    [InlineData(8, 8, "1 … 3 4 5 6 7 8")]
    public void 총_여덟장에서_한쪽에만_생략표가_붙는다(int current, int total, string expected)
        => Assert.Equal(expected, Render(current, total));

    /// <summary>요청받은 모습 그대로: 처음 … 3 4 [5] 6 7 … 끝.</summary>
    [Fact]
    public void 가운데_페이지는_양쪽에_생략표가_붙는다()
        => Assert.Equal("1 … 3 4 5 6 7 … 42", Render(current: 5, total: 42));

    /// <summary>
    /// 앞쪽에서는 창이 왼쪽 끝에 붙어 서고 왼쪽 생략표가 없다.
    /// 창(5칸)이 2~6 을 벗어나는 <b>5페이지부터</b> 왼쪽 생략표가 생긴다.
    /// </summary>
    [Theory]
    [InlineData(1, "1 2 3 4 5 6 … 42")]
    [InlineData(2, "1 2 3 4 5 6 … 42")]
    [InlineData(3, "1 2 3 4 5 6 … 42")]
    [InlineData(4, "1 2 3 4 5 6 … 42")]
    [InlineData(5, "1 … 3 4 5 6 7 … 42")]
    public void 앞쪽에서는_창이_왼쪽에_붙어_선다(int current, string expected)
        => Assert.Equal(expected, Render(current, total: 42));

    /// <summary>뒤쪽도 대칭이다 — 39페이지부터 오른쪽 생략표가 사라진다.</summary>
    [Theory]
    [InlineData(42, "1 … 37 38 39 40 41 42")]
    [InlineData(41, "1 … 37 38 39 40 41 42")]
    [InlineData(40, "1 … 37 38 39 40 41 42")]
    [InlineData(39, "1 … 37 38 39 40 41 42")]
    [InlineData(38, "1 … 36 37 38 39 40 … 42")]
    public void 뒤쪽에서는_창이_오른쪽에_붙어_선다(int current, string expected)
        => Assert.Equal(expected, Render(current, total: 42));

    /// <summary>현재 페이지는 언제나 그려진 칸 안에 있어야 한다 — 없으면 강조할 자리가 사라진다.</summary>
    [Fact]
    public void 현재_페이지는_항상_칸에_들어_있다()
    {
        for (int total = 1; total <= 60; total++)
        {
            for (int current = 1; current <= total; current++)
            {
                var numbers = PageWindow.Build(current, total)
                    .Where(t => !t.IsEllipsis).Select(t => t.Number).ToList();

                Assert.Contains(current, numbers);
                Assert.Contains(1, numbers);
                Assert.Contains(total, numbers);
                Assert.Equal(numbers.OrderBy(n => n), numbers);          // 오름차순
                Assert.Equal(numbers.Distinct().Count(), numbers.Count); // 중복 없음
            }
        }
    }

    /// <summary>범위 밖 현재 페이지는 안쪽으로 끌어당긴다(옛 페이지 수로 그려진 버튼 대비).</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(99)]
    public void 범위_밖_현재_페이지도_칸을_만든다(int current)
    {
        var numbers = PageWindow.Build(current, total: 10)
            .Where(t => !t.IsEllipsis).Select(t => t.Number).ToList();

        Assert.Contains(1, numbers);
        Assert.Contains(10, numbers);
    }

    /// <summary>생략표는 연달아 붙지 않는다.</summary>
    [Fact]
    public void 생략표가_연달아_붙지_않는다()
    {
        for (int total = 1; total <= 60; total++)
        {
            for (int current = 1; current <= total; current++)
            {
                var tokens = PageWindow.Build(current, total);
                for (int i = 1; i < tokens.Count; i++)
                    Assert.False(tokens[i].IsEllipsis && tokens[i - 1].IsEllipsis,
                        $"생략표가 연달아 붙었다: current={current}, total={total}");
            }
        }
    }
}

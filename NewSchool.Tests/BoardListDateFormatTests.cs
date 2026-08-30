using System;
using System.IO;
using System.Text.RegularExpressions;
using NewSchool.Converters;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 게시판 목록의 날짜 표기 고정 (2026-08-30).
///
/// <para>목록은 한 줄이 좁아 <b>날짜만</b> 보여 준다. 상세와 댓글은 시각을 남긴다 —
/// 댓글은 시각이 있어야 주고받은 순서가 읽힌다. 형식은 공용 컨버터의
/// <c>ConverterParameter</c> 로 갈리므로, 매개변수를 안 넘긴 기존 사용처가
/// 예전 그대로여야 한다는 점까지 함께 고정한다.</para>
/// </summary>
public class BoardListDateFormatTests
{
    private static readonly DateTime Sample = new(2026, 8, 30, 14, 5, 9);

    [Fact]
    public void 매개변수가_없으면_예전대로_날짜와_시각()
    {
        var converter = new DateTimeToStringConverter();
        Assert.Equal("2026-08-30 14:05:09",
            converter.Convert(Sample, typeof(string), null!, ""));
    }

    [Fact]
    public void 매개변수로_형식을_지정하면_그대로_쓴다()
    {
        var converter = new DateTimeToStringConverter();
        Assert.Equal("2026-08-30",
            converter.Convert(Sample, typeof(string), "yyyy-MM-dd", ""));
    }

    [Fact]
    public void 날짜가_아니면_빈_문자열()
    {
        var converter = new DateTimeToStringConverter();
        Assert.Equal(string.Empty, converter.Convert("아무거나", typeof(string), null!, ""));
    }

    /// <summary>
    /// 목록의 날짜 칸 셋(표·카드·갤러리)이 모두 날짜만 쓰는지 소스로 확인한다.
    /// 뷰 모드를 하나 손볼 때 나머지가 따라오지 않아 서로 어긋나기 쉬운 자리다.
    /// </summary>
    [Fact]
    public void 목록_세_뷰모드_모두_날짜만_보여준다()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "NewSchool.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        var xaml = File.ReadAllText(Path.Combine(dir!.FullName, "Board", "Pages", "PostListPage.xaml"));
        // 안에 {StaticResource ...} 가 들어 있으므로 중괄호 한 겹 중첩까지 받아 준다.
        var bindings = Regex.Matches(xaml, @"\{x:Bind DateTime,(?:[^{}]|\{[^{}]*\})*\}");

        Assert.Equal(3, bindings.Count);   // 표 · 카드 · 갤러리
        Assert.All(bindings, m => Assert.Contains("ConverterParameter='yyyy-MM-dd'", m.Value));
    }
}

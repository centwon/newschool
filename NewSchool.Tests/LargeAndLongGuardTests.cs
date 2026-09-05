using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// <b>축 "많을 때·길 때"(2026-09-05)가 찾은 두 자리를 지킨다.</b>
///
/// <para>둘 다 <b>수가 적을 때는 안 보이다가</b> 40명·300건에서 드러났다. 개발 PC 의 진짜
/// 자료는 한 반 17명이라 눈으로는 다시 놓치기 쉬우므로, 규칙을 소스에 고정해 둔다.</para>
/// </summary>
public class LargeAndLongGuardTests
{
    /// <summary>
    /// 좌석은 <b>격자를 만들 때와 자동배정 뒤가 같은 줄 높이</b>를 써야 한다.
    ///
    /// <para><c>card.ActualHeight</c> 를 쓰면 사진 표시를 껐을 때 카드 높이가 1/5 로 줄어
    /// (사진 칸이 0 이 된다) 계산값과 어긋나고, 그 오차가 <c>×(줄+1)</c> 로 쌓여
    /// <b>줄이 많을수록 카드가 아래로 뭉친다</b>.</para>
    /// </summary>
    [Fact]
    public void 자동배정은_카드의_실제높이로_자리를_잡지_않는다()
    {
        string source = File.ReadAllText(Path.Combine(RepoRoot(), "Pages", "PageSeats.xaml.cs"));
        string body = MethodBody(source, "SeatAssignAsync");

        Assert.Contains("CellHeight", body);

        // Room.ActualHeight(캔버스 높이)는 여기서도 옳게 쓰인다 — 막아야 하는 것은
        // 카드 자신의 높이다.
        Assert.DoesNotContain("card.ActualHeight", body);
    }

    /// <summary>
    /// 누가기록 인쇄는 카드 머리줄(번호·영역·날짜)을 <c>Decoration.Before</c> 로 둬야 한다.
    ///
    /// <para>그래야 카드가 쪽 경계에 걸렸을 때 <b>넘어간 쪽에도 머리줄이 다시 찍힌다</b>.
    /// 예전에는 본문만 넘어가 어느 기록인지 알 수 없었다(300건이면 62쪽이라 여러 번 일어난다).
    /// <c>ShowEntire</c> 로 막지 않은 이유는 한 쪽보다 긴 기록에서 예외를 던져 인쇄 전체가
    /// 실패하기 때문이다.</para>
    /// </summary>
    [Fact]
    public void 누가기록_인쇄는_카드_머리줄을_쪽마다_다시_찍는다()
    {
        string source = File.ReadAllText(Path.Combine(RepoRoot(), "Services", "StudentLogPrintService.cs"));
        string body = MethodBody(source, "ComposeLogItem");

        Assert.Contains("Decoration", body);
        Assert.Contains("decoration.Before()", body);
    }

    /// <summary>
    /// 메서드 선언부터 중괄호 짝이 닫힐 때까지. <b>주석 줄은 빼고</b> 돌려준다 —
    /// "이렇게 쓰지 말 것" 이라고 적어 둔 주석이 그 자체로 시험을 깨뜨리기 때문이다
    /// (실제로 한 번 걸렸다).
    /// </summary>
    private static string MethodBody(string source, string methodName)
    {
        var declaration = Regex.Match(source, @"\b" + Regex.Escape(methodName) + @"\s*\([^)]*\)\s*\{");
        Assert.True(declaration.Success, $"{methodName} 을 찾지 못했다 — 시험이 낡았다.");

        int start = source.IndexOf('{', declaration.Index);
        int depth = 0;
        for (int i = start; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return StripComments(source[start..(i + 1)]);
        }

        Assert.Fail($"{methodName} 의 본문이 닫히지 않는다.");
        return string.Empty;
    }

    private static string StripComments(string body) =>
        Regex.Replace(body, @"^\s*//.*$", string.Empty, RegexOptions.Multiline);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "NewSchool.csproj")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}

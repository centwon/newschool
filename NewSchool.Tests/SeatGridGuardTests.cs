using System;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Services;
using NewSchool.Tests.Infrastructure;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 좌석배정표 출력 격자 계산 회귀 테스트 — 전수 조사 36차.
///
/// 세 출력 경로(PDF·HTML·Excel)가 모두 <c>jul * jjak</c> 으로 나누는데 이 값은 DB 에서
/// 그대로 온다. 26차에 확인했듯 구데이터나 초기화 실패로 0 이 들어 있을 수 있고,
/// 그러면 0 으로 나뉘어 좌석표가 깨지거나 QuestPDF 안쪽에서 터졌다.
/// </summary>
public class SeatGridGuardTests
{
    /// <summary>
    /// 짝 수가 0 인 배치로 HTML 을 만들어도 예외 없이 결과가 나와야 한다.
    /// (수정 전에는 <c>totalCols = 0</c> 이 되어 나눗셈에서 깨졌다.)
    /// </summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 0)]
    [InlineData(0, 2)]
    [InlineData(-1, -1)]
    public void 줄이나_짝이_0이어도_좌석표_HTML_이_만들어진다(int jul, int jjak)
    {
        var svc = new SeatsPrintService();
        var cells = new System.Collections.Generic.List<SeatsPrintService.SeatCellData>
        {
            new() { Row = 0, Col = 0 },
            new() { Row = 0, Col = 1 },
        };

        var html = svc.BuildSeatsHtml(
            cells, grade: 1, classRoom: 1, jul: jul, jjak: jjak,
            message: "", showPhoto: false);

        Assert.False(string.IsNullOrWhiteSpace(html));
    }

    /// <summary>
    /// 줄 수 하한(2)이 렌더 루프까지 실제로 반영되는지 — 30명·jul=0 이면 열 2개·15행이어야 한다.
    /// 예전에는 보정값을 totalCols 계산에만 쓰고 루프는 원본을 써서 칸이 하나도 안 그려졌다.
    /// 하한이 1 이면 30행이 되어 좌석표 PDF 가 레이아웃 예외로 터진다(17행부터).
    /// </summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(1, 0)]
    public void 줄이_하한_아래여도_열두개_격자로_그려진다(int jul, int jjak)
    {
        var svc = new SeatsPrintService();
        var cells = new System.Collections.Generic.List<SeatsPrintService.SeatCellData>();
        for (int i = 0; i < 30; i++)
            cells.Add(new SeatsPrintService.SeatCellData { Row = i / 2, Col = i % 2 });

        var html = svc.BuildSeatsHtml(
            cells, grade: 1, classRoom: 1, jul: jul, jjak: jjak,
            message: "", showPhoto: false);

        // 좌석 표의 행 수 = 30명 / 열 2개 = 15행 (30행이 아니다)
        int seatRows = System.Text.RegularExpressions.Regex.Matches(
            html[html.IndexOf("<table class=\"seats\">", StringComparison.Ordinal)..], "<tr>").Count;
        Assert.Equal(15, seatRows);

        // 칸이 실제로 그려졌는지 (루프가 원본 jul 을 쓰면 0개가 된다)
        Assert.Contains("<td", html, StringComparison.Ordinal);
    }
    [Fact]
    public void 정상값은_종전대로_동작한다()
    {
        var svc = new SeatsPrintService();
        var cells = new System.Collections.Generic.List<SeatsPrintService.SeatCellData>
        {
            new() { Row = 0, Col = 0 },
            new() { Row = 0, Col = 1 },
            new() { Row = 1, Col = 0 },
            new() { Row = 1, Col = 1 },
        };

        var html = svc.BuildSeatsHtml(
            cells, grade: 2, classRoom: 3, jul: 2, jjak: 2,
            message: "안내 문구", showPhoto: false);

        Assert.Contains("안내 문구", html);
    }
}

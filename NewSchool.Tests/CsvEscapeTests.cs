using NewSchool.Services;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// CsvExportService.Escape — RFC 4180 인용 규칙 + Excel CSV Injection 방어.
/// </summary>
public class CsvEscapeTests
{
    [Fact]
    public void Escape_Null_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, CsvExportService.Escape(null));
    }

    [Fact]
    public void Escape_Empty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, CsvExportService.Escape(string.Empty));
    }

    [Theory]
    [InlineData("Hello")]
    [InlineData("학생 이름")]
    [InlineData("12345")]
    [InlineData("a b c")]
    public void Escape_PlainText_ReturnsAsIs(string value)
    {
        Assert.Equal(value, CsvExportService.Escape(value));
    }

    [Fact]
    public void Escape_ValueWithComma_IsQuoted()
    {
        Assert.Equal("\"a,b\"", CsvExportService.Escape("a,b"));
    }

    [Fact]
    public void Escape_ValueWithNewline_IsQuoted()
    {
        Assert.Equal("\"line1\nline2\"", CsvExportService.Escape("line1\nline2"));
    }

    [Fact]
    public void Escape_ValueWithCarriageReturn_IsQuoted()
    {
        Assert.Equal("\"a\rb\"", CsvExportService.Escape("a\rb"));
    }

    [Fact]
    public void Escape_ValueWithDoubleQuote_IsQuotedAndDoubled()
    {
        // 내부 " → "" + 전체 인용
        Assert.Equal("\"a\"\"b\"", CsvExportService.Escape("a\"b"));
    }

    [Fact]
    public void Escape_ValueWithMultipleQuotes_AllDoubled()
    {
        Assert.Equal("\"\"\"hello\"\"\"", CsvExportService.Escape("\"hello\""));
    }

    [Fact]
    public void Escape_HangulWithComma_IsQuoted()
    {
        Assert.Equal("\"한,글\"", CsvExportService.Escape("한,글"));
    }

    #region CSV Injection 방어 — 선행 = + - @ 인용

    [Theory]
    [InlineData("=SUM(A1:A10)", "\"=SUM(A1:A10)\"")]
    [InlineData("+1234", "\"+1234\"")]
    [InlineData("-1234", "\"-1234\"")]
    [InlineData("@cmd", "\"@cmd\"")]
    public void Escape_FormulaPrefix_IsQuoted(string input, string expected)
    {
        Assert.Equal(expected, CsvExportService.Escape(input));
    }

    [Fact]
    public void Escape_EqualsInMiddle_NotQuoted()
    {
        // 선행이 아니면 인용 안 함
        Assert.Equal("a=b", CsvExportService.Escape("a=b"));
    }

    [Fact]
    public void Escape_NegativeNumber_IsQuoted()
    {
        // -3 처럼 정상적인 음수도 인용됨 (CSV Injection 보수적 정책)
        Assert.Equal("\"-3\"", CsvExportService.Escape("-3"));
    }

    #endregion

    #region 복합 케이스

    [Fact]
    public void Escape_FormulaWithCommaAndQuote_BothEscaped()
    {
        // =A,"B" → 전체 인용 + 내부 " → ""
        Assert.Equal("\"=A,\"\"B\"\"\"", CsvExportService.Escape("=A,\"B\""));
    }

    [Fact]
    public void Escape_MultilineHangulWithQuote_BothEscaped()
    {
        Assert.Equal("\"학생\n\"\"우수\"\"\"", CsvExportService.Escape("학생\n\"우수\""));
    }

    #endregion
}

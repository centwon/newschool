using NewSchool.Services;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// CsvExportService.ParseRecords — RFC 4180 파싱(인용 필드 안 쉼표·줄바꿈, "" 이스케이프).
/// Escape 와의 왕복 일치도 확인.
/// </summary>
public class CsvParseTests
{
    [Fact]
    public void 단순_행_분리()
    {
        var r = CsvExportService.ParseRecords("a,b,c\n1,2,3");
        Assert.Equal(2, r.Count);
        Assert.Equal(new[] { "a", "b", "c" }, r[0]);
        Assert.Equal(new[] { "1", "2", "3" }, r[1]);
    }

    [Fact]
    public void 인용_필드_안_쉼표는_분리되지_않는다()
    {
        var r = CsvExportService.ParseRecords("\"홍길동, 반장\",2학년");
        Assert.Single(r);
        Assert.Equal(new[] { "홍길동, 반장", "2학년" }, r[0]);
    }

    [Fact]
    public void 인용_필드_안_줄바꿈은_같은_레코드()
    {
        var r = CsvExportService.ParseRecords("\"1줄\n2줄\",끝");
        Assert.Single(r);
        Assert.Equal(new[] { "1줄\n2줄", "끝" }, r[0]);
    }

    [Fact]
    public void 이스케이프된_따옴표()
    {
        var r = CsvExportService.ParseRecords("\"a\"\"b\",c");
        Assert.Equal(new[] { "a\"b", "c" }, r[0]);
    }

    [Fact]
    public void BOM_과_CRLF_처리()
    {
        var r = CsvExportService.ParseRecords("﻿a,b\r\n1,2\r\n");
        Assert.Equal(2, r.Count);
        Assert.Equal(new[] { "a", "b" }, r[0]);
        Assert.Equal(new[] { "1", "2" }, r[1]);
    }

    [Fact]
    public void Escape_와_왕복_일치()
    {
        // 쉼표·줄바꿈·따옴표를 모두 포함한 필드가 Escape → ParseRecords 왕복에서 보존돼야 함
        string field = "a,b\n\"q\"";
        string line = $"{CsvExportService.Escape(field)},tail";
        var r = CsvExportService.ParseRecords(line);
        Assert.Single(r);
        Assert.Equal(field, r[0][0]);
        Assert.Equal("tail", r[0][1]);
    }
}

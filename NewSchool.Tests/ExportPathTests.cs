using System;
using System.IO;
using NewSchool.Helpers;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 내보내기·인쇄물이 <b>어디에 저장되는가</b> — 45차에서 한 곳으로 모은 규칙을 못박는다.
///
/// <para>예전에는 이 규칙이 열한 벌이었고 결국 갈라졌다. 통합 내보내기 화면에서 형식만
/// 바꿨을 뿐인데 좌석 xlsx·html 과 학생카드 xlsx 는 <c>Prints</c> 로, 누가기록
/// xlsx·html·csv 는 <c>Exports</c> 로 흩어져 사용자가 방금 만든 파일을 찾지 못했다.</para>
/// </summary>
public class ExportPathTests
{
    /// <summary><b>규칙: 확장자가 폴더를 정한다.</b> PDF 는 종이로 낼 것이라 Prints.</summary>
    [Theory]
    [InlineData("누가기록_3학년1반_일괄_20260903_101112.pdf")]
    [InlineData("좌석배정표_3학년1반_20260903_101112.PDF")]   // 대소문자를 가리지 않는다
    public void PDF_는_인쇄물_폴더로_간다(string fileName)
    {
        var path = ExportPaths.Resolve(fileName);
        Assert.Equal(ExportPaths.PrintDir, Path.GetDirectoryName(path));
    }

    /// <summary>나머지는 다른 프로그램에서 열어 볼 것이라 Exports.</summary>
    [Theory]
    [InlineData("누가기록_3학년1반_20260903_101112.xlsx")]
    [InlineData("누가기록_3학년1반_20260903_101112.csv")]
    [InlineData("누가기록_3학년1반_일괄_20260903_101112.html")]
    [InlineData("확장자없음")]
    public void 나머지는_내보내기_폴더로_간다(string fileName)
    {
        var path = ExportPaths.Resolve(fileName);
        Assert.Equal(ExportPaths.ExportDir, Path.GetDirectoryName(path));
    }

    /// <summary>
    /// <b>규칙: 같은 이름이 있으면 덮어쓰지 않고 비켜난다.</b>
    ///
    /// <para>파일명에 초 단위 시각이 들어가 좀처럼 겹치지 않지만 <b>같은 초에 두 번 누르면
    /// 겹친다.</b> 그때 조용히 덮어쓰면 먼저 만든 것이 오류도 없이 사라진다 —
    /// 첨부파일에서 이미 한 번 겪은 일이다.</para>
    /// </summary>
    [Fact]
    public void 이름이_겹치면_덮어쓰지_않고_비켜난다()
    {
        var stem = $"겹침시험_{Guid.NewGuid():N}";
        var fileName = $"{stem}.csv";

        var first = ExportPaths.Resolve(fileName);
        try
        {
            File.WriteAllText(first, "먼저 만든 것");

            var second = ExportPaths.Resolve(fileName);

            Assert.NotEqual(first, second);
            Assert.EndsWith($"{stem} (2).csv", second);

            // 먼저 만든 것이 그대로 남아 있어야 한다
            Assert.Equal("먼저 만든 것", File.ReadAllText(first));
        }
        finally
        {
            if (File.Exists(first)) File.Delete(first);
        }
    }

    /// <summary>폴더가 없으면 만든다 — 부르는 쪽이 매번 확인하지 않아도 되게.</summary>
    [Fact]
    public void 폴더가_없으면_만들어_준다()
    {
        ExportPaths.Resolve("폴더생성시험.csv");
        ExportPaths.Resolve("폴더생성시험.pdf");

        Assert.True(Directory.Exists(ExportPaths.ExportDir));
        Assert.True(Directory.Exists(ExportPaths.PrintDir));
    }

    /// <summary>두 폴더는 서로 달라야 한다(같아지면 규칙 자체가 의미를 잃는다).</summary>
    [Fact]
    public void 인쇄물과_내보내기는_다른_폴더다()
        => Assert.NotEqual(ExportPaths.PrintDir, ExportPaths.ExportDir);
}

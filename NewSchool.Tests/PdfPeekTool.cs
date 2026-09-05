using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// <b>인쇄물을 눈으로 보는 길.</b> <c>Read</c> 도구는 PDF 를 그리지 못하므로,
/// <see cref="PdfDocument"/> 로 쪽을 PNG 로 떠서 본다(53차에 쓰던 기법을 도구로 남긴다).
///
/// <para>⚠ 시험이 아니라 도구다 — 환경변수 <c>NEWSCHOOL_PDF</c> 가 없으면 아무 일도 하지 않는다.
/// <c>NEWSCHOOL_PDF_PAGES</c> 는 1부터 세는 쪽 번호를 쉼표로(<c>1,62</c>), 없으면 첫 쪽만.
/// PNG 는 PDF 옆 <c>peek\</c> 폴더에 떨어진다.</para>
/// </summary>
public class PdfPeekTool
{
    [Fact]
    public async Task PDF_쪽을_PNG_로_뜬다()
    {
        string? path = Environment.GetEnvironmentVariable("NEWSCHOOL_PDF");
        if (string.IsNullOrWhiteSpace(path)) return;

        var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
        var doc = await PdfDocument.LoadFromFileAsync(file);

        string outDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "peek");
        Directory.CreateDirectory(outDir);

        var pages = (Environment.GetEnvironmentVariable("NEWSCHOOL_PDF_PAGES") ?? "1")
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => uint.Parse(s.Trim()))
            .Where(n => n >= 1 && n <= doc.PageCount)
            .ToArray();

        var report = new System.Text.StringBuilder();
        report.AppendLine($"쪽수: {doc.PageCount}");

        foreach (uint number in pages)
        {
            using var page = doc.GetPage(number - 1);
            using var stream = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(stream);

            var bytes = new byte[stream.Size];
            using (var reader = new DataReader(stream.GetInputStreamAt(0)))
            {
                await reader.LoadAsync((uint)stream.Size);
                reader.ReadBytes(bytes);
            }

            string outPath = Path.Combine(outDir, $"page{number}.png");
            await File.WriteAllBytesAsync(outPath, bytes);
            report.AppendLine($"  {outPath}  ({page.Size.Width:N0}x{page.Size.Height:N0})");
        }

        // 시험 출력으로 경로를 남긴다 — 실패시켜 보여 주는 대신 파일에 적는다.
        await File.WriteAllTextAsync(Path.Combine(outDir, "peek.txt"), report.ToString());
    }
}

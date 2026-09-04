using System;
using System.Diagnostics;
using System.IO;

namespace NewSchool.Helpers;

/// <summary>
/// 내보내기·인쇄물이 저장될 자리를 정하는 <b>유일한 곳</b>.
///
/// <para>예전에는 이 규칙이 열한 벌이었다 — 서비스마다 <c>GetOutputDir()</c> 를 하나씩
/// 들고 있었고(6개) 나머지는 <c>Path.Combine(Settings.RootPath, "Exports")</c> 를 그 자리에
/// 적어 넣었다(5곳). 같은 규칙이 흩어져 있으니 결국 갈라졌다: 통합 내보내기 화면에서
/// 형식만 바꿨을 뿐인데 좌석 xlsx·html 과 학생카드 xlsx 는 <c>Prints</c> 로, 누가기록
/// xlsx·html·csv 는 <c>Exports</c> 로 흩어져 사용자가 방금 만든 파일을 찾지 못했다.</para>
///
/// <para><b>규칙은 확장자가 정한다.</b> PDF 는 종이로 낼 것이라 <c>Prints</c>, 나머지
/// (xlsx·csv·html)는 다른 프로그램에서 열어 볼 것이라 <c>Exports</c>. 이 문장은
/// <c>UnifiedExportService</c> 가 주석으로 선언해 두고 정작 자기 형제들이 어기던 규칙이다.</para>
/// </summary>
public static class ExportPaths
{
    /// <summary>인쇄물(PDF) 폴더.</summary>
    public static string PrintDir => Path.Combine(Settings.RootPath, "Prints");

    /// <summary>내보내기(xlsx·csv·html) 폴더.</summary>
    public static string ExportDir => Path.Combine(Settings.RootPath, "Exports");

    /// <summary>
    /// 파일명 하나를 받아 <b>실제로 저장할 절대 경로</b>를 돌려준다.
    /// 폴더가 없으면 만들고, 같은 이름이 이미 있으면 비켜난 이름을 고른다.
    /// </summary>
    /// <param name="fileName">확장자를 포함한 파일명. 확장자가 폴더를 정한다.</param>
    public static string Resolve(string fileName)
    {
        var dir = IsPrintable(fileName) ? PrintDir : ExportDir;
        Directory.CreateDirectory(dir);   // 이미 있으면 아무 일도 하지 않는다
        return ResolveFreeName(dir, fileName);
    }

    /// <summary>종이로 낼 형식인가(= <see cref="PrintDir"/> 로 가는가).</summary>
    private static bool IsPrintable(string fileName) =>
        string.Equals(Path.GetExtension(fileName), ".pdf", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 그 폴더에서 아직 비어 있는 이름. 부딪히지 않으면 원래 이름 그대로,
    /// 부딪히면 탐색기와 같은 <c>이름 (2).확장자</c> 꼴.
    ///
    /// <para>파일명에 초 단위 시각이 들어가 있어 좀처럼 겹치지 않지만, <b>같은 초에 두 번
    /// 누르면 겹친다.</b> 그때 조용히 덮어쓰면 먼저 만든 것이 오류도 없이 사라진다 —
    /// 첨부파일에서 이미 한 번 겪은 일이라 여기서도 같은 규칙을 쓴다.</para>
    /// </summary>
    private static string ResolveFreeName(string dir, string fileName)
    {
        var path = Path.Combine(dir, fileName);
        if (!File.Exists(path)) return path;

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);

        for (int i = 2; i <= 1000; i++)
        {
            var candidate = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }

        // 여기까지 왔다면 이름 규칙으로는 못 푼다 — 겹치지 않을 이름을 만들어 준다.
        return Path.Combine(dir, $"{stem}_{Guid.NewGuid():N}{ext}");
    }

    /// <summary>
    /// 만들어 둔 파일을 사용자에게 보여 준다(기본 프로그램으로 열기).
    ///
    /// <para>여기서는 <b>알리지 않는다</b> — 파일은 이미 만들어졌고, 통합 내보내기 화면처럼
    /// 경로가 화면에 남는 자리도 있다. 대신 <b>열었는지를 돌려준다</b>: 경로가 화면에 없는
    /// 화면(인쇄 버튼들)은 이 값을 보고 "어디에 뒀는지" 를 알린다(53차).</para>
    ///
    /// <para>⚠ 인쇄물을 여는 길은 여기 하나다. 예전에는 좌석 인쇄·학생카드 인쇄·누가기록
    /// 인쇄·엑셀 내보내기가 각자 <c>new Uri($"file:///{path}")</c> 를 만들어 열었다 —
    /// 45차가 한 곳으로 모았다고 선언한 규칙이 정작 인쇄 쪽에서는 지켜지지 않았다.</para>
    /// </summary>
    /// <returns>기본 프로그램으로 열었으면 true.</returns>
    public static bool TryOpen(string filePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            // 알리지는 않되(위 설명), 왜 안 열렸는지는 남긴다.
            NewSchool.Logging.Log.Warning("ExportPaths", $"내보낸 파일을 열지 못했다({filePath}): {ex.Message}");
            return false;
        }
    }
}

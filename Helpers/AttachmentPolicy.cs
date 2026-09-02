using System;
using System.Collections.Generic;
using System.IO;

namespace NewSchool.Helpers;

/// <summary>
/// 첨부파일 공통 방침 — <b>게시판과 누가기록이 같은 한 벌을 쓴다.</b>
///
/// <para>첨부는 열 때 <c>Process.Start(UseShellExecute = true)</c> 로 열린다. 그러니
/// 무엇을 붙일 수 있는지가 유일한 방어선이고, 그 목록이 두 벌이 되면 한쪽만 늘어나
/// 조용히 어긋난다 — 게시판 쪽 주석이 이미 그렇게 경고하고 있었다(예전에는 실행형
/// 13종만 막아 <c>.lnk</c>·<c>.url</c>·<c>.hta</c> 같은 클릭 실행 확장자가 통과했다).
/// 그래서 <c>Board</c> 안에 있던 것을 여기로 올려 양쪽이 나눠 쓴다.</para>
///
/// <para>학교 공용 PC 에서 실수로 실행·배포되면 위험한 것들이라 <b>차단 목록</b>으로 둔다
/// (허용 목록이 아니다 — 교사가 붙일 수 있는 문서·이미지·압축의 종류를 미리 다 셀 수 없다).</para>
/// </summary>
public static class AttachmentPolicy
{
    private static readonly HashSet<string> _blockedExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".bat", ".cmd", ".com", ".scr", ".msi", ".msp",
            ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".hta",
            ".lnk", ".url", ".pif", ".scf", ".reg", ".inf", ".msc", ".cpl", ".jar", ".gadget", ".application"
        };

    /// <summary>파일명의 확장자가 실행 유발 차단 목록에 있으면 true.</summary>
    public static bool IsBlocked(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        // HashSet.Contains(인스턴스 메서드)를 쓴다 — IReadOnlyCollection 에는 Contains 가 없어
        // LINQ 가 필요해지는데, 판정 하나에 그것까지 끌어올 이유가 없다.
        return !string.IsNullOrEmpty(ext) && _blockedExtensions.Contains(ext);
    }

    /// <summary>
    /// 붙일 수 없는 파일이라고 사용자에게 보여 줄 문구. 왜 막혔는지까지 적는다 —
    /// "안 됩니다"만 있으면 사람은 이름을 바꿔 다시 시도한다.
    /// </summary>
    public static string BlockedMessage(string fileName) =>
        $"'{fileName}' 은(는) 첨부할 수 없습니다.\n" +
        "실행되거나 다른 프로그램을 띄울 수 있는 형식이라 막아 두었습니다.\n" +
        "압축(.zip)해서 붙이면 첨부할 수 있습니다.";
}

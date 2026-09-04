using System;
using System.Collections.Generic;
using System.Linq;

namespace NewSchool.Helpers;

/// <summary>
/// 시작할 때 데이터베이스를 준비하지 못했을 때 <b>무엇을 말할 것인가</b>.
///
/// <para>판정과 문구만 담아 창 없이 시험할 수 있게 갈라 둔다(47차 <c>Interpret</c>,
/// 48차 <c>ContainsRestorableDb</c> 와 같은 수법). 실제 알림은
/// <c>App.ShowFatalStartupErrorAsync</c> 가 맡는다.</para>
/// </summary>
public static class StartupDbFailureText
{
    /// <summary>학교 DB. 이것이 없으면 학생·학적·기록이 전부 안 보인다.</summary>
    public const string School = "학생 정보";

    /// <summary>게시판 DB(수업 일지·메모 포함).</summary>
    public const string Board = "게시판";

    /// <summary>일정 DB(달력·할 일).</summary>
    public const string Scheduler = "일정";

    /// <summary>
    /// 준비하지 못한 DB 이름들로 사용자에게 보여 줄 본문을 만든다.
    /// </summary>
    /// <param name="failed">실패한 DB 이름. 빈 목록이면 알릴 것이 없다.</param>
    /// <param name="dataPath">데이터 폴더 — 사용자가 직접 확인할 수 있어야 한다.</param>
    /// <param name="cause">
    /// 원인을 사람 말로 옮긴 것(<see cref="FileErrorText.Explain(Exception)"/>). 없으면 생략한다.
    /// </param>
    /// <returns>보여 줄 본문. 실패가 없으면 <c>null</c>.</returns>
    public static string? Describe(IEnumerable<string> failed, string dataPath, string? cause = null)
    {
        var names = (failed ?? Enumerable.Empty<string>())
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct()
            .ToList();

        if (names.Count == 0) return null;

        // 무엇이 막혔는지 → 어디인지 → 무엇을 하면 되는지 순서로 적는다.
        var text =
            $"{string.Join("·", names)} 자료를 열지 못했습니다.\n\n" +
            $"위치: {dataPath}\n";

        if (!string.IsNullOrWhiteSpace(cause))
            text += $"\n{cause}\n";

        text +=
            "\n이 상태로 계속하면 자료가 하나도 없는 것처럼 보이고, 그 위에 저장하면\n" +
            "예전 자료와 어긋납니다. 폴더 권한과 남은 공간을 확인한 뒤 다시 실행하거나,\n" +
            "백업이 있으면 복원해 주세요.";

        return text;
    }
}

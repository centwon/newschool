using System;
using System.Threading.Tasks;

namespace NewSchool.Controls;

/// <summary>
/// 사용자 대면 작업(저장/삭제/내보내기 등)에서 발생한 예외를 사용자에게 알리는 헬퍼.
/// 단순 로드 실패는 Debug.WriteLine으로 삼키지만,
/// 사용자가 명시적으로 요청한 작업이 실패할 때는 이 헬퍼로 대화상자를 띄워야 한다.
/// </summary>
public static class UserErrorReporter
{
    /// <summary>
    /// 예외를 사용자에게 알린다. 대화상자 표시에 실패해도 앱이 죽지 않도록 방어한다.
    /// </summary>
    /// <param name="context">"저장", "삭제", "내보내기" 등 행위 설명</param>
    /// <param name="ex">발생 예외</param>
    /// <param name="title">대화상자 제목 (기본: "{context} 오류")</param>
    public static async Task ReportAsync(string context, Exception ex, string? title = null)
    {
        try
        {
            string t = string.IsNullOrWhiteSpace(title) ? $"{context} 오류" : title!;
            string msg = $"{context} 중 오류가 발생했습니다.\n\n{Describe(ex)}";

            // ⚠ Debug.WriteLine 만으로는 기록이 아니다 — [Conditional("DEBUG")] 라 배포본에서
            //    통째로 사라진다. 사용자가 "오류가 났다" 고 할 때 로그에 아무것도 없었다.
            System.Diagnostics.Debug.WriteLine($"[UserErrorReporter] {t}: {ex}");
            NewSchool.Logging.Log.Error("UserErrorReporter", $"{context} 실패", ex);

            await MessageBox.ShowAsync(msg, t);
        }
        catch (Exception inner)
        {
            // 대화상자 실패는 치명적이 아니므로 로그만 남긴다.
            System.Diagnostics.Debug.WriteLine($"[UserErrorReporter] 알림 실패: {inner.Message}");
        }
    }

    /// <summary>
    /// 사용자에게 보일 본문. 파일·폴더가 막힌 것이면 <b>무엇을 하면 되는지</b>를 앞에 세우고
    /// 원문은 괄호로 남긴다 — 도움을 청할 때는 원문이 필요하다.
    /// </summary>
    private static string Describe(Exception ex)
    {
        var friendly = Helpers.FileErrorText.Explain(ex);

        return friendly == null
            ? ex.Message
            : $"{friendly}\n\n(자세한 내용: {ex.Message})";
    }
}

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
            string msg = $"{context} 중 오류가 발생했습니다.\n\n{ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[UserErrorReporter] {t}: {ex}");
            await MessageBox.ShowAsync(msg, t);
        }
        catch (Exception inner)
        {
            // 대화상자 실패는 치명적이 아니므로 로그만 남긴다.
            System.Diagnostics.Debug.WriteLine($"[UserErrorReporter] 알림 실패: {inner.Message}");
        }
    }
}

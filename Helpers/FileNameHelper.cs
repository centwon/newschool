using System.IO;
using System.Text;

namespace NewSchool.Helpers;

/// <summary>
/// 내보내기·인쇄 파일명에 사용자가 입력한 값(학생 이름·과목명 등)을 넣기 전에 정리한다.
///
/// 학생 이름과 과목명은 모두 사용자가 직접 입력하거나 엑셀에서 가져오는 값이라
/// <c>/ \ : * ? " &lt; &gt; |</c> 가 섞일 수 있다("국어/문학" 같은 과목명은 흔하다).
/// 그대로 <see cref="Path.Combine"/> 에 넘기면 엉뚱한 경로가 되어
/// <c>DirectoryNotFoundException</c> 이 나거나, 파일 선택 대화상자가 거부한다.
/// 어느 쪽이든 사용자에게는 이유를 알 수 없는 오류로 보인다.
/// </summary>
public static class FileNameHelper
{
    /// <summary>파일명에 쓸 수 없는 문자를 <paramref name="replacement"/> 로 바꾼다.</summary>
    /// <param name="value">사용자 입력에서 온 조각(이름·과목명 등). null 이면 빈 문자열.</param>
    /// <param name="replacement">대체 문자. 기본은 밑줄.</param>
    public static string Sanitize(string? value, char replacement = '_')
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(value.Length);

        foreach (var ch in value)
            sb.Append(System.Array.IndexOf(invalid, ch) >= 0 ? replacement : ch);

        // 윈도우는 이름 끝의 점·공백을 잘라내므로 미리 정리한다("홍길동." → "홍길동")
        return sb.ToString().TrimEnd('.', ' ');
    }
}

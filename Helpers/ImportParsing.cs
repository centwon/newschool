using System.Linq;

namespace NewSchool.Helpers;

/// <summary>
/// 엑셀 학생 일괄 입력용 순수 파싱 함수. (구 AddStudentsPage 코드비하인드에서 추출 — 테스트 가능화)
/// </summary>
public static class ImportParsing
{
    /// <summary>
    /// 셀 텍스트에서 정수 추출. 순수 숫자면 그대로, "1학년"·"3반" 처럼 섞여 있으면 숫자만 모아 파싱.
    /// 빈 값/숫자 없음이면 false.
    /// </summary>
    public static bool TryParseNumberFromText(string? text, out int result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        text = text.Trim();

        // 순수 숫자인 경우
        if (int.TryParse(text, out result)) return true;

        // 숫자 부분만 추출 ("1학년" → "1", "3반" → "3")
        var digits = new string(text.Where(char.IsDigit).ToArray());
        return !string.IsNullOrEmpty(digits) && int.TryParse(digits, out result);
    }

    /// <summary>
    /// 성별 텍스트 정규화 — "여"/"여자"/"F"/"Female" → "여", 그 외(빈 값·"남"·미상) → "남".
    /// </summary>
    public static string NormalizeSex(string? text)
    {
        var v = (text ?? string.Empty).Trim();
        if (v.StartsWith("여", System.StringComparison.OrdinalIgnoreCase) ||
            v.Equals("F", System.StringComparison.OrdinalIgnoreCase) ||
            v.Equals("Female", System.StringComparison.OrdinalIgnoreCase))
            return "여";
        return "남";
    }
}

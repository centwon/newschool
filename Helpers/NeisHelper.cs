using System;

namespace NewSchool.Helpers;

/// <summary>
/// NEIS 학교생활기록부 관련 유틸리티
/// 바이트 계산, 영역별 글자수 제한 관리
/// </summary>
public static class NeisHelper
{
    /// <summary>
    /// NEIS 바이트 계산
    /// - 한글: 3바이트
    /// - 영문/숫자: 1바이트
    /// - 기타 유니코드: 3바이트
    /// </summary>
    public static int CountByte(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        int byteCount = 0;

        foreach (char c in text)
        {
            if (c >= 0xAC00 && c <= 0xD7A3)
                byteCount += 3; // 한글 (가-힣)
            else if (c <= 0x007F)
                byteCount += 1; // ASCII (영문, 숫자, 기본 기호)
            else
                byteCount += 3; // 기타 유니코드 (한자 등)
        }

        return byteCount;
    }

    /// <summary>
    /// 학생부 영역별 최대 바이트 수 반환
    /// NEIS 기준 (2024학년도~)
    /// </summary>
    public static int GetMaxBytes(string type)
    {
        return type switch
        {
            "교과활동" => 1500,       // 교과 세부능력 및 특기사항 (과목당)
            "개인별세특" => 1500,     // 개인별 세부능력 및 특기사항
            "자율활동" => 1500,      // 자율활동 특기사항
            "동아리활동" => 1500,    // 동아리활동 특기사항
            "진로활동" => 2100,      // 진로활동 특기사항
            "종합의견" => 1500,      // 행동특성 및 종합의견
            _ => 1500               // 기본값
        };
    }

    /// <summary>
    /// 영역별 NEIS 명칭 반환
    /// </summary>
    public static string GetAreaDisplayName(string type)
    {
        return type switch
        {
            "교과활동" => "교과 세부능력 및 특기사항",
            "개인별세특" => "개인별 세부능력 및 특기사항",
            "자율활동" => "자율활동 특기사항",
            "동아리활동" => "동아리활동 특기사항",
            "진로활동" => "진로활동 특기사항",
            "종합의견" => "행동특성 및 종합의견",
            _ => type
        };
    }

    /// <summary>
    /// 바이트 수가 제한을 초과하는지 확인
    /// </summary>
    public static bool IsOverLimit(string text, string type)
    {
        int currentBytes = CountByte(text);
        int maxBytes = GetMaxBytes(type);
        return currentBytes > maxBytes;
    }

    /// <summary>
    /// 바이트 정보 문자열 생성
    /// </summary>
    public static string GetByteInfo(string text, string type)
    {
        int currentBytes = CountByte(text);
        int maxBytes = GetMaxBytes(type);
        int charCount = text?.Length ?? 0;

        return $"{currentBytes} / {maxBytes} Byte ({charCount}자)";
    }

    /// <summary>
    /// 남은 바이트 수 반환
    /// </summary>
    public static int GetRemainingBytes(string text, string type)
    {
        return GetMaxBytes(type) - CountByte(text);
    }
}

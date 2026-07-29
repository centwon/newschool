using System;
using System.Collections.Generic;

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

    /// <summary>학생부 영역 정의 — 키(저장값), 표시 이름, 코드 기본 바이트.</summary>
    public sealed record SpecArea(string Key, string Label, int DefaultBytes);

    /// <summary>
    /// 학생부 영역 단일 정의표. NEIS 기준(2024학년도~)의 코드 기본값이다.
    ///
    /// ⚠ 영역을 추가·삭제하려면 <b>이 표만</b> 고치면 된다 — 설정 화면의 한도 입력칸과
    /// 내보내기 필터의 영역 목록이 모두 이 표에서 생성된다.
    /// (예전에는 이 스위치·설정 화면 XAML·설정 코드·내보내기 필터 4곳에 흩어져 있어
    ///  실제로 "봉사활동"이 내보내기 필터에만 있고 한도·설정에는 빠져 있었다.)
    ///
    /// 지침으로 한도가 바뀌면 앱 수정 없이 설정 화면에서 학년도별로 덮어쓸 수 있다
    /// (<see cref="Settings.GetSpecMaxBytes(string,int)"/> 참고).
    /// </summary>
    public static readonly IReadOnlyList<SpecArea> Areas =
    [
        new("교과활동",   "교과활동(세특)",       1500),  // 교과 세부능력 및 특기사항 (과목당)
        new("개인별세특", "개인별 세특",          1500),
        new("자율활동",   "자율활동",             1500),
        new("동아리활동", "동아리활동",           1500),
        new("봉사활동",   "봉사활동",             1500),
        new("진로활동",   "진로활동",             2100),
        new("종합의견",   "행동특성 및 종합의견", 1500),
    ];

    /// <summary>
    /// 학생부 영역별 최대 바이트 수(코드 기본값). 사용자 설정 오버라이드까지 반영하려면
    /// <see cref="Settings.GetSpecMaxBytes(string,int)"/> 를 쓸 것.
    /// </summary>
    public static int GetMaxBytes(string type)
    {
        foreach (var area in Areas)
            if (area.Key == type) return area.DefaultBytes;
        return 1500;   // 표에 없는 영역의 안전 기본값
    }

    // 미사용 메서드 제거 (2026-04-22):
    //   GetAreaDisplayName / IsOverLimit / GetByteInfo / GetRemainingBytes — 호출처 0건
    //   유지되는 공개 API: CountByte, GetMaxBytes, Areas
}

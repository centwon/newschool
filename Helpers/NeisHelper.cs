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

    /// <summary>
    /// 한도 초과 판정 — <b>경계는 "이하 허용"</b>이다. 즉 한도와 정확히 같은 바이트는 초과가 아니고
    /// 한도를 1바이트라도 넘을 때만 초과다(예: 500자=1500바이트 한도에서 1500 은 정상, 1501 은 초과).
    ///
    /// ⚠ 의도된 결정이다(2026-07-30 확인). NEIS 지침이 "500자까지 입력 가능"이라는 뜻이므로
    /// 500자를 온전히 쓸 수 있어야 한다. 예전에는 이 비교(<c>&gt;</c>)가 입력·일괄입력·HTML·PDF
    /// 6곳에 흩어져 있어 한 곳만 <c>&gt;=</c> 로 바뀌어도 알아채기 어려웠다 — 이 메서드로 모았다.
    /// </summary>
    public static bool IsOverLimit(int byteCount, int maxBytes) => byteCount > maxBytes;

    /// <summary>
    /// 학생부 영역 정의 — 키(저장값), 표시 이름, 코드 기본 바이트, 학기별 작성 여부.
    /// </summary>
    /// <param name="IsSemesterScoped">
    /// 학기별로 따로 작성하는 영역인가. 교과 세부능력 및 특기사항(<c>교과활동</c>)만 true 다.
    /// 개인별세특은 교과 영역이지만 <b>학년 단위</b>이므로 false(=<c>Semester 0</c>).
    /// </param>
    public sealed record SpecArea(string Key, string Label, int DefaultBytes, bool IsSemesterScoped = false);

    /// <summary>
    /// 학생부 영역 단일 정의표. 코드 기본값은 <b>2027·2028~ 입시 기준</b>이다
    /// (두 기준의 글자 수가 같다). 2026 입시는 진로활동 700자·종합의견 500자·봉사활동 250자로
    /// 달랐으므로, 그 학년도 기록을 다루려면 설정에서 <b>학년도별 오버라이드</b>를 쓰면 된다.
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
        // 교과 세특만 학기별(과목당·학기당). 나머지는 전부 학년 단위.
        // 기본값은 2027·2028~ 입시 기준(두 기준의 값이 동일). 한글 1자 = 3바이트.
        new("교과활동",   "교과활동(세특)",       1500, IsSemesterScoped: true),  // 과목별 500자
        new("개인별세특", "개인별 세특",          1500),   // 500자. 교과 영역이지만 학년 단위
        new("자율활동",   "자율활동",             1500),   // 연간 500자
        new("동아리활동", "동아리활동",           1500),   // 연간 500자
        new("봉사활동",   "봉사활동",              150),   // 실적 50자
        new("진로활동",   "진로활동",             1500),   // 연간 500자 (2026 입시는 700자였다)
        new("종합의견",   "행동특성 및 종합의견",  900),   // 연간 300자 (2026 입시는 500자였다)
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

    /// <summary>
    /// 해당 영역이 학기별로 작성되는가(= <c>StudentSpecial.Semester</c> 에 1·2 를 넣어야 하는가).
    /// false 면 학년 단위이므로 0 을 넣는다. 판단 근거는 <see cref="Areas"/> 정의표 하나뿐이다.
    /// </summary>
    public static bool IsSemesterScoped(string type)
    {
        foreach (var area in Areas)
            if (area.Key == type) return area.IsSemesterScoped;
        return false;   // 모르는 영역은 안전하게 학년 단위
    }

    // 미사용 메서드 제거 (2026-04-22):
    //   GetAreaDisplayName / IsOverLimit / GetByteInfo / GetRemainingBytes — 호출처 0건
    //   유지되는 공개 API: CountByte, GetMaxBytes, Areas
}

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
    /// <param name="TitleLabel">
    /// <c>StudentSpecial.Title</c> 칸이 이 영역에서 담는 것. null 이면 미사용.
    /// 교과 세특은 과목명, 진로활동은 희망분야를 담는다(영역마다 의미가 다르므로 여기에 적어둔다).
    /// </param>
    /// <param name="TitleCountsInBytes">
    /// 바이트 한도 계산에 <c>Title</c> 도 포함되는가. 진로활동은 <b>희망분야 + 특기사항</b> 을
    /// 합쳐 한도(연간 500자)를 적용하므로 true 다. 교과 세특의 과목명은 분량에 포함되지 않는다.
    /// </param>
    public sealed record SpecArea(
        string Key, string Label, int DefaultBytes,
        bool IsSemesterScoped = false,
        string? TitleLabel = null,
        bool TitleCountsInBytes = false);

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
        new("교과활동",   "교과활동(세특)",       1500, IsSemesterScoped: true, TitleLabel: "과목명"),  // 과목별 500자
        new("개인별세특", "개인별 세특",          1500, TitleLabel: "과목명"),   // 500자. 교과 영역이지만 학년 단위
        new("자율활동",   "자율활동",             1500),   // 연간 500자
        new("동아리활동", "동아리활동",           1500),   // 연간 500자
        new("봉사활동",   "봉사활동",              150),   // 실적 50자
        // 진로활동은 희망분야와 특기사항으로 구성되고 둘을 합쳐 500자다 → Title 도 한도에 포함
        new("진로활동",   "진로활동",             1500, TitleLabel: "희망분야", TitleCountsInBytes: true),
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

    /// <summary>이 영역에서 <c>Title</c> 칸이 담는 것의 이름(입력 라벨용). 미사용이면 null.</summary>
    public static string? GetTitleLabel(string type)
    {
        foreach (var area in Areas)
            if (area.Key == type) return area.TitleLabel;
        return null;
    }

    /// <summary>바이트 한도 계산에 <c>Title</c> 도 합산하는 영역인가(진로활동).</summary>
    public static bool TitleCountsInBytes(string type)
    {
        foreach (var area in Areas)
            if (area.Key == type) return area.TitleCountsInBytes;
        return false;
    }

    /// <summary>
    /// 영역 기준으로 실제 사용 바이트를 계산한다.
    /// 진로활동은 <b>희망분야(Title) + 특기사항(Content)</b> 을 합산하고, 그 외는 특기사항만 센다.
    /// 입력 화면·일괄 입력·뷰모델·엑셀·HTML·PDF 가 모두 이 메서드를 쓰므로 기준이 어긋나지 않는다.
    /// </summary>
    public static int CountSpecBytes(string type, string? title, string? content)
        => TitleCountsInBytes(type)
            ? CountByte(title ?? string.Empty) + CountByte(content ?? string.Empty)
            : CountByte(content ?? string.Empty);

    /// <summary>
    /// 목록·인쇄·내보내기의 "과목/분야" 칸에 넣을 값. 영역마다 담기는 정보가 달라서 한 칸에 모은다.
    ///  · 교과활동   → 과목명 + 학기("국어 (2학기)") — 교과 세특만 학기별이라 학기를 함께 노출
    ///  · 개인별세특 → 과목명만(학년 단위)
    ///  · 진로활동   → 희망분야(Title). 분량이 특기사항과 합산되므로 함께 보여야 한다
    ///  · 그 외      → 빈칸
    ///
    /// 화면 목록에만 이 규칙이 있고 인쇄·엑셀·HTML 은 <c>SubjectName</c> 만 찍어서,
    /// <b>진로활동 희망분야가 출력물에서 통째로 빠지고</b>(바이트는 합산돼 숫자가 안 맞아 보였다)
    /// 교과활동 학기도 사라졌다. 기준을 한 곳으로 모은다.
    /// </summary>
    public static string BuildSubjectDisplay(string type, string? subjectName, string? title, int semester)
    {
        if (TitleCountsInBytes(type))
            return title ?? string.Empty;

        var subject = subjectName ?? string.Empty;
        if (IsSemesterScoped(type) && semester > 0)
            return string.IsNullOrEmpty(subject) ? $"{semester}학기" : $"{subject} ({semester}학기)";

        return subject;
    }

    // 미사용 메서드 제거 (2026-04-22):
    //   GetAreaDisplayName / IsOverLimit / GetByteInfo / GetRemainingBytes — 호출처 0건
    //   유지되는 공개 API: CountByte, GetMaxBytes, Areas
}

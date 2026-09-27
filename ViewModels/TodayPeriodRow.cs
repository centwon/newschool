using Microsoft.UI.Xaml;
using NewSchool.Models;

namespace NewSchool.ViewModels;

/// <summary>
/// 오늘 화면 시간표 카드의 한 줄 = 한 교시. 왼쪽이 내 수업, 오른쪽이 우리 반(담임일 때만).
///
/// <para>예전에는 두 열이 따로 노는 목록 둘이었다. 내 수업은 수업 있는 교시만, 우리 반은 모든
/// 교시를 늘어놓아서, 첫 줄의 "2교시 영어" 옆에 "1교시 국어" 가 붙었다. 한 줄을 한 교시로
/// 묶어 좌우가 같은 시각을 가리키게 했다(2026-09-28). 공강도 빈 줄로 보인다 — 그 줄을 눌러
/// 보강·대강을 넣는다.</para>
///
/// 줄은 시간표가 바뀌면 통째로 다시 만든다 — 바뀌는 값은 현재 교시 강조뿐이다.
/// </summary>
public sealed class TodayPeriodRow : NotifyPropertyChangedBase
{
    private bool _isCurrentPeriod;

    public int Period { get; init; }

    /// <summary>표식을 붙인 과목명 — "(휴)영어". 빈 교시면 빈 문자열.</summary>
    public string Subject { get; init; } = string.Empty;

    public string Room { get; init; } = string.Empty;

    public bool IsCancelled { get; init; }

    /// <summary>그 교시에 수업 일지를 써 두었다 — 공책 표시</summary>
    public bool HasJournal { get; init; }

    /// <summary>줄의 툴팁 (변경 사유 메모 포함)</summary>
    public string Tooltip { get; init; } = string.Empty;

    /// <summary>우리 반 과목 · 담당 교사</summary>
    public string ClassSubject { get; init; } = string.Empty;
    public string ClassTeacher { get; init; } = string.Empty;

    /// <summary>우리 반 열의 너비 — 담임이 아니면 0 (머리의 열과 맞춘다)</summary>
    public GridLength ClassColumnWidth { get; init; } = new(1, GridUnitType.Star);

    public bool IsCurrentPeriod
    {
        get => _isCurrentPeriod;
        set => SetProperty(ref _isCurrentPeriod, value);
    }
}

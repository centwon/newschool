using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NewSchool.Scheduler;

/// <summary>
/// KCalendarList 모델 — Google Calendar의 캘린더 목록 대응
/// KtaskList(할일 목록)와 같은 이름을 공유하지만 별도 모델로 관리
/// 수업 / 담임 / 행정 / 개인 등 카테고리별 색상 포함
/// </summary>
public class KCalendarList : INotifyPropertyChanged
{
    private string _title = string.Empty;
    private string _color = "#4285F4"; // Google Blue 기본

    public int No { get; set; } = -1;

    /// <summary>Google Calendar ID (동기화용)</summary>
    public string GoogleId { get; set; } = string.Empty;

    /// <summary>캘린더 이름 (수업, 담임, 행정, 개인 등)</summary>
    public string Title
    {
        get => _title;
        set { if (_title != value) { _title = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// 캘린더 색상 (#RRGGBB)
    /// 이벤트에 별도 ColorId가 없으면 이 색상을 사용
    /// </summary>
    public string Color
    {
        get => _color;
        set { if (_color != value) { _color = value; OnPropertyChanged(); } }
    }

    /// <summary>정렬 순서</summary>
    public int SortOrder { get; set; }

    /// <summary>시스템 기본 캘린더 여부 (삭제 불가)</summary>
    public bool IsDefault { get; set; }

    /// <summary>달력에 표시 여부</summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>마지막 동기화 시각 (UTC 문자열)</summary>
    public string Updated { get; set; } = string.Empty;

    /// <summary>Google 동기화 모드 (None / OneWay / TwoWay)</summary>
    public string SyncMode { get; set; } = "None";

    /// <summary>Google incremental sync token (events.list syncToken)</summary>
    public string SyncToken { get; set; } = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public override string ToString() => Title;
}

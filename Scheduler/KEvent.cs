using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Windows.UI.Text;

namespace NewSchool.Scheduler;

/// <summary>
/// KEvent 모델 — Google Calendar Events 대응
/// INotifyPropertyChanged 구현으로 UI 바인딩 지원
/// ✅ Ktask 통합: ItemType="task"인 경우 할 일로 동작
/// </summary>
[WinRT.GeneratedBindableCustomProperty]
public partial class KEvent : INotifyPropertyChanged
{
    #region Fields

    private string _title = string.Empty;
    private string _notes = string.Empty;
    private DateTime _start;
    private DateTime _end;
    private bool _isAllday;
    private string _status = "confirmed";
    private string _colorId = string.Empty;
    private int _calendarId;
    private bool _isDone;

    #endregion

    #region Properties — DB 컬럼

    public int No { get; set; } = -1;

    /// <summary>Google Calendar Event ID (동기화용)</summary>
    public string GoogleId { get; set; } = string.Empty;

    /// <summary>소속 캘린더 ID (KCalendarList.No 대응)</summary>
    public int CalendarId
    {
        get => _calendarId;
        set { if (_calendarId != value) { _calendarId = value; OnPropertyChanged(); } }
    }

    /// <summary>이벤트 제목</summary>
    public string Title
    {
        get => _title;
        set { if (_title != value) { _title = value; OnPropertyChanged(); } }
    }

    /// <summary>이벤트 설명/메모</summary>
    public string Notes
    {
        get => _notes;
        set { if (_notes != value) { _notes = value; OnPropertyChanged(); } }
    }

    /// <summary>시작 일시</summary>
    public DateTime Start
    {
        get => _start;
        set { if (_start != value) { _start = value; OnPropertyChanged(); } }
    }

    /// <summary>종료 일시</summary>
    public DateTime End
    {
        get => _end;
        set { if (_end != value) { _end = value; OnPropertyChanged(); } }
    }

    /// <summary>종일 이벤트 여부</summary>
    public bool IsAllday
    {
        get => _isAllday;
        set { if (_isAllday != value) { _isAllday = value; OnPropertyChanged(); } }
    }

    /// <summary>장소</summary>
    public string Location { get; set; } = string.Empty;

    /// <summary>상태: confirmed / tentative / cancelled</summary>
    public string Status
    {
        get => _status;
        set { if (_status != value) { _status = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// 이벤트별 색상 ID (Google Calendar 색상 번호 1~11, 빈 문자열이면 카테고리 색상 사용)
    /// </summary>
    public string ColorId
    {
        get => _colorId;
        set { if (_colorId != value) { _colorId = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayColor)); } }
    }

    /// <summary>반복 규칙 (RRULE 문자열, 빈 문자열이면 반복 없음)</summary>
    public string Recurrence { get; set; } = string.Empty;

    /// <summary>마지막 수정 일시 (UTC 문자열)</summary>
    public string Updated { get; set; } = string.Empty;

    /// <summary>작성자</summary>
    public string User { get; set; } = string.Empty;

    // ── Ktask 통합 프로퍼티 ──────────────────────

    /// <summary>항목 유형: "event" (일정) 또는 "task" (할 일)</summary>
    public string ItemType { get; set; } = "event";

    /// <summary>할 일 완료 여부 (PropertyChanged 지원, ItemType="task"용)</summary>
    public bool IsDone
    {
        get => _isDone;
        set
        {
            if (_isDone != value)
            {
                _isDone = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TextDecorations));
            }
        }
    }

    /// <summary>완료 일시 (UTC 문자열, ItemType="task"용)</summary>
    public string Completed { get; set; } = string.Empty;

    #endregion

    #region Computed Properties (UI 바인딩용)

    /// <summary>
    /// 표시용 색상 문자열 (#RRGGBB)
    /// ColorId가 있으면 Google Calendar 색상, 없으면 빈 문자열 (카테고리 색상 사용)
    /// </summary>
    public string DisplayColor => ColorIdToHex(ColorId);

    /// <summary>
    /// 달력 셀 표시용 시간 문자열
    /// - 종일: 빈 문자열
    /// - 시간 있음: "HH:mm"
    /// </summary>
    public string TimeLabel => IsAllday ? string.Empty : Start.ToString("HH:mm");

    /// <summary>이벤트가 취소 상태인지</summary>
    public bool IsCancelled => Status == "cancelled";

    /// <summary>이벤트가 임시(미확정) 상태인지</summary>
    public bool IsTentative => Status == "tentative";

    /// <summary>할 일인지 여부</summary>
    public bool IsTaskItem => ItemType == "task";

    /// <summary>
    /// 텍스트 장식 (완료된 할 일은 취소선 표시)
    /// x:Bind에서 직접 바인딩 가능
    /// </summary>
    public TextDecorations TextDecorations => IsDone
        ? TextDecorations.Strikethrough
        : TextDecorations.None;

    #endregion

    #region INotifyPropertyChanged

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    #endregion

    #region Static Helpers

    /// <summary>
    /// Google Calendar ColorId를 HEX 색상 코드로 변환
    /// https://developers.google.com/calendar/api/v3/reference/colors
    /// </summary>
    public static string ColorIdToHex(string colorId) => colorId switch
    {
        "1"  => "#AC725E",  // Tomato
        "2"  => "#D06B64",  // Flamingo
        "3"  => "#F83A22",  // Tangerine
        "4"  => "#FA573C",  // Banana
        "5"  => "#FF7537",  // Sage
        "6"  => "#FFAD46",  // Basil
        "7"  => "#42D692",  // Peacock
        "8"  => "#16A765",  // Blueberry
        "9"  => "#7BD148",  // Lavender
        "10" => "#B3DC6C",  // Grape
        "11" => "#9E69AF",  // Graphite
        _    => string.Empty
    };

    #endregion

    public override string ToString() => $"{Title} ({Start:yyyy-MM-dd HH:mm}~{End:HH:mm})";
}

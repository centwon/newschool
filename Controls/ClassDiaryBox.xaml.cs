using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Services;
using NewSchool.ViewModels;

namespace NewSchool.Controls;

/// <summary>
/// 학급일지 입력 컨트롤 (간소화 버전)
/// - 출결 (결석/지각/조퇴)
/// - 메모
/// - 알림장
/// 
/// 학생 생활 로그는 PageDiary에서 직접 관리
/// </summary>
public sealed partial class ClassDiaryBox : UserControl
{
    public ClassDiaryViewModel ViewModel { get; }

    private bool _isChanged = false;

    /// <summary>마지막 입력 후 이만큼 지나면 자동 저장한다.</summary>
    private const int AutoSaveDelayMs = 3000;

    private readonly DispatcherQueueTimer _autoSaveTimer;

    public ClassDiaryBox()
    {
        this.InitializeComponent();
        ViewModel = new ClassDiaryViewModel();
        
        // JoditEditor TextChanged 이벤트 구독
        NoticeBox.TextChanged += NoticeBox_TextChanged;

        // ⚠ 저장 시점을 앞당기는 장치다.
        //    예전에는 저장이 날짜 변경·목록 이동·화면 언로드에서만 일어나서, 알림장을 쓰고
        //    앱을 그냥 닫으면 내용이 사라졌다. 창 닫기 훅으로는 못 막는다 — 핸들러가 동기라
        //    비동기 DB 저장이 프로세스 종료와 경합한다.
        //    그래서 (1) 입력칸에서 포커스가 빠질 때, (2) 입력이 멎고 3초 뒤에 저장한다.
        _autoSaveTimer = DispatcherQueue.CreateTimer();
        _autoSaveTimer.Interval = TimeSpan.FromMilliseconds(AutoSaveDelayMs);
        _autoSaveTimer.IsRepeating = false;
        _autoSaveTimer.Tick += async (_, _) => await SaveDiaryAsync();

        foreach (var box in new[] { TBoxAbsent, TBoxLate, TBoxLeaveEarly, TBoxMemo })
            box.LostFocus += async (_, _) => await SaveDiaryAsync();

        this.Unloaded += OnUnloaded;
    }

    /// <summary>입력이 있을 때마다 자동 저장 타이머를 다시 센다(디바운스).</summary>
    private void RestartAutoSaveTimer()
    {
        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();
    }

    /// <summary>
    /// 특정 날짜의 학급일지 로드
    /// </summary>
    public async Task LoadDiaryAsync(int grade, int classNumber, DateTime date)
    {
        if (_isChanged)
        {
            await SaveDiaryAsync();
        }

        System.Diagnostics.Debug.WriteLine($"[ClassDiaryBox] LoadDiaryAsync: {grade}학년 {classNumber}반, {date:yyyy-MM-dd}");
        
        await ViewModel.LoadDiaryAsync(grade, classNumber, date);
        
        System.Diagnostics.Debug.WriteLine($"[ClassDiaryBox] ViewModel 로드 완료: No={ViewModel.No}, Absent={ViewModel.Absent}, Memo={ViewModel.Memo?.Length ?? 0} chars");
        System.Diagnostics.Debug.WriteLine($"[ClassDiaryBox] Notice 길이: {ViewModel.Notice?.Length ?? 0} chars");
        
        // 알림장 내용 로드
        NoticeBox.Text = ViewModel.Notice ?? string.Empty;
        UpdateNoticePreview();

        System.Diagnostics.Debug.WriteLine($"[ClassDiaryBox] NoticeBox.Text 설정 완료");
        
        // 현재 작업 학년도로 시간표 로드
        LoadTimetable(grade, classNumber, Settings.WorkYear);
        _isChanged = false;
        ResetTextBoxStyles();
    }
    /// <summary>
    /// 시간표 로드
    /// </summary>
    private async void LoadTimetable(int grade, int classNumber, int year)
    {
        if (grade == 0 || classNumber == 0 || year == 0) return;

        // async void 이므로 예외가 전역으로 전파되어 앱이 종료될 수 있어 방어
        try
        {
            using var service = new TimetableService(SchoolDatabase.DbPath);
            var timeset = await service.GetClassTimetableAsync(
                Settings.SchoolCode,
                year,
                Settings.WorkSemester,
                grade,
                classNumber);

            // 시간표 표시
            ClassTimeTable.DataContext = timeset;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ClassDiaryBox] 시간표 로드 실패: {ex.Message}");
        }
    }
    /// <summary>
    /// 현재 학급일지 저장.
    ///
    /// <para>⚠ 이 메서드는 <b>날짜 변경·목록 이동·화면 언로드 시 자동으로</b> 불린다.
    /// 그 호출부들이 전부 <c>async void</c> 라서, 예전에는 저장이 실패하면 예외가 아무에게도
    /// 잡히지 않고 사용자는 저장된 줄 알았다. 그래서 실패를 여기서 처리한다 —
    /// 변경 표시(<c>_isChanged</c>)를 유지해 다음 기회에 다시 저장되게 하고 사용자에게 알린다.</para>
    /// </summary>
    public async Task SaveDiaryAsync()
    {
        if (!_isChanged)
        {
            System.Diagnostics.Debug.WriteLine("[ClassDiaryBox] SaveDiaryAsync: 변경사항 없음, 저장 스킵");
            return;
        }

        System.Diagnostics.Debug.WriteLine($"[ClassDiaryBox] SaveDiaryAsync 시작: No={ViewModel.No}, Absent={ViewModel.Absent}, Memo={ViewModel.Memo?.Length ?? 0} chars");
        
        // 알림장 내용 저장
        ViewModel.Notice = NoticeBox.Text;
        
        System.Diagnostics.Debug.WriteLine($"[ClassDiaryBox] Notice 저장: {ViewModel.Notice?.Length ?? 0} chars");

        try
        {
            await ViewModel.SaveDiaryAsync();
        }
        catch (Exception ex)
        {
            // _isChanged 를 그대로 두어 편집 내용을 잃지 않는다
            System.Diagnostics.Debug.WriteLine($"[ClassDiaryBox] 학급일지 저장 실패: {ex}");
            await UserErrorReporter.ReportAsync("학급일지 저장", ex);
            return;
        }

        System.Diagnostics.Debug.WriteLine($"[ClassDiaryBox] SaveDiaryAsync 완료: No={ViewModel.No}");
        
        _isChanged = false;
        _autoSaveTimer.Stop();
        ResetTextBoxStyles();
    }

    /// <summary>
    /// 텍스트박스 스타일 초기화
    /// </summary>
    private void ResetTextBoxStyles()
    {
        TBoxAbsent.FontStyle = Windows.UI.Text.FontStyle.Normal;
        TBoxLate.FontStyle = Windows.UI.Text.FontStyle.Normal;
        TBoxLeaveEarly.FontStyle = Windows.UI.Text.FontStyle.Normal;
        TBoxMemo.FontStyle = Windows.UI.Text.FontStyle.Normal;
    }

    /// <summary>
    /// 텍스트박스 변경 표시
    /// </summary>
    private void MarkTextBoxAsChanged(TextBox textBox)
    {
        _isChanged = true;
        textBox.FontStyle = Windows.UI.Text.FontStyle.Italic;
        RestartAutoSaveTimer();
    }

    #region 이벤트 핸들러

    /// <summary>
    /// 알림장 (JoditEditor) 텍스트 변경 - ReadOnly 모드에서는 호출되지 않음
    /// </summary>
    private void NoticeBox_TextChanged(object? sender, string e)
    {
        _isChanged = true;
        UpdateNoticePreview();
        RestartAutoSaveTimer();
    }

    /// <summary>
    /// 알림장 미리보기 텍스트 업데이트
    /// </summary>
    private void UpdateNoticePreview()
    {
        string content = NoticeBox.Text ?? string.Empty;

        // HTML 태그 제거하여 순수 텍스트만 추출
        string plainText = StripHtmlTags(content);

        // 미리보기 텍스트 설정 (비어있으면 안내 메시지)
        if (string.IsNullOrWhiteSpace(plainText))
        {
            TxtNoticePreview.Text = "(내용 없음)";
        }
        else
        {
            // 최대 50자까지만 표시
            TxtNoticePreview.Text = plainText.Length > 50
                ? plainText.Substring(0, 50) + "..."
                : plainText;
        }
    }

    /// <summary>
    /// HTML 태그 제거
    /// </summary>
    private static string StripHtmlTags(string html)
    {
        if (string.IsNullOrEmpty(html))
            return string.Empty;

        // HTML 태그 제거
        string text = Regex.Replace(html, @"<[^>]+>", " ");
        // HTML 엔티티 변환
        text = System.Net.WebUtility.HtmlDecode(text);
        // 연속 공백 제거
        text = Regex.Replace(text, @"\s+", " ");

        return text.Trim();
    }

    /// <summary>
    /// 출결 텍스트 변경
    /// </summary>
    private void OnAttendanceTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            MarkTextBoxAsChanged(textBox);
        }
    }

    /// <summary>
    /// 메모 텍스트 변경
    /// </summary>
    private void OnMemoTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            MarkTextBoxAsChanged(textBox);
        }
    }

    /// <summary>
    /// 알림장 전체 편집 버튼
    /// </summary>
    private async void BtnNoticeEdit_Click(object sender, RoutedEventArgs e)
    {
        // RichTextEditorWin 으로 전체 화면 편집
        var editorWin = new RichTextEditorWin(
            "알림장 편집",
            BuildNoticeHeaderHtml() + "<br>" + NoticeBox.Text,
            RichTextEditor.EditorMode.Full);

        editorWin.SetSize(1000, 800);

        bool result = await editorWin.ShowDialogAsync();

        if (result)
        {
            // 헤더 테이블 제거 후 NoticeBox에 적용
            string content = RemoveNoticeHeaderHtml(editorWin.Text);
            NoticeBox.Text = content;
            _isChanged = true;
            UpdateNoticePreview();
            RestartAutoSaveTimer();
        }
    }

    /// <summary>
    /// 알림장 헤더 HTML 생성 — "알림장"(16px 굵게 가운데)과 날짜(14px 굵게 오른쪽) 두 줄.
    /// 표를 쓰지 않는다(2026-07-31 변경).
    /// </summary>
    private string BuildNoticeHeaderHtml()
    {
        string dateStr = ViewModel.Date.ToString("yyyy년 M월 d일(ddd)");
        return $@"<div data-notice-header='true'>
                <p style='text-align:center;margin:0;'><span style='font-size:16px;'><strong>알림장</strong></span></p>
                <p style='text-align:right;margin:0;'><span style='font-size:14px;'><strong>{dateStr}</strong></span></p>
            </div>";
    }

    /// <summary>
    /// 알림장 헤더 HTML 제거 (정규식 사용, Native AOT 호환)
    /// </summary>
    internal static string RemoveNoticeHeaderHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        // 1) 현재 형식: data-notice-header 를 단 div (내부에 div 를 두지 않으므로 비탐욕 매칭으로 충분)
        html = Regex.Replace(html, @"<div[^>]*data-notice-header[^>]*>.*?</div>",
            string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase);

        // 2) 구 형식: data-notice-header 를 단 표 (예전에 저장된 알림장 호환)
        html = Regex.Replace(html, @"<table[^>]*data-notice-header[^>]*>.*?</table>",
            string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase);

        // 3) 구 형식 fallback: "알 림 장" 이 든 표 (편집기가 data-* 속성을 지운 경우)
        html = Regex.Replace(html,
            @"<table[^>]*>\s*<tbody>\s*<tr>\s*<td[^>]*>\s*<span[^>]*>알\s*림\s*장</span>\s*</td>\s*</tr>.*?</table>",
            string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase);

        // 4) 현재 형식 fallback: 맨 앞의 "알림장" 문단과 바로 뒤 날짜 문단
        //    (편집기가 data-* 속성이나 감싼 div 를 지우면 3)까지로는 안 걸린다)
        html = Regex.Replace(html,
            @"^\s*<p[^>]*>(?:(?!</p>).)*?알\s*림\s*장(?:(?!</p>).)*?</p>\s*" +
            @"(?:<p[^>]*>(?:(?!</p>).)*?\d{4}년\s*\d{1,2}월\s*\d{1,2}일(?:(?!</p>).)*?</p>)?",
            string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase);

        // 앞뒤 <br> 태그 정리
        html = Regex.Replace(html, @"^(\s*<br\s*/?>\s*)+", string.Empty, RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"(\s*<br\s*/?>\s*)+$", string.Empty, RegexOptions.IgnoreCase);

        return html.Trim();
    }

    /// <summary>
    /// 언로드 시 자동 저장
    /// </summary>
    private async void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // 컨트롤이 사라진 뒤에 타이머가 뜨지 않도록 먼저 멈춘다
        _autoSaveTimer.Stop();

        if (_isChanged)
        {
            await SaveDiaryAsync();
        }
    }

    #endregion
}

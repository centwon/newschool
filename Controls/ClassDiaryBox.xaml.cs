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
        
        // 알림장 편집기(RichTextEditor) TextChanged 이벤트 구독
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
    /// 특정 날짜의 학급일지 로드.
    ///
    /// <para>⚠ 학년도·학기를 <b>반드시 부르는 쪽에서 받는다.</b> 예전에는 여기서
    /// <c>Settings.WorkYear</c>·<c>Settings.WorkSemester</c> 를 직접 읽어서, 화면 위쪽
    /// 피커로 지난 학년도를 펼쳐 놓아도 일지만 <b>올해 행</b>을 읽고 썼다.</para>
    /// </summary>
    public async Task LoadDiaryAsync(int year, int semester, int grade, int classNumber, DateTime date)
    {
        if (_isChanged)
        {
            await SaveDiaryAsync();
        }

        System.Diagnostics.Debug.WriteLine($"[ClassDiaryBox] LoadDiaryAsync: {year}학년도 {semester}학기 {grade}학년 {classNumber}반, {date:yyyy-MM-dd}");
        
        await ViewModel.LoadDiaryAsync(year, semester, grade, classNumber, date);
        
        System.Diagnostics.Debug.WriteLine($"[ClassDiaryBox] ViewModel 로드 완료: No={ViewModel.No}, Absent={ViewModel.Absent}, Memo={ViewModel.Memo?.Length ?? 0} chars");
        System.Diagnostics.Debug.WriteLine($"[ClassDiaryBox] Notice 길이: {ViewModel.Notice?.Length ?? 0} chars");
        
        // 알림장 내용 로드
        NoticeBox.Text = ViewModel.Notice ?? string.Empty;
        UpdateNoticePreview();

        System.Diagnostics.Debug.WriteLine($"[ClassDiaryBox] NoticeBox.Text 설정 완료");
        
        // 시간표도 일지와 같은 학년도·학기로 읽는다 — 둘이 갈리면 지난 학년도 일지 옆에
        // 올해 시간표가 붙는다.
        LoadTimetable(year, semester, grade, classNumber);
        _isChanged = false;
        ResetTextBoxStyles();
    }
    /// <summary>
    /// 시간표 로드
    /// </summary>
    private async void LoadTimetable(int year, int semester, int grade, int classNumber)
    {
        if (grade == 0 || classNumber == 0 || year == 0 || semester == 0) return;

        // async void 이므로 예외가 전역으로 전파되어 앱이 종료될 수 있어 방어
        try
        {
            using var service = new TimetableService(SchoolDatabase.DbPath);
            var timeset = await service.GetClassTimetableAsync(
                Settings.SchoolCode,
                year,
                semester,
                grade,
                classNumber);

            // 시간표 표시
            ClassTimeTable.DataContext = timeset;

            // ⚠ LoadFailed 를 반드시 본다. 서비스는 조회에 실패해도 <b>빈 시간표</b>를
            //    돌려주므로(호출부가 화면을 못 그리는 일이 없도록), 이걸 안 보면 DB 오류가
            //    "이 반은 시간표가 없습니다" 와 똑같이 보인다. 그 구분을 하라고 만든
            //    플래그인데 그동안 읽는 곳이 한 곳도 없었다.
            ShowTimetableFailure(timeset.LoadFailed ? timeset.ErrorMessage : null);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ClassDiaryBox] 시간표 로드 실패: {ex.Message}");
            ShowTimetableFailure(ex.Message);
        }
    }
    /// <summary>
    /// 시간표를 못 읽었다는 사실을 제목 줄에 드러낸다(<paramref name="reason"/> 가 null 이면 되돌린다).
    ///
    /// <para>대화상자로 알리지 않는 이유 — 이 컨트롤은 <b>날짜를 옮길 때마다</b> 시간표를
    /// 다시 읽으므로, 오류가 이어지면 대화상자가 계속 뜬다. 자리에 남는 문구가 낫다.</para>
    /// </summary>
    private void ShowTimetableFailure(string? reason)
    {
        bool failed = !string.IsNullOrWhiteSpace(reason);

        TxtTimetableTitle.Text = failed ? "학급 시간표 — 불러오지 못했습니다" : "학급 시간표";
        ToolTipService.SetToolTip(TxtTimetableTitle, failed ? reason : null);
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
    /// 알림장 (RichTextEditor) 텍스트 변경 - ReadOnly 모드에서는 호출되지 않음
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

        // 헤더("알림장" + 날짜)는 모든 알림장에 똑같이 들어가므로 요약에서 뺀다.
        // 안 그러면 50자 미리보기의 앞부분을 매번 같은 글자가 차지한다.
        int headerEnd = SkipNoticeHeader(content);

        // HTML 태그 제거하여 순수 텍스트만 추출
        string plainText = StripHtmlTags(content[headerEnd..]);

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
    /// 헤더 블록이 맨 앞에 있으면 그 뒤 위치를 돌려준다(없으면 0).
    /// 표시용 요약에만 쓰며, 저장 내용은 건드리지 않는다.
    /// </summary>
    private static int SkipNoticeHeader(string html)
    {
        int marker = html.IndexOf(NoticeHeaderMarker, StringComparison.OrdinalIgnoreCase);
        if (marker < 0) return 0;

        int close = html.IndexOf("</div>", marker, StringComparison.OrdinalIgnoreCase);
        return close < 0 ? 0 : close + "</div>".Length;
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
        // 헤더는 "처음 작성할 때"만 넣는다. 그 뒤로는 본문의 일부라 편집창이 손대지 않는다.
        //
        // ⚠ 예전에는 열 때마다 헤더를 붙였다가 저장할 때 정규식으로 떼어냈다. 그 정규식이
        //    어긋나면 헤더가 본문에 눌러앉아 편집할 때마다 중복되거나 본문 첫 줄이 잘렸다.
        //    헤더를 본문에 두면 그 위험이 통째로 사라진다 — 일지는 (학년도·학년·반·날짜)로
        //    고정된 행이라 날짜가 나중에 어긋날 일도 없다.
        string current = NoticeBox.Text ?? string.Empty;
        string initialHtml = ShouldInsertNoticeHeader(current)
            ? BuildNoticeHeaderHtml() + current
            : current;

        // ⚠ RichTextEditorWin.ShowDialogAsync 는 이름과 달리 모달이 아니다 — 창을 띄우고
        //    닫힘을 기다릴 뿐이라 그동안 뒤의 일지 화면이 그대로 조작된다. 편집하는 사이에
        //    날짜나 학급을 바꾸면 이 상자에는 이미 다른 일지가 올라와 있고, 그 위에 편집
        //    결과를 얹으면 엉뚱한 날짜의 알림장이 되면서 원래 있던 알림장이 사라진다.
        //    열 때의 대상을 적어 두고 돌아와서 같은지 본다.
        var target = (ViewModel.Grade, ViewModel.Class, ViewModel.Date.Date);

        var editorWin = new RichTextEditorWin(
            "알림장 편집",
            initialHtml,
            RichTextEditor.EditorMode.Full);

        editorWin.SetSize(1000, 800);

        bool result = await editorWin.ShowDialogAsync();

        if (!result) return;

        if (target != (ViewModel.Grade, ViewModel.Class, ViewModel.Date.Date))
        {
            await MessageBox.ShowAsync(
                $"편집하는 사이에 화면이 {ViewModel.Grade}학년 {ViewModel.Class}반 " +
                $"{ViewModel.Date:M월 d일} 일지로 바뀌어서, 고친 알림장을 적용하지 않았습니다.\n" +
                $"{target.Item1}학년 {target.Item2}반 {target.Item3:M월 d일} 로 돌아가 다시 편집해 주세요.",
                "알림장 적용 안 됨");
            return;
        }

        // 편집 결과를 그대로 쓴다(헤더도 본문의 일부다)
        NoticeBox.Text = editorWin.Text;
        _isChanged = true;
        UpdateNoticePreview();
        RestartAutoSaveTimer();
    }

    /// <summary>
    /// 알림장 헤더를 새로 넣어야 하는가. <b>내용이 비어 있을 때만</b> 넣는다.
    ///
    /// <para>이미 쓴 알림장을 다시 열 때는 넣지 않는다 — 헤더가 이미 본문에 있거나,
    /// 사용자가 일부러 지웠거나 둘 중 하나이고 어느 쪽이든 다시 붙이면 안 된다.
    /// 태그만 있고 글자가 없는 경우(<c>&lt;p&gt;&lt;br&gt;&lt;/p&gt;</c> 등)도 빈 것으로 본다.</para>
    /// </summary>
    /// <summary>헤더 블록임을 알아보는 표식(본문 요약에서 헤더를 건너뛸 때 쓴다).</summary>
    private const string NoticeHeaderMarker = "data-notice-header";

    internal static bool ShouldInsertNoticeHeader(string? html)
        => string.IsNullOrWhiteSpace(StripHtmlTags(html ?? string.Empty));

    /// <summary>
    /// 알림장 헤더 HTML 생성 — "알림장"(16px 굵게 가운데)과 날짜(14px 굵게 오른쪽) 두 줄.
    /// 표를 쓰지 않는다(2026-07-31 변경).
    /// </summary>
    private string BuildNoticeHeaderHtml()
    {
        string dateStr = ViewModel.Date.ToString("yyyy년 M월 d일(ddd)");
        // 헤더 뒤에 빈 문단(왼쪽 정렬, 14px)을 하나 붙여 바로 입력할 수 있게 한다.
        // 없으면 커서가 날짜 줄 끝에 놓여, 오른쪽 정렬로 날짜 뒤에 이어 붙는다.
        return $@"<div data-notice-header='true'>
                <p style='text-align:center;margin:0;'><span style='font-size:16px;'><strong>알림장</strong></span></p>
                <p style='text-align:right;margin:0;'><span style='font-size:14px;'><strong>{dateStr}</strong></span></p>
            </div>
            <p style='text-align:left;'><span style='font-size:14px;'><br></span></p>";
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

        // ⚠ 저장이 끝난 뒤에 놓아준다 — SaveDiaryAsync 가 그 서비스를 쓴다.
        //    ViewModel 이 IDisposable 인데 부르는 곳이 없어, 이 칸을 열 때마다 서비스가
        //    쥔 SQLite 연결이 풀로 돌아가지 못하고 GC 를 기다렸다.
        ViewModel.Dispose();
    }

    #endregion
}

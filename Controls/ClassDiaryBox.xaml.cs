using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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

    public ClassDiaryBox()
    {
        this.InitializeComponent();
        ViewModel = new ClassDiaryViewModel();
        
        // JoditEditor TextChanged 이벤트 구독
        NoticeBox.TextChanged += NoticeBox_TextChanged;
        
        this.Unloaded += OnUnloaded;
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
    /// <summary>
    /// 현재 학급일지 저장
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

        await ViewModel.SaveDiaryAsync();
        
        System.Diagnostics.Debug.WriteLine($"[ClassDiaryBox] SaveDiaryAsync 완료: No={ViewModel.No}");
        
        _isChanged = false;
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
    }

    #region 이벤트 핸들러

    /// <summary>
    /// 알림장 (JoditEditor) 텍스트 변경 - ReadOnly 모드에서는 호출되지 않음
    /// </summary>
    private void NoticeBox_TextChanged(object? sender, string e)
    {
        _isChanged = true;
        UpdateNoticePreview();
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
        // JoditEditorWin으로 전체 화면 편집
        var editorWin = new JoditEditorWin(
            "알림장 편집",
            BuildNoticeHeaderHtml() + "<br>" + NoticeBox.Text,
            JoditEditor.EditorMode.Full);

        editorWin.SetSize(1000, 800);

        bool result = await editorWin.ShowDialogAsync();

        if (result)
        {
            // 헤더 테이블 제거 후 NoticeBox에 적용
            string content = RemoveNoticeHeaderHtml(editorWin.Text);
            NoticeBox.Text = content;
            _isChanged = true;
            UpdateNoticePreview();
        }
    }

    /// <summary>
    /// 알림장 헤더 HTML 생성 (학년/반/날짜 정보)
    /// </summary>
    private string BuildNoticeHeaderHtml()
    {
        string dateStr = ViewModel.Date.ToString("yyyy년 M월 d일(ddd)");
        return $@"<table style='border-collapse:collapse;width:100%;border:0;' data-notice-header='true'>
                <tbody>
                    <tr>
                        <td style='width:100%;text-align:center;border:none;' colspan='2'>
                            <span style='font-size:18px;'>알 림 장</span>
                        </td>
                    </tr>
                    <tr>
                        <td style='width:50%;border:none;'>
                            <span style='font-size:16px;'>{ViewModel.Grade}학년 {ViewModel.Class}반</span>
                        </td>
                        <td style='width:50%;text-align:right;border:none;'>
                            <span style='font-size:16px;'>{dateStr}</span>
                        </td>
                    </tr>
                </tbody>
            </table>";
    }

    /// <summary>
    /// 알림장 헤더 HTML 제거 (정규식 사용, Native AOT 호환)
    /// </summary>
    private static string RemoveNoticeHeaderHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        // data-notice-header 속성이 있는 테이블 제거
        string pattern1 = @"<table[^>]*data-notice-header[^>]*>.*?</table>";
        html = Regex.Replace(html, pattern1, string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase);

        // "알 림 장" 텍스트가 포함된 테이블 제거 (fallback)
        string pattern2 = @"<table[^>]*>\s*<tbody>\s*<tr>\s*<td[^>]*>\s*<span[^>]*>알\s*림\s*장</span>\s*</td>\s*</tr>.*?</table>";
        html = Regex.Replace(html, pattern2, string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase);

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
        if (_isChanged)
        {
            await SaveDiaryAsync();
        }
    }

    #endregion
}

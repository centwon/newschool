using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.System;
using WinUIRichEditor.Controls;

namespace NewSchool.Controls;

/// <summary>
/// JoditEditor(WebView2) 대체용 어댑터. WinUIRichEditor 의 <see cref="RichEditorView"/>(Win2D 네이티브)를
/// 호스팅하면서 기존 JoditEditor 와 동일한 공개 API(<c>Text</c>·<c>Mode</c>·<c>PlainText</c>·<c>TextChanged</c>)를
/// 노출해 사용처를 드롭인 교체할 수 있게 한다.
/// </summary>
public sealed partial class RichTextEditor : UserControl, INotifyPropertyChanged, IDisposable
{
    /// <summary>JoditEditor.EditorMode 와 1:1 대응 (드롭인 교체용). Simple→WinUIRichEditor Basic.</summary>
    public enum EditorMode { ReadOnly, Simple, Full }

    private readonly RichEditorView _view;
    private bool _isUpdatingFromEditor;
    private bool _disposed;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>텍스트 내용이 변경되었을 때 발생 (HTML 본문 전달). JoditEditor 와 시그니처 동일.</summary>
    public event EventHandler<string>? TextChanged;

    /// <summary>에디터의 순수 텍스트 (HTML 태그 제거).</summary>
    public string PlainText => _view.Editor.GetPlainText();

    /// <summary>초기화 완료 여부. 네이티브 컨트롤은 동기 생성이라 항상 true (JoditEditor 호환용).</summary>
    public bool IsInitialized => true;

    public RichTextEditor()
    {
        InitializeComponent();

        _view = new RichEditorView
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        HostRoot.Children.Add(_view);
        _view.Editor.TextChanged += OnEditorTextChanged;

        // XAML 로 먼저 설정된 DP 값 반영 (생성자보다 DP 콜백이 앞서므로 여기서 적용).
        ApplyMode(Mode);
        ApplyText(Text);
    }

    #region Dependency Properties

    public static readonly DependencyProperty ModeProperty =
        DependencyProperty.Register(
            nameof(Mode), typeof(EditorMode), typeof(RichTextEditor),
            new PropertyMetadata(EditorMode.Simple, (d, e) => ((RichTextEditor)d).ApplyMode((EditorMode)e.NewValue)));

    public EditorMode Mode
    {
        get => (EditorMode)GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text), typeof(string), typeof(RichTextEditor),
            new PropertyMetadata(string.Empty, (d, e) => ((RichTextEditor)d).OnTextDpChanged((string)e.NewValue)));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    #endregion

    private void ApplyMode(EditorMode mode)
    {
        if (_view == null) return;
        _view.EditorMode = mode switch
        {
            EditorMode.ReadOnly => WinUIRichEditor.Controls.EditorMode.ReadOnly,
            EditorMode.Simple => WinUIRichEditor.Controls.EditorMode.Basic,
            _ => WinUIRichEditor.Controls.EditorMode.Full,
        };
    }

    private void OnTextDpChanged(string? value)
    {
        if (_view == null || _isUpdatingFromEditor) return;
        ApplyText(value);
    }

    private void ApplyText(string? html)
    {
        if (_view == null) return;
        if (string.IsNullOrEmpty(html))
            _view.Editor.Clear();
        else
            _view.Editor.LoadHtml(html);
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        _isUpdatingFromEditor = true;
        try
        {
            string html = _view.Editor.ToHtml();
            SetValue(TextProperty, html);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PlainText)));
            TextChanged?.Invoke(this, html);
        }
        finally
        {
            _isUpdatingFromEditor = false;
        }
    }

    #region Public Methods (JoditEditor API 호환)

    /// <summary>현재 HTML 내용 가져오기.</summary>
    public Task<string> GetHtmlAsync() => Task.FromResult(_view.Editor.ToHtml());

    /// <summary>캐럿 위치에 HTML 삽입 (JoditEditor 의 editor.selection.insertHTML 대응).</summary>
    public void InsertHtml(string html) => _view.Editor.InsertHtml(html);

    /// <summary>
    /// 인쇄. WinUIRichEditor 는 시스템 인쇄 다이얼로그가 없어 PDF 로 렌더 후 기본 뷰어로 열어 인쇄하게 한다
    /// (JoditEditor 의 브라우저 인쇄 다이얼로그와 UX 가 다름 — 추후 PrintManager 연동 검토).
    /// </summary>
    public async Task PrintAsync()
    {
        string dir = Path.Combine(Path.GetTempPath(), "NewSchool", "Print");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"print_{Guid.NewGuid():N}.pdf");
        using (var fs = File.Create(path))
        {
            _view.Editor.SavePdf(fs);
        }
        var file = await StorageFile.GetFileFromPathAsync(path);
        await Launcher.LaunchFileAsync(file);
    }

    #endregion

    /// <summary>
    /// MemoBoard reparent / 윈도우 종료 시 호출. WinUIRichEditor 컨트롤은 IDisposable 을 노출하지 않아
    /// (네이티브 Win2D 리소스는 GC 로 회수) 이벤트 구독 해제만 안전하게 수행한다.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_view?.Editor != null)
        {
            _view.Editor.TextChanged -= OnEditorTextChanged;
            // 문서를 비워 네이티브 CanvasTextLayout 캐시 + 디코드된 GPU 이미지 비트맵을 즉시 해제
            // (Clear → Document 교체 → ClearLayoutCache + ImageCache.Clear). 공유 D3D 디바이스는 유지.
            try { _view.Editor.Clear(); } catch { /* 종료 경로, 무시 */ }
        }
    }
}

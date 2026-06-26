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
/// JoditEditor(WebView2) 대체용 어댑터. WinUIRichEditor 를 호스팅하면서 기존 JoditEditor 와 동일한 공개 API
/// (<c>Text</c>·<c>Mode</c>·<c>PlainText</c>·<c>TextChanged</c>)를 노출해 드롭인 교체할 수 있게 한다.
///
/// <para><see cref="ShowToolbar"/> 로 호스트 형태를 고른다:
/// <list type="bullet">
/// <item><c>true</c>(기본): <see cref="RichEditorView"/> — 툴바 + 페이지/줌 크롬 + 상태바 (게시판 글 등).</item>
/// <item><c>false</c>: bare <see cref="RichEditor"/> — 편집면만(최소). 퀵잡 메모처럼 군더더기 UI 가 불필요할 때.
///   타이핑·HTML 붙여넣기(Ctrl+V)·단축키 서식은 그대로 동작.</item>
/// </list>
/// XAML 로 설정된 <see cref="ShowToolbar"/> 를 반영하려고 호스트 생성을 <c>Loaded</c> 로 지연한다.</para>
/// </summary>
public sealed partial class RichTextEditor : UserControl, INotifyPropertyChanged, IDisposable
{
    /// <summary>JoditEditor.EditorMode 와 1:1 대응 (드롭인 교체용). Simple→WinUIRichEditor Basic.</summary>
    public enum EditorMode { ReadOnly, Simple, Full }

    private RichEditorView? _view;     // ShowToolbar=true 일 때만
    private RichEditor? _editor;       // 실제 편집면 (두 경우 모두)
    private bool _isUpdatingFromEditor;
    private bool _disposed;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>텍스트 내용이 변경되었을 때 발생 (HTML 본문 전달). JoditEditor 와 시그니처 동일.</summary>
    public event EventHandler<string>? TextChanged;

    /// <summary>툴바·페이지/줌 크롬·상태바를 갖춘 전체 뷰(true) vs 편집면만(false). XAML 로 1회 설정.</summary>
    public bool ShowToolbar { get; set; } = true;

    /// <summary>에디터의 순수 텍스트 (HTML 태그 제거).</summary>
    public string PlainText => _editor?.GetPlainText() ?? string.Empty;

    /// <summary>초기화 완료 여부. 네이티브 컨트롤은 동기 생성이라 항상 true (JoditEditor 호환용).</summary>
    public bool IsInitialized => true;

    public RichTextEditor()
    {
        InitializeComponent();
        Loaded += (_, _) => Build();
    }

    /// <summary>호스트(뷰 또는 bare 컨트롤)를 1회 생성. ShowToolbar 가 XAML 로 설정된 뒤(Loaded) 실행.</summary>
    private void Build()
    {
        if (_editor != null) return;

        if (ShowToolbar)
        {
            _view = new RichEditorView
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };
            HostRoot.Children.Add(_view);
            _editor = _view.Editor;
        }
        else
        {
            _editor = new RichEditor
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };
            HostRoot.Children.Add(_editor);
        }

        _editor.TextChanged += OnEditorTextChanged;
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
        if (_editor == null) return;
        _editor.EditorMode = mode switch
        {
            EditorMode.ReadOnly => WinUIRichEditor.Controls.EditorMode.ReadOnly,
            EditorMode.Simple => WinUIRichEditor.Controls.EditorMode.Basic,
            _ => WinUIRichEditor.Controls.EditorMode.Full,
        };
    }

    private void OnTextDpChanged(string? value)
    {
        if (_editor == null || _isUpdatingFromEditor) return;
        ApplyText(value);
    }

    private void ApplyText(string? html)
    {
        if (_editor == null) return;
        if (string.IsNullOrEmpty(html))
            _editor.Clear();
        else
            _editor.LoadHtml(html);
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (_editor == null) return;
        _isUpdatingFromEditor = true;
        try
        {
            string html = _editor.ToHtml();
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
    public Task<string> GetHtmlAsync() => Task.FromResult(_editor?.ToHtml() ?? string.Empty);

    /// <summary>캐럿 위치에 HTML 삽입 (JoditEditor 의 editor.selection.insertHTML 대응).</summary>
    public void InsertHtml(string html) => _editor?.InsertHtml(html);

    /// <summary>
    /// 인쇄. WinUIRichEditor 는 시스템 인쇄 다이얼로그가 없어 PDF 로 렌더 후 기본 뷰어로 열어 인쇄하게 한다
    /// (JoditEditor 의 브라우저 인쇄 다이얼로그와 UX 가 다름 — 추후 PrintManager 연동 검토).
    /// </summary>
    public async Task PrintAsync()
    {
        if (_editor == null) return;
        string dir = Path.Combine(Path.GetTempPath(), "NewSchool", "Print");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"print_{Guid.NewGuid():N}.pdf");
        using (var fs = File.Create(path))
        {
            _editor.SavePdf(fs);
        }
        var file = await StorageFile.GetFileFromPathAsync(path);
        await Launcher.LaunchFileAsync(file);
    }

    #endregion

    /// <summary>
    /// 호스트 종료 시 호출. WinUIRichEditor 컨트롤은 IDisposable 을 노출하지 않아(네이티브 Win2D 리소스는 GC 회수)
    /// 이벤트 해제 + 문서 비우기로 네이티브 메모리를 즉시 반환한다(공유 D3D 디바이스는 유지).
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_editor != null)
        {
            _editor.TextChanged -= OnEditorTextChanged;
            // Clear → Document 교체 → ClearLayoutCache + ImageCache.Clear (네이티브 레이아웃/GPU 비트맵 해제)
            try { _editor.Clear(); } catch { /* 종료 경로, 무시 */ }
        }
    }
}

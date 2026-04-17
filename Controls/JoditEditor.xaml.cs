using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace NewSchool.Controls;

public partial class JoditEditor : UserControl, INotifyPropertyChanged, IDisposable
{
    private const string VirtualHostName = "jodit.local";

    public enum EditorMode { ReadOnly, Simple, Full }

    #region Dependency Properties

    public static readonly DependencyProperty ModeProperty =
        DependencyProperty.Register(
            nameof(Mode),
            typeof(EditorMode),
            typeof(JoditEditor),
            new PropertyMetadata(EditorMode.Simple, async (d, e) => await ((JoditEditor)d).OnModeChangedAsync(e)));

    public EditorMode Mode
    {
        get => (EditorMode)GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(JoditEditor),
            new PropertyMetadata(
                string.Empty,
                async (d, e) => await ((JoditEditor)d).OnTextChangedAsync(e)));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    #endregion

    #region Fields
    private bool _isInitialized;
    private bool _isUpdatingFromEditor;
    private bool _disposed;
    private bool _isInitializing;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    #endregion

    /// <summary>에디터의 순수 텍스트 (HTML 태그 제거된 innerText)</summary>
    public string PlainText { get; private set; } = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// 텍스트 내용이 변경되었을 때 발생하는 이벤트
    /// </summary>
    public event EventHandler<string>? TextChanged;

    public JoditEditor()
    {
        InitializeComponent();
        Loaded += OnControlLoaded;
        Unloaded += OnControlUnloaded;
    }

    private async void OnControlLoaded(object sender, RoutedEventArgs e)
    {
        if (_isInitializing || _isInitialized || _disposed) return;

        await Task.Delay(100);
        await InitializeEditorAsync();
    }

    private void OnControlUnloaded(object sender, RoutedEventArgs e)
    {
        // reparent 시나리오에서는 Unloaded가 일시적이므로
        // 자동 Dispose하지 않음 (명시적으로 Dispose() 호출 필요)
    }

    private async Task EnsureInitializedAsync()
    {
        if (!_isInitialized && !_disposed && !_isInitializing)
        {
            await InitializeEditorAsync();
        }
    }

    /// <summary>
    /// Assets/Jodit 폴더 경로 가져오기
    /// </summary>
    private static string GetJoditAssetsPath()
    {
        // 실행 파일 기준 Assets/Jodit 경로
        var baseDir = AppContext.BaseDirectory;
        var joditPath = Path.Combine(baseDir, "Assets", "Jodit");

        if (Directory.Exists(joditPath))
        {
            Debug.WriteLine($"[JoditEditor] Assets path: {joditPath}");
            return joditPath;
        }

        // 개발 환경 fallback
        var devPath = Path.Combine(baseDir, "..", "..", "..", "Assets", "Jodit");
        if (Directory.Exists(devPath))
        {
            Debug.WriteLine($"[JoditEditor] Dev assets path: {devPath}");
            return Path.GetFullPath(devPath);
        }

        Debug.WriteLine($"[JoditEditor] Assets/Jodit not found, using base: {joditPath}");
        return joditPath;
    }

    private async Task InitializeEditorAsync()
    {
        if (!await _initLock.WaitAsync(0)) return;

        try
        {
            if (_isInitialized || _disposed || _webView == null || _isInitializing)
                return;

            _isInitializing = true;
            Debug.WriteLine("[JoditEditor] 초기화 시작 (Virtual Host Mapping)...");

            // WebView2 임시파일을 사용자 Temp 폴더에 저장
            var userDataFolder = Path.Combine(
                Path.GetTempPath(),
                "NewSchool", "WebView2");
            var env = await CoreWebView2Environment.CreateWithOptionsAsync(
                null, userDataFolder, new CoreWebView2EnvironmentOptions());
            await _webView.EnsureCoreWebView2Async(env);

            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            _webView.CoreWebView2.Settings.IsZoomControlEnabled = false;
            _webView.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;
            _webView.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;

            // Virtual Host Mapping: Assets/Jodit 폴더를 가상 도메인으로 매핑
            string assetsPath = GetJoditAssetsPath();
            _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                VirtualHostName,
                assetsPath,
                CoreWebView2HostResourceAccessKind.Allow);

            _webView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
            _webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;

            // 가상 도메인을 통해 editor.html 로드 (브라우저 캐싱 활용)
            _webView.CoreWebView2.Navigate($"https://{VirtualHostName}/editor.html");

            Debug.WriteLine("[JoditEditor] Navigate 호출 완료 (대기 중...)");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[JoditEditor] 초기화 실패: {ex.Message}");
            _isInitializing = false;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async void OnNavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        try
        {
            if (!args.IsSuccess)
            {
                Debug.WriteLine($"[JoditEditor] 탐색 실패: {args.HttpStatusCode}");
                return;
            }

            _isInitialized = true;
            _isInitializing = false;

            string initialText = Text ?? string.Empty;
            string serializedText = JsonSerializer.Serialize(initialText, JoditEditorJsonContext.Default.String);
            string script = $@"
                if (window.editor) {{
                    editor.value = {serializedText};
                    setEditorMode('{Mode}');
                }}
            ";
            await ExecuteJsAsync(script);

            Debug.WriteLine("[JoditEditor] Virtual Host 에디터 구성 완료");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[JoditEditor] NavigationCompleted 오류: {ex.Message}");
        }
    }

    private void CoreWebView2_WebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            var json = JsonDocument.Parse(args.TryGetWebMessageAsString()).RootElement;
            var type = json.GetProperty("type").GetString();

            if (type == "contentChanged")
            {
                string content = json.GetProperty("content").GetString() ?? string.Empty;

                // plain text (editor.text) 수신
                if (json.TryGetProperty("plainText", out var pt))
                    PlainText = pt.GetString() ?? string.Empty;

                _isUpdatingFromEditor = true;
                SetValue(TextProperty, content);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PlainText)));

                TextChanged?.Invoke(this, content);
            }
            else if (type == "contentHeight")
            {
                // ReadOnly 모드: 내부 스크롤 사용, 동적 높이 조정 불필요
            }
            else if (type == "ready")
            {
                Debug.WriteLine("[JoditEditor] JS ready signal received");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[JoditEditor] 메시지 처리 오류: {ex.Message}");
        }
        finally
        {
            _isUpdatingFromEditor = false;
        }
    }

    private async Task ExecuteJsAsync(string script)
    {
        if (_webView?.CoreWebView2 != null && _isInitialized && !_disposed)
        {
            try
            {
                await _webView.ExecuteScriptAsync(script);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[JoditEditor] JS 실행 오류: {ex.Message}");
            }
        }
    }

    private async Task SetEditorContentAsync(string content)
    {
        string serializedContent = JsonSerializer.Serialize(content ?? string.Empty, JoditEditorJsonContext.Default.String);
        await ExecuteJsAsync($"editor.value = {serializedContent};");
    }

    private async Task OnModeChangedAsync(DependencyPropertyChangedEventArgs e)
    {
        await EnsureInitializedAsync();

        if (_isInitialized)
        {
            await ExecuteJsAsync($"setEditorMode('{e.NewValue}')");
        }
    }

    private async Task OnTextChangedAsync(DependencyPropertyChangedEventArgs e)
    {
        await EnsureInitializedAsync();

        if (_isInitialized && !_isUpdatingFromEditor)
        {
            await SetEditorContentAsync((string)e.NewValue ?? string.Empty);
        }
    }

    // 비동기 Dispose
    public async Task DisposeAsync()
    {
        if (_disposed) return;

        await _initLock.WaitAsync();

        try
        {
            _disposed = true;
            Debug.WriteLine("[JoditEditor] 정리 시작...");

            if (_isInitialized && _webView?.CoreWebView2 != null)
            {
                try
                {
                    var jsCleanup = ExecuteJsAsync(@"
                        if (window.editor) {
                            window.editor.events.off('change');
                            window.editor.destruct();
                            window.editor = null;
                        }
                        if (window.changeTimeout) {
                            clearTimeout(window.changeTimeout);
                            window.changeTimeout = null;
                        }
                    ");

                    var timeout = Task.Delay(300);
                    await Task.WhenAny(jsCleanup, timeout);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[JoditEditor] JS 정리 오류 (무시): {ex.Message}");
                }

                try
                {
                    _webView.CoreWebView2.WebMessageReceived -= CoreWebView2_WebMessageReceived;
                    _webView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[JoditEditor] 이벤트 해제 오류: {ex.Message}");
                }

                try
                {
                    // Virtual Host 매핑 해제
                    _webView.CoreWebView2.ClearVirtualHostNameToFolderMapping(VirtualHostName);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[JoditEditor] VirtualHost 해제 오류 (무시): {ex.Message}");
                }

                try
                {
                    var suspendTask = _webView.CoreWebView2.TrySuspendAsync().AsTask();
                    var timeout = Task.Delay(300);
                    await Task.WhenAny(suspendTask, timeout);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[JoditEditor] Suspend 오류 (무시): {ex.Message}");
                }
            }

            try
            {
                _webView?.Close();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[JoditEditor] Close 오류: {ex.Message}");
            }

            Debug.WriteLine("[JoditEditor] 정리 완료");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[JoditEditor] DisposeAsync 오류: {ex.Message}");
        }
        finally
        {
            _initLock.Release();
            _initLock.Dispose();
        }
    }

    // 동기 Dispose
    public void Dispose()
    {
        if (_disposed) return;

        if (DispatcherQueue.HasThreadAccess)
        {
            _ = DisposeAsync().ContinueWith(t =>
            {
                if (t.IsFaulted)
                    System.Diagnostics.Debug.WriteLine($"[JoditEditor] {t.Exception?.InnerException?.Message}");
            }, TaskContinuationOptions.OnlyOnFaulted);
        }
        else
        {
            DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    await DisposeAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[JoditEditor] Dispose (DispatcherQueue) 오류: {ex.Message}");
                }
            });
        }

        GC.SuppressFinalize(this);
    }

    #region Public Methods

    /// <summary>
    /// 프린트 다이얼로그 실행
    /// </summary>
    public async Task PrintAsync()
    {
        await EnsureInitializedAsync();

        if (!_isInitialized || _webView?.CoreWebView2 == null)
        {
            throw new InvalidOperationException("JoditEditor가 초기화되지 않았습니다.");
        }

        await _webView.CoreWebView2.ExecuteScriptAsync("printContent()");
    }

    /// <summary>
    /// 현재 HTML 내용 가져오기
    /// </summary>
    public async Task<string> GetHtmlAsync()
    {
        await EnsureInitializedAsync();

        if (!_isInitialized || _webView?.CoreWebView2 == null)
        {
            return string.Empty;
        }

        string result = await _webView.CoreWebView2.ExecuteScriptAsync("editor.value");
        return JsonSerializer.Deserialize(result, JoditEditorJsonContext.Default.String) ?? string.Empty;
    }

    /// <summary>
    /// JavaScript 실행 (고급 기능용)
    /// </summary>
    public async Task<string> ExecuteScriptAsync(string script)
    {
        await EnsureInitializedAsync();

        if (!_isInitialized || _webView?.CoreWebView2 == null)
        {
            throw new InvalidOperationException("JoditEditor가 초기화되지 않았습니다.");
        }

        return await _webView.CoreWebView2.ExecuteScriptAsync(script);
    }

    /// <summary>
    /// 초기화 완료 여부 확인
    /// </summary>
    public bool IsInitialized => _isInitialized;

    #endregion
}

using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace NewSchool.Controls;

/// <summary>
/// RichTextEditor 를 다이얼로그 창으로 감싼 래퍼 (구 JoditEditorWin 대체).
/// WinUI3 에는 DialogResult 가 없으므로 Result 프로퍼티 + ShowDialogAsync 사용.
/// </summary>
public sealed partial class RichTextEditorWin : Window
{
    private readonly TaskCompletionSource<bool> _dialogResult = new();

    /// <summary>에디터 모드 (ReadOnly, Simple, Full).</summary>
    public RichTextEditor.EditorMode EditorMode
    {
        get => richEditor.Mode;
        set => richEditor.Mode = value;
    }

    /// <summary>에디터 텍스트 내용 (HTML).</summary>
    public string Text
    {
        get => richEditor.Text;
        set => richEditor.Text = value;
    }

    /// <summary>다이얼로그 결과 (확인: true, 취소: false).</summary>
    public bool Result { get; private set; }

    public RichTextEditorWin()
    {
        InitializeComponent();
        SetWindowSize(900, 700);
        Title = "편집기";

        // 안내·오류 대화상자가 메인 창이 아니라 이 창 위에 뜨도록 등록한다.
        MessageBox.TrackWindow(this);

        // 메인 창이 '항상 위에'면 이 창도 같은 topmost 레벨로 올려 뒤로 숨지 않게 함
        if (Settings.TopMost.Value)
            MainWindow.SetAlwaysOnTop(this, true);

        // 메인 창과 같은 테마로 연다
        NewSchool.Helpers.ThemeHelper.Apply(this);

        // [확인] 을 눌러야 고친 글이 부르는 쪽으로 간다 — X 로 닫으면 사라지므로 묻는다(52차).
        UnsavedWorkGuard.AskBeforeClosing(
            this, () => !Result && richEditor.PlainText != _openedText, "고친 내용이 반영되지 않습니다.");

        Closed += OnWindowClosed;

        // 여는 쪽이 생성자 뒤에 Text 를 넣으므로, 기준값은 창이 떠서 자리를 잡을 때 뜬다.
        Activated += OnFirstActivated;
    }

    /// <summary>연 그대로의 본문. 이후 달라지면 아직 반영되지 않은 편집이 있는 것이다.</summary>
    private string _openedText = string.Empty;

    private void OnFirstActivated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= OnFirstActivated;
        _openedText = richEditor.PlainText;
    }

    public RichTextEditorWin(string title) : this()
    {
        Title = title;
    }

    public RichTextEditorWin(string title, string initialText) : this(title)
    {
        Text = initialText;
    }

    public RichTextEditorWin(string title, string initialText, RichTextEditor.EditorMode mode) : this(title, initialText)
    {
        EditorMode = mode;
    }

    #region Window Size / Position

    private void SetWindowSize(int width, int height)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new SizeInt32(width, height));
    }

    public void SetSize(int width, int height) => SetWindowSize(width, height);

    public void CenterOnParent(Window parent)
    {
        if (parent == null) return;

        var parentHwnd = WinRT.Interop.WindowNative.GetWindowHandle(parent);
        var parentWindowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(parentHwnd);
        var parentAppWindow = AppWindow.GetFromWindowId(parentWindowId);

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        var parentPos = parentAppWindow.Position;
        var parentSize = parentAppWindow.Size;
        var thisSize = appWindow.Size;

        int x = parentPos.X + (parentSize.Width - thisSize.Width) / 2;
        int y = parentPos.Y + (parentSize.Height - thisSize.Height) / 2;
        appWindow.Move(new PointInt32(x, y));
    }

    #endregion

    #region Dialog Methods

    public async Task<bool> ShowDialogAsync()
    {
        Activate();
        return await _dialogResult.Task;
    }

    public async Task<bool> ShowDialogAsync(Window parent)
    {
        CenterOnParent(parent);
        Activate();
        return await _dialogResult.Task;
    }

    #endregion

    #region Event Handlers

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        Result = true;
        _dialogResult.TrySetResult(true);
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        // ⚠ 결과를 여기서 먼저 넣지 않는다 — 닫기 확인에서 [계속 편집] 을 고르면 창은 열려
        //   있는데 기다리던 쪽은 "취소" 를 받고 돌아가 버린다. 결과는 OnWindowClosed 가 넣는다.
        Close();
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        // X 버튼으로 닫은 경우도 취소로 처리
        _dialogResult.TrySetResult(false);
        richEditor?.Dispose();
    }

    #endregion

    #region Public Methods

    public Task PrintAsync() => richEditor.PrintAsync(Title ?? "NewSchool");

    #endregion
}

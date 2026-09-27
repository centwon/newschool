using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace NewSchool.Controls;

/// <summary>
/// HTML 한 덩이를 서식 편집기로 고치는 창 (구 JoditEditorWin 대체). 학급일지 알림장이 쓴다.
/// WinUI3 에는 DialogResult 가 없으므로 Result 프로퍼티 + ShowDialogAsync 사용.
/// </summary>
public sealed partial class RichTextEditorWin : Window
{
    private readonly TaskCompletionSource<bool> _dialogResult = new();

    private WinUIRichEditor.Controls.RichEditor richEditor => richEditorView.Editor;

    /// <summary>
    /// 본문(HTML). 창을 열 때 넣은 값이고, [확인] 으로 닫으면 고친 결과다.
    ///
    /// <para>⚠ [확인] 을 누를 때 뽑아 둔다 — 닫힐 때 편집기를 비우므로(<see cref="OnWindowClosed"/>)
    /// 부르는 쪽이 닫힌 뒤에 편집기에서 읽으면 빈 글이 온다.</para>
    /// </summary>
    public string Text { get; private set; } = string.Empty;

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
            this, () => !Result && richEditor.IsModified, "고친 내용이 반영되지 않습니다.");

        Closed += OnWindowClosed;
    }

    public RichTextEditorWin(string title) : this()
    {
        Title = title;
    }

    /// <summary>본문을 싣고 연다. <c>LoadHtml</c> 은 불러온 직후를 "고치지 않음" 으로 둔다.</summary>
    public RichTextEditorWin(string title, string initialHtml) : this(title)
    {
        Text = initialHtml ?? string.Empty;
        if (Text.Length == 0) richEditor.Clear();
        else richEditor.LoadHtml(Text);
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
        // 손대지 않았으면 넣은 HTML 을 그대로 돌려준다 — 다시 뽑으면 모양이 바뀔 수 있다.
        if (richEditor.IsModified) Text = richEditor.ToHtml();
        Result = true;
        _dialogResult.TrySetResult(true);
        Close();
    }

    private async void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        // ⚠ 결과를 여기서 먼저 넣지 않는다 — 닫기 확인에서 [계속 편집] 을 고르면 창은 열려
        //   있는데 기다리던 쪽은 "취소" 를 받고 돌아가 버린다. 결과는 OnWindowClosed 가 넣는다.
        // ⚠ Close() 를 바로 부르면 묻지 않고 닫힌다 — X 와 같은 확인을 거친다.
        await UnsavedWorkGuard.CloseAsync(this);
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        // X 버튼으로 닫은 경우도 취소로 처리
        _dialogResult.TrySetResult(false);
        richEditor.Clear();
    }

    #endregion
}

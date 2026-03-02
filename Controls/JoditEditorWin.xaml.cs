using System;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace NewSchool.Controls;

/// <summary>
/// JoditEditor를 다이얼로그 창으로 감싼 래퍼
/// WinUI3에서는 DialogResult가 없으므로 Result 프로퍼티 사용
/// </summary>
public sealed partial class JoditEditorWin : Window
{
    private readonly TaskCompletionSource<bool> _dialogResult = new();

    #region Properties

    /// <summary>
    /// 에디터 모드 (ReadOnly, Simple, Full)
    /// </summary>
    public JoditEditor.EditorMode EditorMode
    {
        get => joditEditor.Mode;
        set => joditEditor.Mode = value;
    }

    /// <summary>
    /// 에디터 텍스트 내용
    /// </summary>
    public string Text
    {
        get => joditEditor.Text;
        set => joditEditor.Text = value;
    }

    /// <summary>
    /// 다이얼로그 결과 (확인: true, 취소: false)
    /// </summary>
    public bool Result { get; private set; }

    #endregion

    public JoditEditorWin()
    {
        InitializeComponent();

        // 기본 창 크기 설정
        SetWindowSize(900, 700);

        // 창 제목 표시줄 설정
        Title = "편집기";
        
        // 창 닫힘 이벤트
        Closed += OnWindowClosed;
    }

    /// <summary>
    /// 제목 설정 생성자
    /// </summary>
    public JoditEditorWin(string title) : this()
    {
        Title = title;
    }

    /// <summary>
    /// 제목과 초기 텍스트 설정 생성자
    /// </summary>
    public JoditEditorWin(string title, string initialText) : this(title)
    {
        Text = initialText;
    }

    /// <summary>
    /// 모든 옵션 설정 생성자
    /// </summary>
    public JoditEditorWin(string title, string initialText, JoditEditor.EditorMode mode) : this(title, initialText)
    {
        EditorMode = mode;
    }

    #region Window Size

    private void SetWindowSize(int width, int height)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        appWindow.Resize(new SizeInt32(width, height));
    }

    /// <summary>
    /// 창 크기 조절
    /// </summary>
    public void SetSize(int width, int height)
    {
        SetWindowSize(width, height);
    }

    /// <summary>
    /// 부모 창 중앙에 배치
    /// </summary>
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

    /// <summary>
    /// 모달 다이얼로그로 표시하고 결과 대기
    /// </summary>
    public async Task<bool> ShowDialogAsync()
    {
        Activate();
        return await _dialogResult.Task;
    }

    /// <summary>
    /// 부모 창 중앙에 모달 다이얼로그로 표시
    /// </summary>
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
        Result = false;
        _dialogResult.TrySetResult(false);
        Close();
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        // X 버튼으로 닫은 경우도 취소로 처리
        _dialogResult.TrySetResult(false);
        
        // JoditEditor 리소스 정리
        joditEditor?.Dispose();
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// 현재 HTML 내용 가져오기
    /// </summary>
    public async Task<string> GetHtmlAsync()
    {
        return await joditEditor.GetHtmlAsync();
    }

    /// <summary>
    /// 프린트 다이얼로그 실행
    /// </summary>
    public async Task PrintAsync()
    {
        await joditEditor.PrintAsync();
    }

    /// <summary>
    /// 에디터 초기화 완료 여부
    /// </summary>
    public bool IsEditorInitialized => joditEditor.IsInitialized;

    #endregion
}

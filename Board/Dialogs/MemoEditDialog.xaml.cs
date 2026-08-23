using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using Windows.Storage;
using NewSchool.Board.Services;
using NewSchool.Controls;
using NewSchool.Models;

namespace NewSchool.Board.Dialogs;

/// <summary>
/// 메모 상세 편집 창. 예전에는 ContentDialog였으나(리사이즈 불가), 사용자가 자유롭게
/// 크기를 조절할 수 있도록 Window로 전환. WinUI3에는 DialogResult가 없으므로
/// Result 프로퍼티 + ShowDialogAsync 패턴 사용 (RichTextEditorWin과 동일한 패턴).
/// </summary>
public sealed partial class MemoEditDialog : Window
{
    private readonly Post _post;
    private readonly TaskCompletionSource<bool> _dialogResult = new();

    /// <summary>다이얼로그 결과 (저장: true, 취소: false).</summary>
    public bool Result { get; private set; }

    public MemoEditDialog(Post post)
    {
        _post = post ?? throw new ArgumentNullException(nameof(post));

        InitializeComponent();
        Title = "메모 편집";
        SetWindowSize(900, 700);

        Closed += OnWindowClosed;
    }

    #region Window Size / Position

    private void SetWindowSize(int width, int height)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new SizeInt32(width, height));
    }

    private void CenterOnParent(Window parent)
    {
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

    public async Task<bool> ShowDialogAsync(Window? parent = null)
    {
        if (parent != null) CenterOnParent(parent);

        // 메인 창이 '항상 위에' 상태면 이 편집 창도 같은 topmost 레벨로 올려,
        // 메인 창 뒤로 숨어버리는 현상을 방지 (나중에 Activate 되므로 위에 표시됨)
        if (Settings.TopMost.Value)
            MainWindow.SetAlwaysOnTop(this, true);

        Activate();
        await LoadAsync();
        return await _dialogResult.Task;
    }

    #endregion

    private async Task LoadAsync()
    {
        try
        {
            // 체크박스
            ChkCompleted.IsChecked = _post.IsCompleted;

            // 카테고리
            SelectComboBoxByTag(CBoxCategory, _post.Category);

            // 파일리스트 카테고리 설정
            FileList.Category = _post.Category;

            // 제목
            TxtTitle.Text = _post.Title ?? "";

            // 에디터
            Editor.LoadFlow(_post.Content);

            // 메타정보
            TxtMetadata.Text = $"작성일시: {_post.DateTime:yyyy-MM-dd HH:mm:ss}";

            // 첨부파일 로드
            if (_post.No > 0)
            {
                FileList.Post = _post;
                await LoadFilesAsync();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MemoEditDialog] 로드 중 오류: {ex.Message}");
        }
    }

    private async Task LoadFilesAsync()
    {
        if (_post.No <= 0) return;

        try
        {
            using var service = Board.CreateService();
            var files = await service.GetPostFilesByPostAsync(_post.No);
            FileList.SetFiles(files);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MemoEditDialog] 파일 로드 실패: {ex.Message}");
        }
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // 데이터 업데이트
            _post.IsCompleted = ChkCompleted.IsChecked == true;
            _post.Category = GetSelectedCategory();
            _post.Title = TxtTitle.Text;
            _post.Content = Editor.GetFlowBytes();
            _post.PlainText = Editor.PlainText;
            _post.DateTime = DateTime.Now;

            using var service = Board.CreateService();

            // 1. Post 저장
            int postNo = await service.SavePostAsync(_post);

            if (postNo <= 0)
            {
                // 저장 실패인데도 아래에서 Result=true 로 창을 닫으면 편집이 조용히 유실된다.
                // 창을 열어둔 채 실패를 알려 사용자가 다시 시도하게 한다(PostEditPage 와 동일 정책).
                await MessageBox.ShowErrorAsync("메모 저장에 실패했습니다.");
                return;
            }

            // 2. 첨부 변경(삭제 예정 + 새로 붙인 파일) 반영
            _post.HasFile = await PostAttachments.ApplyAsync(
                service, FileList, postNo, _post.Category);

            Debug.WriteLine($"[MemoEditDialog] 메모 저장 완료: No={_post.No}");

            Result = true;
            _dialogResult.TrySetResult(true);
            Close();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MemoEditDialog] 저장 실패: {ex.Message}");

            // 오류 발생 시 창은 닫지 않고 사용자가 다시 시도할 수 있게 둠
            await MessageBox.ShowErrorAsync($"메모 저장 중 오류가 발생했습니다.\n{ex.Message}", ex);
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        Result = false;
        _dialogResult.TrySetResult(false);
        Close();
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        // 타이틀바 X 버튼으로 닫은 경우도 취소로 처리 (버튼으로 이미 완료된 경우 TrySetResult는 안전하게 무시됨)
        _dialogResult.TrySetResult(false);
        Editor?.Dispose();
    }

    private static void SelectComboBoxByTag(ComboBox comboBox, string? tag)
    {
        if (string.IsNullOrEmpty(tag))
        {
            comboBox.SelectedIndex = 0;
            return;
        }

        foreach (var item in comboBox.Items)
        {
            if (item is ComboBoxItem cbi && cbi.Tag?.ToString() == tag)
            {
                comboBox.SelectedItem = item;
                return;
            }
        }
        comboBox.SelectedIndex = 0;
    }

    private string GetSelectedCategory()
    {
        if (CBoxCategory.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            return tag;
        }
        return CategoryNames.Lesson;
    }
}

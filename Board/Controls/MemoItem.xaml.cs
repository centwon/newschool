using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Board.Services;
using NewSchool.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;

namespace NewSchool.Board.Controls;

/// <summary>
/// 메모 아이템 컨트롤
/// Expander 기반으로 펼침/접힘 지원
/// </summary>
public sealed partial class MemoItem : UserControl
{
    #region Fields

    private bool _isLoading = false;

    /// <summary>첨부파일 목록</summary>
    public ObservableCollection<PostFile> Files { get; } = [];

    #endregion

    #region Events

    /// <summary>삭제 요청 이벤트</summary>
    public event EventHandler<Post>? DeleteRequested;

    /// <summary>저장 완료 이벤트</summary>
    public event EventHandler<Post>? Saved;

    /// <summary>완료 상태 변경 이벤트</summary>
    public event EventHandler<Post>? CompletedChanged;

    #endregion

    #region Properties

    /// <summary>Post 데이터</summary>
    public Post? Post
    {
        get;
        set
        {
            field = value;
            LoadPostData();
        }
    }

    /// <summary>펼침 상태</summary>
    public bool IsExpanded
    {
        get => MemoExpander.IsExpanded;
        set => MemoExpander.IsExpanded = value;
    }

    #endregion

    #region Constructor

    public MemoItem()
    {
        InitializeComponent();
        FileList.ItemsSource = Files;
    }

    public MemoItem(Post post) : this()
    {
        Post = post;
    }

    #endregion

    #region Data Loading

    /// <summary>
    /// Post 데이터를 UI에 로드
    /// </summary>
    private void LoadPostData()
    {
        if (Post == null) return;

        _isLoading = true;

        // 체크박스
        ChkCompleted.IsChecked = Post.IsCompleted;

        // 카테고리 배지
        TxtSubject.Text = string.IsNullOrEmpty(Post.Subject) ? "기타" : Post.Subject;
        UpdateSubjectBadgeColor();

        // 제목
        TxtTitle.Text = string.IsNullOrEmpty(Post.Title) ? "(제목 없음)" : Post.Title;

        // 완료 상태 스타일
        UpdateCompletedStyle();

        // 첨부파일 아이콘
        IconFile.Visibility = Post.HasFile ? Visibility.Visible : Visibility.Collapsed;

        // 날짜
        TxtDate.Text = Post.DateTime.ToString("M/d HH:mm");

        // 내용
        TxtContent.Text = Post.Content ?? "";

        _isLoading = false;
    }

    /// <summary>
    /// 첨부파일 로드
    /// </summary>
    private async Task LoadFilesAsync()
    {
        if (Post == null || Post.No <= 0) return;

        try
        {
            using var service = Board.CreateService();
            var files = await service.GetPostFilesByPostAsync(Post.No);

            Files.Clear();
            foreach (var file in files)
            {
                Files.Add(file);
            }

            UpdateFilePanel();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MemoItem] 파일 로드 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 파일 패널 업데이트
    /// </summary>
    private void UpdateFilePanel()
    {
        TxtFileCount.Text = $"첨부파일 ({Files.Count})";
        PanelFiles.Visibility = Files.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    #endregion

    #region UI Helpers

    /// <summary>
    /// 카테고리 배지 색상 업데이트
    /// </summary>
    private void UpdateSubjectBadgeColor()
    {
        // 카테고리별 색상 (선택사항)
        var color = Post?.Subject switch
        {
            "업무" => Microsoft.UI.Colors.RoyalBlue,
            "수업" => Microsoft.UI.Colors.ForestGreen,
            "학급" => Microsoft.UI.Colors.Orange,
            "개인" => Microsoft.UI.Colors.MediumPurple,
            _ => Microsoft.UI.Colors.Gray
        };

        BadgeSubject.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(color);
    }

    /// <summary>
    /// 완료 상태 스타일 업데이트
    /// </summary>
    private void UpdateCompletedStyle()
    {
        if (Post?.IsCompleted == true)
        {
            TxtTitle.TextDecorations = Windows.UI.Text.TextDecorations.Strikethrough;
            TxtTitle.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorDisabledBrush"];
        }
        else
        {
            TxtTitle.TextDecorations = Windows.UI.Text.TextDecorations.None;
            TxtTitle.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
        }
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// Expander 펼침 시 파일 로드
    /// </summary>
    private async void MemoExpander_Expanding(Expander sender, ExpanderExpandingEventArgs args)
    {
        await LoadFilesAsync();
    }

    /// <summary>
    /// 완료 체크박스 클릭
    /// </summary>
    private async void ChkCompleted_Click(object sender, RoutedEventArgs e)
    {
        if (Post == null || _isLoading) return;

        Post.IsCompleted = ChkCompleted.IsChecked == true;
        UpdateCompletedStyle();

        try
        {
            using var service = Board.CreateService();
            await service.UpdatePostIsCompletedAsync(Post.No, Post.IsCompleted);

            CompletedChanged?.Invoke(this, Post);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MemoItem] 완료 상태 저장 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 내용 변경
    /// </summary>
    private void TxtContent_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading) return;
    }

    /// <summary>
    /// 저장 버튼 클릭
    /// </summary>
    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (Post == null) return;

        try
        {
            Post.Content = TxtContent.Text;
            Post.DateTime = DateTime.Now;

            using var service = Board.CreateService();
            await service.SavePostAsync(Post);

                TxtDate.Text = Post.DateTime.ToString("M/d HH:mm");

            Saved?.Invoke(this, Post);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MemoItem] 저장 실패: {ex.Message}");
            await MessageBox.ShowAsync($"저장 중 오류가 발생했습니다.\n{ex.Message}", "오류");
        }
    }

    /// <summary>
    /// 삭제 버튼 클릭
    /// </summary>
    private async void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (Post == null) return;

        var confirmed = await MessageBox.ShowConfirmAsync(
            "이 메모를 삭제하시겠습니까?\n삭제된 메모는 복구할 수 없습니다.",
            "메모 삭제", "삭제", "취소");
        if (confirmed)
        {
            DeleteRequested?.Invoke(this, Post);
        }
    }

    /// <summary>
    /// 파일 추가 버튼 클릭
    /// </summary>
    private async void BtnAddFile_Click(object sender, RoutedEventArgs e)
    {
        if (Post == null || Post.No <= 0)
        {
            await MessageBox.ShowAsync("메모를 먼저 저장해주세요.", "알림");
            return;
        }

        try
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary
            };
            picker.FileTypeFilter.Add("*");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var files = await picker.PickMultipleFilesAsync();
            if (files == null || files.Count == 0) return;

            using var service = Board.CreateService();

            foreach (var file in files)
            {
                // 파일 복사
                Board.EnsureCategoryDirectory("Memo");
                var uniqueFileName = $"{DateTime.Now:yyyyMMddHHmmss}_{file.Name}";
                var destPath = Board.GetFilePath(uniqueFileName, "Memo");

                var destFolder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(destPath)!);
                await file.CopyAsync(destFolder, Path.GetFileName(destPath));

                // DB에 기록
                var props = await file.GetBasicPropertiesAsync();
                var postFile = new PostFile
                {
                    Post = Post.No,
                    DateTime = DateTime.Now,
                    FileName = uniqueFileName,
                    FileSize = (long)props.Size
                };

                await service.AddPostFileAsync(postFile);
                Files.Add(postFile);
            }

            // HasFile 업데이트
            Post.HasFile = true;
            await service.UpdatePostHasFileAsync(Post.No, true);
            IconFile.Visibility = Visibility.Visible;

            UpdateFilePanel();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MemoItem] 파일 추가 실패: {ex.Message}");
            await MessageBox.ShowAsync($"파일 추가 중 오류가 발생했습니다.\n{ex.Message}", "오류");
        }
    }

    /// <summary>
    /// 파일 열기 버튼 클릭
    /// </summary>
    private async void BtnOpenFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not PostFile file) return;

        try
        {
            var filePath = Board.GetFilePath(file.FileName, "Memo");
            var storageFile = await StorageFile.GetFileFromPathAsync(filePath);
            await Launcher.LaunchFileAsync(storageFile);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MemoItem] 파일 열기 실패: {ex.Message}");
            await MessageBox.ShowAsync($"파일을 열 수 없습니다.\n{ex.Message}", "오류");
        }
    }

    #endregion
}

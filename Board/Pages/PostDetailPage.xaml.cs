using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using NewSchool.Board.Services;
using NewSchool.Board.ViewModels;
using NewSchool.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace NewSchool.Board.Pages;

public sealed partial class PostDetailPage : Page
{
    public PostDetailViewModel ViewModel { get; }
    private int _postNo;
    private StorageFile? _commentAttachedFile;
    private PostListPageParameter? _boardParameter;

    public PostDetailPage()
    {
        this.InitializeComponent();
        ViewModel = new PostDetailViewModel();
        this.DataContext = ViewModel;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is PostDetailPageParameter param)
        {
            _postNo = param.PostNo;
            _boardParameter = param.BoardParameter;
        }
        else if (e.Parameter is int postNo)
        {
            _postNo = postNo;
        }

        if (_postNo > 0)
        {
            await ViewModel.LoadPostAsync(_postNo);

            if (ViewModel.Post != null)
            {
                // 내용을 JoditEditor에 설정
                ContentViewer.Text = ViewModel.Post.Content;

                using var service = Board.CreateService();
                var files = await service.GetPostFilesByPostAsync(_postNo);
                DetailFileListBox.LoadFiles(files, ViewModel.Post.Category, readOnly: true);
            }
        }
    }

    private void EditButton_Click(object sender, RoutedEventArgs e)
    {
        // 게시판 컨텍스트를 PostEditPage에 전달
        Frame.Navigate(typeof(PostEditPage), new PostEditPageParameter
        {
            PostNo = _postNo,
            DefaultCategory = _boardParameter?.Category,
            AllowCategoryChange = _boardParameter?.AllowCategoryChange ?? true
        });
    }

    private async void PrintButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Post == null) return;

        try
        {
            var printService = new PostPrintService();
            var comments = new System.Collections.Generic.List<Comment>(ViewModel.Comments);
            string filePath = printService.GeneratePostPdf(ViewModel.Post, comments);

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync($"PDF 생성 중 오류가 발생했습니다.\n{ex.Message}");
        }
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        var confirmed = await MessageBox.ShowConfirmAsync(
            "정말로 이 게시글을 삭제하시겠습니까?",
            "게시글 삭제", "삭제", "취소");
        if (confirmed && ViewModel.Post != null)
        {
            using var service = Board.CreateService();
            bool success = await service.DeletePostAsync(_postNo, ViewModel.Post.Category);

            if (success)
            {
                if (Frame.CanGoBack)
                    Frame.GoBack();
                else
                    Frame.Navigate(typeof(PostListPage));
            }
            else
            {
                await ShowErrorAsync("게시글 삭제에 실패했습니다.");
            }
        }
    }

    /// <summary>
    /// 댓글에 파일 첨부
    /// </summary>
    private async void CommentAttachFileButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker();
            var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
            InitializeWithWindow.Initialize(picker, hwnd);

            picker.FileTypeFilter.Add("*");
            picker.ViewMode = PickerViewMode.List;

            var file = await picker.PickSingleFileAsync();

            if (file != null)
            {
                _commentAttachedFile = file;

                // 파일 정보 표시
                var properties = await file.GetBasicPropertiesAsync();
                CommentFileNameTextBlock.Text = file.Name;
                CommentFileSizeTextBlock.Text = $"({FormatFileSize((long)properties.Size)})";

                CommentFileInfoBorder.Visibility = Visibility.Visible;
                CommentRemoveFileButton.Visibility = Visibility.Visible;

                System.Diagnostics.Debug.WriteLine($"댓글 파일 선택됨: {file.Name}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"파일 선택 실패: {ex.Message}");
            await ShowErrorAsync("파일 선택 중 오류가 발생했습니다.");
        }
    }

    /// <summary>
    /// 댓글 첨부파일 제거
    /// </summary>
    private void CommentRemoveFileButton_Click(object sender, RoutedEventArgs e)
    {
        _commentAttachedFile = null;
        CommentFileInfoBorder.Visibility = Visibility.Collapsed;
        CommentRemoveFileButton.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// 댓글 작성 / 수정
    /// </summary>
    private async void AddCommentButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsEditing)
        {
            await ViewModel.UpdateCommentAsync();
            ResetCommentUI();
        }
        else
        {
            await ViewModel.AddCommentAsync(_commentAttachedFile);

            // 파일 첨부 UI 초기화
            _commentAttachedFile = null;
            CommentFileInfoBorder.Visibility = Visibility.Collapsed;
            CommentRemoveFileButton.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// 댓글 수정 시작
    /// </summary>
    private void EditCommentButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Comment comment)
        {
            ViewModel.StartEdit(comment);

            AddCommentButton.Content = "수정 완료";
            CancelEditButton.Visibility = Visibility.Visible;

            // TextBox에 포커스
            NewCommentTextBox.Focus(FocusState.Programmatic);
        }
    }

    /// <summary>
    /// 댓글 수정 취소
    /// </summary>
    private void CancelEditButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CancelEdit();
        ResetCommentUI();
    }

    private void ResetCommentUI()
    {
        AddCommentButton.Content = "댓글 작성";
        CancelEditButton.Visibility = Visibility.Collapsed;

        _commentAttachedFile = null;
        CommentFileInfoBorder.Visibility = Visibility.Collapsed;
        CommentRemoveFileButton.Visibility = Visibility.Collapsed;
    }

    private async void DeleteCommentButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Comment comment)
        {
            var confirmed = await MessageBox.ShowConfirmAsync(
                "정말로 이 댓글을 삭제하시겠습니까?",
                "댓글 삭제", "삭제", "취소");
            if (confirmed)
            {
                await ViewModel.DeleteCommentAsync(comment);
            }
        }
    }

    /// <summary>
    /// 댓글 첨부파일 클릭
    /// </summary>
    private async void CommentFileButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Comment comment && ViewModel.Post != null)
        {
            try
            {
                string filepath = Board.GetFilePath(comment.FileName, ViewModel.Post.Category);

                if (!File.Exists(filepath))
                {
                    await ShowErrorAsync("파일이 존재하지 않습니다.");
                    return;
                }

                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = filepath,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"파일 열기 실패: {ex.Message}");
                await ShowErrorAsync($"파일을 열 수 없습니다.\n{ex.Message}");
            }
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
        else
        {
            Frame.Navigate(typeof(PostListPage));
        }
    }

    private string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;

        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }

        return $"{len:0.##} {sizes[order]}";
    }

    private async Task ShowErrorAsync(string message)
    {
        await MessageBox.ShowErrorAsync(message);
    }
}

using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using NewSchool.Board.Services;
using NewSchool.Controls;

namespace NewSchool.Board
{
    /// <summary>
    /// CommentBox - BoardService 통합 버전 (비동기 + 트랜잭션)
    /// </summary>
    public sealed partial class CommentBox : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private readonly BoardService _service;
        private Comment _comment = new();
        private Post _post = new();
        private string _fileNameText = "추가";
        private string _fileSizeText = string.Empty;

        public string OrgFilePath { get; private set; } = string.Empty;
        public string SaveFilePath { get; private set; } = string.Empty;
        private string FileToDelete = string.Empty;

        #region Properties

        public Comment Comment
        {
            get => _comment;
            set
            {
                if (_comment != value)
                {
                    _comment = value;
                    OnPropertyChanged();
                    UpdateFileDisplay();
                }
            }
        }

        public Post Post
        {
            get => _post;
            set
            {
                if (_post != value)
                {
                    _post = value;
                    OnPropertyChanged();
                }
            }
        }

        public string FileNameText
        {
            get => _fileNameText;
            set
            {
                if (_fileNameText != value)
                {
                    _fileNameText = value;
                    OnPropertyChanged();
                }
            }
        }

        public string FileSizeText
        {
            get => _fileSizeText;
            set
            {
                if (_fileSizeText != value)
                {
                    _fileSizeText = value;
                    OnPropertyChanged();
                }
            }
        }

        #endregion

        public CommentBox()
        {
            this.InitializeComponent();
            this.DataContext = this;
            _service = Board.CreateService();
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void UpdateFileDisplay()
        {
            FileNameText = Comment.HasFile ? Comment.FileName : "추가";
            FileSizeText = Comment.HasFile ? Tool.FormatSize(Comment.FileSize) : string.Empty;
        }

        #region File Operations

        private async void BtnFileOpenOrDelete_Click(object sender, RoutedEventArgs e)
        {
            if (Comment.HasFile)
            {
                // 파일 삭제 모드
                if (string.IsNullOrEmpty(FileToDelete))
                {
                    FileToDelete = Comment.FileName;
                }

                Comment.HasFile = false;
                Comment.FileName = string.Empty;
                Comment.FileSize = 0;
                FileNameText = "파일 추가";
                FileSizeText = string.Empty;
            }
            else
            {
                // 파일 선택 모드
                await SelectFileAsync();
            }
        }

        private async Task SelectFileAsync()
        {
            try
            {
                var picker = new FileOpenPicker();
                picker.FileTypeFilter.Add("*");

                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                var file = await picker.PickSingleFileAsync();
                if (file != null)
                {
                    OrgFilePath = file.Path;
                    string filename = Path.GetFileNameWithoutExtension(OrgFilePath);
                    string fileext = Path.GetExtension(OrgFilePath);

                    // 카테고리 디렉토리 확인
                    Board.EnsureCategoryDirectory(Post.Category);
                    string folder = $@"{Board.Data_Dir}\{Post.Category}";

                    // 중복 파일명 처리
                    SaveFilePath = $@"{folder}\{filename}{fileext}";
                    while (File.Exists(SaveFilePath))
                    {
                        filename += "_1";
                        SaveFilePath = $@"{folder}\{filename}{fileext}";
                    }

                    var props = await file.GetBasicPropertiesAsync();
                    Comment.FileSize = (int)props.Size;
                    Comment.HasFile = true;
                    Comment.FileName = $"{filename}{fileext}";
                    FileNameText = Comment.FileName;
                    FileSizeText = Tool.FormatSize(Comment.FileSize);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"파일 선택 실패: {ex.Message}");
                await MessageBox.ShowAsync($"파일 선택 중 오류가 발생했습니다: {ex.Message}");
            }
        }

        private async void BtnFile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string filepath = string.IsNullOrEmpty(OrgFilePath)
                    ? Board.GetFilePath(Comment.FileName, Post.Category)
                    : OrgFilePath;

                if (File.Exists(filepath))
                {
                    await Windows.System.Launcher.LaunchFileAsync(
                        await Windows.Storage.StorageFile.GetFileFromPathAsync(filepath));
                }
                else
                {
                    await MessageBox.ShowAsync("파일을 찾을 수 없습니다.");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"파일 열기 실패: {ex.Message}");
                await MessageBox.ShowAsync($"파일을 열 수 없습니다: {ex.Message}");
            }
        }

        #endregion

        #region Comment Operations

        private async void BtnCommentSave_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(TBoxContent.Text))
            {
                await MessageBox.ShowAsync("내용이 없습니다");
                return;
            }

            try
            {
                // 버튼 비활성화 (중복 클릭 방지)
                BtnCommentSave.IsEnabled = false;

                Comment.DateTime = DateTime.Now;
                Comment.Post = Post.No;
                Comment.Content = TBoxContent.Text;

                // 서비스를 통한 저장 (트랜잭션 자동 처리)
                if (Comment.No <= 0)
                {
                    // 새 댓글 생성
                    Comment.No = await _service.CreateCommentAsync(Comment);
                }
                else
                {
                    // 기존 댓글 수정
                    await _service.UpdateCommentAsync(Comment);
                }

                if (Comment.No > 0)
                {
                    // 파일 처리
                    await HandleFileSaveAsync();

                    BtnCommentDelete.Visibility = Visibility.Visible;
                    await MessageBox.ShowAsync("댓글이 저장되었습니다.");
                }
                else
                {
                    await MessageBox.ShowAsync("댓글 저장에 실패했습니다.");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"댓글 저장 실패: {ex.Message}");
                await MessageBox.ShowAsync($"댓글 저장 중 오류가 발생했습니다: {ex.Message}");
            }
            finally
            {
                BtnCommentSave.IsEnabled = true;
            }
        }

        private async Task HandleFileSaveAsync()
        {
            try
            {
                // 삭제할 파일 처리
                if (!string.IsNullOrEmpty(FileToDelete))
                {
                    string fileToDeletePath = Board.GetFilePath(FileToDelete, Post.Category);
                    if (File.Exists(fileToDeletePath))
                    {
                        await Task.Run(() => File.Delete(fileToDeletePath));
                        System.Diagnostics.Debug.WriteLine($"파일 삭제됨: {FileToDelete}");
                    }
                    FileToDelete = string.Empty;
                }

                // 새 파일 저장
                if (Comment.HasFile && !string.IsNullOrEmpty(OrgFilePath) && File.Exists(OrgFilePath))
                {
                    Board.EnsureCategoryDirectory(Post.Category);
                    await Task.Run(() => File.Copy(OrgFilePath, SaveFilePath, true));
                    System.Diagnostics.Debug.WriteLine($"파일 저장됨: {SaveFilePath}");
                    OrgFilePath = string.Empty;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"파일 처리 실패: {ex.Message}");
            }
        }

        private async void BtnCommentDelete_Click(object sender, RoutedEventArgs e)
        {
            if (Comment.No < 1) return;

            try
            {
                // 삭제 확인 다이얼로그
                var confirmed = await MessageBox.ShowConfirmAsync(
                    "정말로 이 댓글을 삭제하시겠습니까?",
                    "댓글 삭제", "삭제", "취소");
                if (!confirmed)
                {
                    return;
                }

                // 서비스를 통한 삭제 (트랜잭션 + 파일 삭제 자동 처리)
                bool success = await _service.DeleteCommentAsync(Comment.No, Post.Category);

                if (success)
                {
                    // UI에서 제거
                    if (this.Parent is Panel panel)
                    {
                        panel.Children.Remove(this);
                    }
                }
                else
                {
                    await MessageBox.ShowAsync("댓글 삭제에 실패했습니다.");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"댓글 삭제 실패: {ex.Message}");
                await MessageBox.ShowAsync($"댓글 삭제 중 오류가 발생했습니다: {ex.Message}");
            }
        }

        #endregion

        #region Helper Methods


        #endregion
    }
}

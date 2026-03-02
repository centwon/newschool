using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Board;
using NewSchool.Board.Services;
using NewSchool.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace NewSchool.Dialogs
{
    /// <summary>
    /// 수업 자료 편집 다이얼로그
    /// Board 시스템 연동
    /// </summary>
    public sealed partial class MaterialEditDialog : ContentDialog
    {
        private Post? _post;
        private readonly string _category;
        private readonly string _subject;
        private readonly bool _isEdit;

        /// <summary>
        /// 첨부파일 목록
        /// </summary>
        public ObservableCollection<PostFileViewModel> Files { get; } = new();
        
        /// <summary>
        /// 새로 추가할 파일 목록
        /// </summary>
        private List<StorageFile> _newFiles = new();

        /// <summary>
        /// 삭제 여부
        /// </summary>
        public bool IsDeleted { get; private set; }

        /// <summary>
        /// 새 자료 추가
        /// </summary>
        public MaterialEditDialog(string category, string subject)
        {
            this.InitializeComponent();

            _category = category;
            _subject = subject;
            _isEdit = false;

            Title = "자료 추가";
        }

        /// <summary>
        /// 기존 자료 수정
        /// </summary>
        public MaterialEditDialog(Post post)
        {
            this.InitializeComponent();

            _post = post;
            _category = post.Category;
            _subject = post.Subject;
            _isEdit = true;

            Title = "자료 수정";
            BtnDelete.Visibility = Visibility.Visible;

            LoadPostData();
        }

        /// <summary>
        /// 기존 데이터 로드
        /// </summary>
        private async void LoadPostData()
        {
            if (_post == null) return;

            TxtTitle.Text = _post.Title;
            TxtContent.Text = _post.Content;

            // 첨부파일 로드
            await LoadFilesAsync();
        }

        /// <summary>
        /// 첨부파일 목록 로드
        /// </summary>
        private async Task LoadFilesAsync()
        {
            if (_post == null) return;

            try
            {
                using var boardService = Board.Board.CreateService();
                var files = await boardService.GetPostFilesByPostAsync(_post.No);

                Files.Clear();
                foreach (var file in files)
                {
                    Files.Add(new PostFileViewModel(file));
                }

                UpdateFileListVisibility();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MaterialEditDialog] 파일 로드 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 파일 목록 표시 업데이트
        /// </summary>
        private void UpdateFileListVisibility()
        {
            bool hasFiles = Files.Count > 0;
            FileListView.Visibility = hasFiles ? Visibility.Visible : Visibility.Collapsed;
            FileListView.ItemsSource = Files;
        }

        /// <summary>
        /// 에러 표시
        /// </summary>
        private void ShowError(string message)
        {
            ErrorInfoBar.Message = message;
            ErrorInfoBar.IsOpen = true;
        }

        /// <summary>
        /// 에러 숨김
        /// </summary>
        private void HideError()
        {
            ErrorInfoBar.IsOpen = false;
        }

        /// <summary>
        /// 파일 추가 버튼
        /// </summary>
        private async void BtnAddFile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FileOpenPicker();
                picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                picker.FileTypeFilter.Add("*");

                // WinUI3에서 윈도우 핸들 설정
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                var files = await picker.PickMultipleFilesAsync();
                if (files == null || files.Count == 0) return;

                foreach (var file in files)
                {
                    _newFiles.Add(file);
                    
                    // 임시로 목록에 표시
                    var props = await file.GetBasicPropertiesAsync();
                    Files.Add(new PostFileViewModel
                    {
                        No = -1, // 임시
                        FileName = file.Name,
                        FileSize = (long)props.Size,
                        TempFile = file
                    });
                }

                UpdateFileListVisibility();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MaterialEditDialog] 파일 선택 실패: {ex.Message}");
                ShowError("파일을 선택할 수 없습니다.");
            }
        }

        /// <summary>
        /// 파일 삭제 버튼
        /// </summary>
        private async void BtnRemoveFile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag == null) return;

            int fileNo = Convert.ToInt32(btn.Tag);

            // 임시 파일인 경우 (No == -1)
            if (fileNo == -1)
            {
                // _newFiles에서 제거
                var fileVm = Files.FirstOrDefault(f => f.No == -1 && btn.DataContext == f);
                if (fileVm != null)
                {
                    Files.Remove(fileVm);
                    if (fileVm.TempFile != null)
                    {
                        _newFiles.Remove(fileVm.TempFile);
                    }
                }
            }
            else
            {
                // DB에서 삭제
                try
                {
                    using var boardService = Board.Board.CreateService();
                    await boardService.DeletePostFileAsync(fileNo, _category);

                    var fileVm = Files.FirstOrDefault(f => f.No == fileNo);
                    if (fileVm != null)
                    {
                        Files.Remove(fileVm);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MaterialEditDialog] 파일 삭제 실패: {ex.Message}");
                    ShowError("파일 삭제에 실패했습니다.");
                }
            }

            UpdateFileListVisibility();
        }

        /// <summary>
        /// 저장 버튼
        /// </summary>
        private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            var deferral = args.GetDeferral();

            try
            {
                HideError();

                // 유효성 검사
                if (string.IsNullOrWhiteSpace(TxtTitle.Text))
                {
                    ShowError("제목을 입력해주세요.");
                    args.Cancel = true;
                    return;
                }

                using var boardService = Board.Board.CreateService();

                int postNo;
                if (_isEdit && _post != null)
                {
                    // 수정
                    _post.Title = TxtTitle.Text.Trim();
                    _post.Content = TxtContent.Text.Trim();

                    postNo = await boardService.SavePostAsync(_post);
                }
                else
                {
                    // 추가
                    var newPost = new Post
                    {
                        User = Settings.User.Value,
                        DateTime = DateTime.Now,
                        Category = _category,
                        Subject = _subject,
                        Title = TxtTitle.Text.Trim(),
                        Content = TxtContent.Text.Trim()
                    };

                    postNo = await boardService.SavePostAsync(newPost);
                }

                if (postNo <= 0)
                {
                    ShowError("저장에 실패했습니다.");
                    args.Cancel = true;
                    return;
                }

                // 새 파일 업로드
                foreach (var file in _newFiles)
                {
                    await UploadFileAsync(boardService, postNo, file);
                }

                Debug.WriteLine($"[MaterialEditDialog] 저장 완료: PostNo={postNo}");
            }
            catch (Exception ex)
            {
                ShowError($"저장 중 오류가 발생했습니다.\n{ex.Message}");
                args.Cancel = true;
            }
            finally
            {
                deferral.Complete();
            }
        }

        /// <summary>
        /// 파일 업로드
        /// </summary>
        private async Task UploadFileAsync(BoardService boardService, int postNo, StorageFile file)
        {
            try
            {
                // 카테고리 디렉토리 확인
                Board.Board.EnsureCategoryDirectory(_category);

                // 파일명 생성 (중복 방지)
                string uniqueFileName = $"{DateTime.Now:yyyyMMddHHmmss}_{file.Name}";
                string destPath = Board.Board.GetFilePath(uniqueFileName, _category);

                // 파일 복사
                var destFolder = await StorageFolder.GetFolderFromPathAsync(
                    Path.GetDirectoryName(destPath)!);
                await file.CopyAsync(destFolder, Path.GetFileName(destPath));

                // DB에 기록
                var props = await file.GetBasicPropertiesAsync();
                var postFile = new PostFile
                {
                    Post = postNo,
                    DateTime = DateTime.Now,
                    FileName = uniqueFileName,
                    FileSize = (long)props.Size
                };

                await boardService.AddPostFileAsync(postFile);

                Debug.WriteLine($"[MaterialEditDialog] 파일 업로드 완료: {uniqueFileName}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MaterialEditDialog] 파일 업로드 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 삭제 버튼
        /// </summary>
        private async void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (_post == null) return;

            var confirmed = await MessageBox.ShowConfirmAsync(
                "이 자료를 삭제하시겠습니까?\n첨부파일도 함께 삭제됩니다.",
                "자료 삭제", "삭제", "취소");
            if (!confirmed) return;

            try
            {
                using var boardService = Board.Board.CreateService();
                bool success = await boardService.DeletePostAsync(_post.No, _category);

                if (success)
                {
                    IsDeleted = true;
                    this.Hide();
                }
                else
                {
                    ShowError("삭제에 실패했습니다.");
                }
            }
            catch (Exception ex)
            {
                ShowError($"삭제 중 오류가 발생했습니다.\n{ex.Message}");
            }
        }
    }

    /// <summary>
    /// PostFile 표시용 ViewModel
    /// </summary>
    [Microsoft.UI.Xaml.Data.Bindable]
    public class PostFileViewModel
    {
        public int No { get; set; }
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public StorageFile? TempFile { get; set; }

        public PostFileViewModel() { }

        public PostFileViewModel(PostFile file)
        {
            No = file.No;
            FileName = file.FileName;
            FileSize = file.FileSize;
        }

        public string FileSizeDisplay
        {
            get
            {
                if (FileSize < 1024)
                    return $"{FileSize} B";
                if (FileSize < 1024 * 1024)
                    return $"{FileSize / 1024.0:F1} KB";
                return $"{FileSize / (1024.0 * 1024):F1} MB";
            }
        }
    }
}

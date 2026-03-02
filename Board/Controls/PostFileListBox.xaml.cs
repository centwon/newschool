using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;
using NewSchool.Controls;

namespace NewSchool.Board.Controls
{
    public sealed partial class PostFileListBox : UserControl
    {
        public ObservableCollection<FileItemBox> FileBoxes { get; } = new();
        public ObservableCollection<PostFile> FilesToDelete { get; } = new();

        private string _category = string.Empty;
        private bool _isReadOnly = false;
        private Post? _post;

        /// <summary>파일 변경 이벤트</summary>
        public event EventHandler? FileBoxesChanged;

        public string Category
        {
            get => _category;
            set => _category = value;
        }

        /// <summary>Post 객체</summary>
        public Post? Post
        {
            get => _post;
            set
            {
                _post = value;
                if (_post != null)
                {
                    _category = _post.Category ?? "Memo";
                }
            }
        }

        /// <summary>파일 개수</summary>
        public int FileCount => FileBoxes.Count;

        /// <summary>
        /// 읽기 전용 모드 (추가/삭제 불가)
        /// </summary>
        public bool IsReadOnly
        {
            get => _isReadOnly;
            set
            {
                _isReadOnly = value;
                UpdateReadOnlyState();
            }
        }

        public PostFileListBox()
        {
            this.InitializeComponent();
            FileItemsControl.ItemsSource = FileBoxes;
        }

        /// <summary>
        /// 읽기 전용 상태 업데이트
        /// </summary>
        private void UpdateReadOnlyState()
        {
            // 버튼 표시/숨김
            AddFileButton.Visibility = _isReadOnly ? Visibility.Collapsed : Visibility.Visible;
            RemoveFileButton.Visibility = _isReadOnly ? Visibility.Collapsed : Visibility.Visible;

            // 헤더 텍스트 변경
            HeaderTextBlock.Text = _isReadOnly
                ? "첨부파일"
                : "파일을 끌어 놓거나 '+' 버튼을 눌러 추가하세요";

            // 드롭 영역 비활성화
            FileDropArea.AllowDrop = !_isReadOnly;

            // 체크박스 숨김
            foreach (var fileBox in FileBoxes)
            {
                fileBox.ShowCheckBox = !_isReadOnly;
            }
        }

        /// <summary>
        /// 기존 파일 로드
        /// </summary>
        public void LoadFiles(System.Collections.Generic.List<PostFile> files, string category, bool readOnly = false)
        {
            _category = category;
            IsReadOnly = readOnly;
            FileBoxes.Clear();

            foreach (var file in files)
            {
                var fileBox = new FileItemBox
                {
                    PostFile = file,
                    Category = category,
                    OrgFilePath = string.Empty,
                    ShowCheckBox = !readOnly
                };
                FileBoxes.Add(fileBox);
            }

            FileBoxesChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 파일 설정 (MemoBoard용)
        /// </summary>
        public void SetFiles(System.Collections.Generic.List<PostFile> files)
        {
            FileBoxes.Clear();

            foreach (var file in files)
            {
                var fileBox = new FileItemBox
                {
                    PostFile = file,
                    Category = _category,
                    OrgFilePath = string.Empty,
                    ShowCheckBox = !_isReadOnly
                };
                FileBoxes.Add(fileBox);
            }

            FileBoxesChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 파일 추가 버튼
        /// </summary>
        private async void AddFileButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isReadOnly) return;
            await AddFilesAsync();
        }

        private async Task AddFilesAsync()
        {
            var picker = new FileOpenPicker();
            var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
            InitializeWithWindow.Initialize(picker, hwnd);

            picker.FileTypeFilter.Add("*");
            picker.ViewMode = PickerViewMode.List;

            var files = await picker.PickMultipleFilesAsync();

            if (files != null && files.Count > 0)
            {
                foreach (var file in files)
                {
                    await AddFileAsync(file);
                }
            }
        }

        private async Task AddFileAsync(StorageFile file)
        {
            var properties = await file.GetBasicPropertiesAsync();
            var postFile = new PostFile
            {
                FileName = file.Name,
                FileSize = (int)properties.Size,
                DateTime = DateTime.Now
            };

            var fileBox = new FileItemBox
            {
                PostFile = postFile,
                Category = _category,
                OrgFilePath = file.Path,
                ShowCheckBox = !_isReadOnly
            };

            FileBoxes.Add(fileBox);
            FileBoxesChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 선택 파일 삭제
        /// </summary>
        private void RemoveFileButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isReadOnly) return;

            bool removed = false;
            for (int i = FileBoxes.Count - 1; i >= 0; i--)
            {
                var fileBox = FileBoxes[i];
                if (fileBox.IsSelected)
                {
                    // 기존 파일이면 삭제 목록에 추가
                    if (string.IsNullOrEmpty(fileBox.OrgFilePath) && fileBox.PostFile != null)
                    {
                        FilesToDelete.Add(fileBox.PostFile);
                    }
                    FileBoxes.RemoveAt(i);
                    removed = true;
                }
            }

            if (removed)
            {
                FileBoxesChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// 폴더 열기
        /// </summary>
        private async void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(_category))
                {
                    await MessageBox.ShowAsync("카테고리를 먼저 선택해주세요.");
                    return;
                }

                string folderPath = Path.Combine(Board.Data_Dir, _category);

                if (!Directory.Exists(folderPath))
                {
                    Directory.CreateDirectory(folderPath);
                }

                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{folderPath}\"",
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"폴더 열기 실패: {ex.Message}");

                await MessageBox.ShowErrorAsync($"폴더를 열 수 없습니다.\n{ex.Message}", ex);
            }
        }

        /// <summary>
        /// 드래그 오버
        /// </summary>
        private void FileDropArea_DragOver(object sender, DragEventArgs e)
        {
            if (_isReadOnly)
            {
                e.AcceptedOperation = DataPackageOperation.None;
            }
            else
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.DragUIOverride.Caption = "파일 추가";
            }
        }

        /// <summary>
        /// 파일 드롭
        /// </summary>
        private async void FileDropArea_Drop(object sender, DragEventArgs e)
        {
            if (_isReadOnly) return;

            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();

                foreach (var item in items)
                {
                    if (item is StorageFile file)
                    {
                        await AddFileAsync(file);
                    }
                }
            }
        }
    }
}

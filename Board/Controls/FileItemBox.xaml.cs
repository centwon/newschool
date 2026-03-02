using System;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Threading.Tasks;
using NewSchool.Controls;

namespace NewSchool.Board.Controls
{
    public sealed partial class FileItemBox : UserControl
    {
        public event EventHandler<bool>? SelectionChanged;

        private PostFile? _postFile;
        private string _orgFilePath = string.Empty;
        private string _category = string.Empty;
        private bool _showCheckBox = true;

        public bool IsSelected { get; private set; }

        public PostFile? PostFile
        {
            get => _postFile;
            set
            {
                _postFile = value;
                UpdateUI();
            }
        }

        public string OrgFilePath
        {
            get => _orgFilePath;
            set => _orgFilePath = value;
        }

        public string Category
        {
            get => _category;
            set => _category = value;
        }

        /// <summary>
        /// 체크박스 표시 여부
        /// </summary>
        public bool ShowCheckBox
        {
            get => _showCheckBox;
            set
            {
                _showCheckBox = value;
                SelectCheckBox.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        public FileItemBox()
        {
            this.InitializeComponent();
        }

        private void UpdateUI()
        {
            if (_postFile != null)
            {
                FileNameTextBlock.Text = _postFile.FileName;
                FileSizeTextBlock.Text = $"({FormatFileSize(_postFile.FileSize)})";
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

        private void SelectCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            IsSelected = true;
            SelectionChanged?.Invoke(this, true);
        }

        private void SelectCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            IsSelected = false;
            SelectionChanged?.Invoke(this, false);
        }

        private async void FileButton_Click(object sender, RoutedEventArgs e)
        {
            if (_postFile == null) return;

            try
            {
                string filepath;
                if (string.IsNullOrEmpty(_orgFilePath))
                {
                    // 저장된 파일
                    filepath = Board.GetFilePath(_postFile.FileName, _category);
                }
                else
                {
                    // 새로 추가한 파일
                    filepath = _orgFilePath;
                }

                System.Diagnostics.Debug.WriteLine($"파일 경로: {filepath}");

                if (!File.Exists(filepath))
                {
                    await ShowDialogAsync("파일 없음", "파일이 존재하지 않습니다.");
                    return;
                }

                // Process.Start로 파일 열기
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = filepath,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);

                System.Diagnostics.Debug.WriteLine("파일 열기 성공");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"파일 열기 실패: {ex.Message}");
                await ShowDialogAsync("오류", $"파일을 열 수 없습니다.\n{ex.Message}");
            }
        }

        private async Task ShowDialogAsync(string title, string message)
        {
            try
            {
                await MessageBox.ShowAsync(message, title);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"다이얼로그 표시 실패: {ex.Message}");
            }
        }
    }
}

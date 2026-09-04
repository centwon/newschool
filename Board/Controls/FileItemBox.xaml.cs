using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Controls;

namespace NewSchool.Board.Controls;

public sealed partial class FileItemBox : UserControl
{
    // 선택이 바뀔 때 알리던 SelectionChanged 는 구독자가 한 곳도 없어 지웠다(2026-08-31).
    // 지금 선택 상태를 보는 쪽(PostFileListBox 의 삭제 버튼)은 IsSelected 를 그때 훑는다.

    private PostFile? _postFile;
    private string _orgFilePath = string.Empty;
    private string _category = string.Empty;
    private string _savedFilePath = string.Empty;
    private bool _showCheckBox = true;

    /// <summary>
    /// 이미 저장된 첨부의 <b>절대 경로</b>. 채워 두면 게시판의 카테고리 경로 대신 이것을 연다.
    ///
    /// <para>누가기록 첨부는 폴더를 분류가 아니라 학년도·학생으로 나누므로
    /// (<see cref="Models.StudentLogFile"/> 머리 주석) <c>Board.GetFilePath</c> 로는
    /// 경로가 나오지 않는다. 표시·열기 로직을 한 벌로 쓰려고 경로를 바깥에서 받는다.</para>
    /// </summary>
    public string SavedFilePath
    {
        get => _savedFilePath;
        set => _savedFilePath = value ?? string.Empty;
    }

    /// <summary>
    /// <see cref="PostFile"/> 없이 표시할 이름과 크기를 직접 준다(게시판 밖의 첨부용).
    /// </summary>
    public void SetDisplay(string fileName, long fileSize)
    {
        FileNameTextBlock.Text = fileName ?? string.Empty;
        FileSizeTextBlock.Text = $"({FormatFileSize(fileSize)})";
    }

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
    }

    private void SelectCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        IsSelected = false;
    }

    private async void FileButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // 경로를 정하는 순서: 새로 붙인 원본 → 바깥에서 준 저장 경로 → 게시판 카테고리 경로.
            // 앞의 둘만으로 정해지는 첨부(누가기록)는 PostFile 이 없어도 열려야 한다.
            string filepath;
            if (!string.IsNullOrEmpty(_orgFilePath))
                filepath = _orgFilePath;                      // 새로 추가한 파일
            else if (!string.IsNullOrEmpty(_savedFilePath))
                filepath = _savedFilePath;                    // 바깥에서 준 저장 경로
            else if (_postFile != null)
                filepath = Board.GetFilePath(_postFile.FileName, _category);
            else
                return;

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
            NewSchool.Logging.Log.Error("FileItemBox", "첨부 파일을 열지 못했다", ex);
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
            NewSchool.Logging.Log.Error("FileItemBox", "안내 대화상자를 띄우지 못했다 — 사용자는 아무 반응도 못 본다", ex);
        }
    }
}

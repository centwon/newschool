using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Board.Controls;
using NewSchool.Helpers;
using NewSchool.Models;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace NewSchool.Controls;

/// <summary>
/// 누가기록 첨부 목록 — 붙이고 빼는 화면.
///
/// <para>이 컨트롤은 <b>파일을 건드리지 않는다.</b> 사용자가 무엇을 붙였고 무엇을 뺐는지만
/// 들고 있다가, <b>저장을 누를 때</b> 그 목록을 넘긴다
/// (<see cref="Services.StudentLogAttachments.ApplyAsync"/>). 새 기록은 저장 전에
/// <c>No</c> 가 없어 어차피 붙일 수 없고, 취소하고 창을 닫은 사용자의 파일이 폴더에
/// 남는 것도 이 순서라야 막을 수 있다.</para>
///
/// <para>줄 하나를 그리는 일은 게시판의 <see cref="FileItemBox"/> 를 그대로 쓴다 —
/// 이름·크기·열기가 같은 모양이라 한 벌이면 된다. 다만 저장 경로 규칙이 다르므로
/// (누가기록은 학년도·학생으로 나눈다) 경로는 <c>SavedFilePath</c> 로 직접 준다.</para>
/// </summary>
public sealed partial class StudentLogFileListBox : UserControl
{
    /// <summary>
    /// 첨부 한 건의 최대 크기. 게시판과 같은 100MB 로 맞춘다 — 사용자가 보기에 같은
    /// "첨부"인데 한도가 다르면 어느 쪽이 맞는지 알 방법이 없다.
    /// </summary>
    private const long MaxFileSizeBytes = 100L * 1024 * 1024;

    private readonly ObservableCollection<FileItemBox> _fileBoxes = new();

    /// <summary>새로 붙인 파일들의 원본 경로(저장할 때 복사한다).</summary>
    private readonly Dictionary<FileItemBox, string> _newFiles = new();

    /// <summary>이미 저장돼 있던 첨부들.</summary>
    private readonly Dictionary<FileItemBox, StudentLogFile> _existing = new();

    /// <summary>빼기로 한 기존 첨부 — 저장할 때 DB 와 실물에서 지운다.</summary>
    private readonly List<StudentLogFile> _toDelete = new();

    private bool _isReadOnly;

    public StudentLogFileListBox()
    {
        this.InitializeComponent();
        FileItemsControl.ItemsSource = _fileBoxes;
        _fileBoxes.CollectionChanged += (_, _) => UpdateEmptyState();
        UpdateEmptyState();
    }

    /// <summary>읽기 전용이면 붙이기·빼기 버튼을 감춘다.</summary>
    public bool IsReadOnly
    {
        get => _isReadOnly;
        set
        {
            _isReadOnly = value;
            AddFileButton.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
            RemoveFileButton.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
            HeaderTextBlock.Text = value ? "첨부파일" : "첨부파일 — '+' 로 붙입니다";

            foreach (var box in _fileBoxes) box.ShowCheckBox = !value;
        }
    }

    /// <summary>지금 화면에 있는 첨부 수(기존 + 새로 붙인 것).</summary>
    public int Count => _fileBoxes.Count;

    /// <summary>저장할 때 복사해야 할 원본 경로들.</summary>
    public IReadOnlyList<string> NewFilePaths => _newFiles.Values.ToList();

    /// <summary>저장할 때 지워야 할 기존 첨부들.</summary>
    public IReadOnlyList<StudentLogFile> FilesToDelete => _toDelete;

    /// <summary>저장된 첨부를 목록에 올린다. 다시 부르면 화면과 대기 중인 변경이 모두 초기화된다.</summary>
    public void LoadFiles(IEnumerable<StudentLogFile> files)
    {
        _fileBoxes.Clear();
        _newFiles.Clear();
        _existing.Clear();
        _toDelete.Clear();

        foreach (var file in files ?? Enumerable.Empty<StudentLogFile>())
        {
            var box = new FileItemBox
            {
                // 게시판 경로 규칙을 타지 않도록 절대 경로를 직접 준다.
                SavedFilePath = Services.StudentLogAttachments.GetFilePath(file),
                ShowCheckBox = !_isReadOnly,
            };
            box.SetDisplay(file.FileName, file.FileSize);

            _existing[box] = file;
            _fileBoxes.Add(box);
        }

        UpdateEmptyState();
    }

    /// <summary>저장이 끝난 뒤 대기 목록을 비운다(같은 창에서 계속 편집할 때 두 번 반영되지 않게).</summary>
    public void MarkApplied()
    {
        _newFiles.Clear();
        _toDelete.Clear();
    }

    private void UpdateEmptyState() =>
        EmptyTextBlock.Visibility = _fileBoxes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    #region 붙이기

    private async void AddFileButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isReadOnly) return;

        try
        {
            var picker = new FileOpenPicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
            picker.FileTypeFilter.Add("*");
            picker.ViewMode = PickerViewMode.List;

            var files = await picker.PickMultipleFilesAsync();
            if (files == null) return;

            foreach (var file in files) await AddFileAsync(file);
        }
        catch (Exception ex)
        {
            await UserErrorReporter.ReportAsync("첨부파일 선택", ex);
        }
    }

    private async Task AddFileAsync(StorageFile file)
    {
        // 첨부는 열 때 UseShellExecute 로 열린다 — 무엇을 붙일 수 있는지가 유일한 방어선이고
        // 그 목록은 게시판과 한 벌이다(Helpers.AttachmentPolicy).
        if (AttachmentPolicy.IsBlocked(file.Name))
        {
            await MessageBox.ShowErrorAsync(AttachmentPolicy.BlockedMessage(file.Name));
            return;
        }

        var properties = await file.GetBasicPropertiesAsync();
        if (properties.Size > MaxFileSizeBytes)
        {
            await MessageBox.ShowErrorAsync(
                $"'{file.Name}' 이(가) 너무 큽니다. 첨부는 한 개당 {MaxFileSizeBytes / (1024 * 1024)}MB 까지입니다.");
            return;
        }

        var box = new FileItemBox
        {
            OrgFilePath = file.Path,   // 아직 복사 전이라 원본을 연다
            ShowCheckBox = !_isReadOnly,
        };
        box.SetDisplay(file.Name, (long)properties.Size);

        _newFiles[box] = file.Path;
        _fileBoxes.Add(box);
    }

    #endregion

    #region 빼기

    private void RemoveFileButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isReadOnly) return;

        for (int i = _fileBoxes.Count - 1; i >= 0; i--)
        {
            var box = _fileBoxes[i];
            if (!box.IsSelected) continue;

            // 이미 저장돼 있던 것만 지울 목록에 넣는다. 방금 붙였다가 뺀 것은
            // 아직 복사되지 않았으므로 지울 것이 없다.
            if (_existing.TryGetValue(box, out var saved)) _toDelete.Add(saved);

            _existing.Remove(box);
            _newFiles.Remove(box);
            _fileBoxes.RemoveAt(i);
        }
    }

    #endregion
}

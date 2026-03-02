using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Models;
using NewSchool.Services;
using Windows.Storage.Pickers;

namespace NewSchool.Controls;

/// <summary>
/// 공통 첨부파일 컨트롤
/// OwnerType + OwnerNo를 설정하면 해당 레코드의 첨부파일을 관리
/// LessonLog, ClassDiary, CourseSection 등에서 사용
/// </summary>
public sealed partial class AttachmentBox : UserControl
{
    #region Fields

    private AttachmentService? _service;
    private string _ownerType = string.Empty;
    private int _ownerNo;
    private bool _isReadOnly;

    #endregion

    #region Properties

    public ObservableCollection<Attachment> Attachments { get; } = new();

    /// <summary>읽기 전용 모드 (삭제/추가 버튼 숨김)</summary>
    public bool IsReadOnly
    {
        get => _isReadOnly;
        set
        {
            _isReadOnly = value;
            BtnAddFile.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    #endregion

    #region Events

    /// <summary>첨부파일 변경 시 (추가/삭제)</summary>
    public event EventHandler? AttachmentsChanged;

    #endregion

    public AttachmentBox()
    {
        this.InitializeComponent();
        LvFiles.ItemsSource = Attachments;
    }

    #region Public Methods

    /// <summary>
    /// 소유자 설정 및 첨부파일 로드
    /// </summary>
    /// <param name="ownerType">"LessonLog", "ClassDiary", "CourseSection"</param>
    /// <param name="ownerNo">소유자 레코드 No</param>
    public async Task LoadAsync(string ownerType, int ownerNo)
    {
        _ownerType = ownerType;
        _ownerNo = ownerNo;

        _service ??= new AttachmentService();

        await RefreshAsync();
    }

    /// <summary>
    /// 새로고침
    /// </summary>
    public async Task RefreshAsync()
    {
        if (_service == null || string.IsNullOrEmpty(_ownerType) || _ownerNo <= 0)
        {
            UpdateEmptyState();
            return;
        }

        try
        {
            Attachments.Clear();

            var files = await _service.GetByOwnerAsync(_ownerType, _ownerNo);
            foreach (var f in files)
            {
                Attachments.Add(f);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AttachmentBox] RefreshAsync 오류: {ex.Message}");
        }

        UpdateEmptyState();
    }

    /// <summary>
    /// 외부에서 파일 추가 (드래그앤드롭 등)
    /// </summary>
    public async Task AddFilesAsync(IEnumerable<string> filePaths)
    {
        if (_service == null || _ownerNo <= 0) return;

        try
        {
            await _service.AddFilesAsync(_ownerType, _ownerNo, filePaths);
            await RefreshAsync();
            AttachmentsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AttachmentBox] AddFilesAsync 오류: {ex.Message}");
        }
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// 파일 추가 버튼
    /// </summary>
    private async void BtnAddFile_Click(object sender, RoutedEventArgs e)
    {
        if (_service == null || _ownerNo <= 0)
        {
            await MessageBox.ShowAsync("먼저 레코드를 저장한 후 파일을 첨부할 수 있습니다.");
            return;
        }

        try
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add("*");

            // WinUI3에서 HWND 필요
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var files = await picker.PickMultipleFilesAsync();
            if (files == null || files.Count == 0) return;

            foreach (var file in files)
            {
                await _service.AddFileAsync(_ownerType, _ownerNo, file.Path);
            }

            await RefreshAsync();
            AttachmentsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AttachmentBox] BtnAddFile 오류: {ex.Message}");
            await MessageBox.ShowAsync($"파일 추가 중 오류가 발생했습니다.\n{ex.Message}");
        }
    }

    /// <summary>
    /// 폴더 열기 버튼
    /// </summary>
    private async void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_service == null || _ownerNo <= 0) return;

        var opened = await _service.OpenFolderAsync(_ownerType, _ownerNo);
        if (!opened)
        {
            await MessageBox.ShowAsync("첨부파일 폴더가 아직 생성되지 않았습니다.");
        }
    }

    /// <summary>
    /// 파일 클릭 → 기본 프로그램으로 열기
    /// </summary>
    private async void LvFiles_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Attachment attachment && _service != null)
        {
            var opened = await _service.OpenFileAsync(attachment.No);
            if (!opened)
            {
                await MessageBox.ShowAsync("파일을 열 수 없습니다. 파일이 존재하는지 확인해주세요.");
            }
        }
    }

    /// <summary>
    /// 파일 삭제 버튼
    /// </summary>
    private async void BtnDeleteFile_Click(object sender, RoutedEventArgs e)
    {
        if (_isReadOnly || _service == null) return;

        if (sender is Button btn && btn.Tag is int no)
        {
            var attachment = await _service.GetByIdAsync(no);
            if (attachment == null) return;

            var confirmed = await MessageBox.ShowConfirmAsync(
                $"'{attachment.OriginalFileName}'을(를) 삭제하시겠습니까?",
                "첨부파일 삭제", "삭제", "취소");
            if (!confirmed) return;

            await _service.DeleteAsync(no);
            await RefreshAsync();
            AttachmentsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    #endregion

    #region Helper Methods

    private void UpdateEmptyState()
    {
        bool isEmpty = Attachments.Count == 0;
        TxtEmpty.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
        LvFiles.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
    }

    #endregion
}

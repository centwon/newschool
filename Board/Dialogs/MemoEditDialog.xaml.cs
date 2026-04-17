using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using NewSchool.Board.Services;
using NewSchool.Controls;
using NewSchool.Models;

namespace NewSchool.Board.Dialogs;

public sealed partial class MemoEditDialog : ContentDialog
{
    private readonly Post _post;
    private bool _isLoading = false;

    public MemoEditDialog(Post post)
    {
        _post = post ?? throw new ArgumentNullException(nameof(post));
        
        InitializeComponent();
        
        Loaded += MemoEditDialog_Loaded;
    }

    private async void MemoEditDialog_Loaded(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _isLoading = true;

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
            Editor.Text = _post.Content ?? "";

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
        finally
        {
            _isLoading = false;
        }

        // 버튼 이벤트
        PrimaryButtonClick += MemoEditDialog_PrimaryButtonClick;
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

    private async void MemoEditDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // 비동기 작업을 위해 지연
        var deferral = args.GetDeferral();

        try
        {
            // 데이터 업데이트
            _post.IsCompleted = ChkCompleted.IsChecked == true;
            _post.Category = GetSelectedCategory();
            _post.Title = TxtTitle.Text;
            _post.Content = Editor.Text;
            _post.DateTime = DateTime.Now;

            using var service = Board.CreateService();
            
            // 1. Post 저장
            int postNo = await service.SavePostAsync(_post);

            if (postNo > 0)
            {
                string fileCategory = _post.Category;

                // 2. 기존 파일 삭제
                foreach (var fileToDelete in FileList.FilesToDelete)
                {
                    await service.DeletePostFileAsync(fileToDelete.No, fileCategory);
                    Debug.WriteLine($"[MemoEditDialog] 파일 삭제: {fileToDelete.FileName}");
                }

                // 3. 새 파일 저장
                foreach (var fileBox in FileList.FileBoxes)
                {
                    // OrgFilePath가 있으면 새로 추가된 파일
                    if (!string.IsNullOrEmpty(fileBox.OrgFilePath) && fileBox.PostFile != null)
                    {
                        var savedFile = await SaveFileAsync(
                            fileBox.OrgFilePath,
                            postNo,
                            fileCategory);

                        if (savedFile != null)
                        {
                            await service.AddPostFileAsync(savedFile);
                            Debug.WriteLine($"[MemoEditDialog] 파일 저장: {savedFile.FileName}");
                        }
                    }
                }

                // HasFile 플래그 업데이트
                _post.HasFile = FileList.FileCount > 0;
            }

            Debug.WriteLine($"[MemoEditDialog] 메모 저장 완료: No={_post.No}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MemoEditDialog] 저장 실패: {ex.Message}");
            
            // 오류 발생 시 다이얼로그 닫기 취소
            args.Cancel = true;
            
            await MessageBox.ShowErrorAsync($"메모 저장 중 오류가 발생했습니다.\n{ex.Message}", ex);
        }
        finally
        {
            deferral.Complete();
        }
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

    /// <summary>
    /// 파일 저장 (물리적 파일 복사)
    /// </summary>
    private async Task<PostFile?> SaveFileAsync(string sourceFilePath, int postNo, string category)
    {
        try
        {
            // 카테고리 디렉토리 확인 및 생성
            Board.EnsureCategoryDirectory(category);

            // 원본 파일 정보
            var sourceFile = await StorageFile.GetFileFromPathAsync(sourceFilePath);
            var properties = await sourceFile.GetBasicPropertiesAsync();

            // 고유한 파일명 생성
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var extension = Path.GetExtension(sourceFile.Name);
            var fileNameWithoutExt = Path.GetFileNameWithoutExtension(sourceFile.Name);
            var fileName = $"{timestamp}_{fileNameWithoutExt}{extension}";

            // 목적지 경로
            var destinationPath = Board.GetFilePath(fileName, category);
            var destinationFolder = await StorageFolder.GetFolderFromPathAsync(
                Path.GetDirectoryName(destinationPath));

            // 파일 복사
            await sourceFile.CopyAsync(destinationFolder, fileName, NameCollisionOption.ReplaceExisting);

            // PostFile 객체 생성
            var postFile = new PostFile
            {
                Post = postNo,
                FileName = fileName,
                FileSize = (int)properties.Size,
                DateTime = DateTime.Now
            };

            Debug.WriteLine($"[MemoEditDialog] 파일 저장 완료: {destinationPath}");
            return postFile;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MemoEditDialog] 파일 저장 실패: {ex.Message}");
            return null;
        }
    }
}

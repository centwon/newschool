using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using NewSchool.Board.Models;
using NewSchool.Board.Services;
using NewSchool.Controls;
using Windows.Storage;

namespace NewSchool.Board.Pages;

public sealed partial class PostEditPage : Page
{
    private Post? _post;
    private bool _isEditMode;
    private PostEditPageParameter? _parameter;

    public PostEditPage()
    {
        this.InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // 파라미터 처리
        int postNo = 0;
        if (e.Parameter is PostEditPageParameter param)
        {
            _parameter = param;
            postNo = param.PostNo;
        }
        else if (e.Parameter is int no)
        {
            postNo = no;
        }

        if (postNo > 0)
        {
            // 수정 모드
            _isEditMode = true;

            using var service = Board.CreateService();
            _post = await service.GetPostAsync(postNo, incrementReadCount: false);

            if (_post != null)
            {
                TitleTextBox.Text = _post.Title;
                ContentEditor.Text = _post.Content;
                SubjectTextBox.Text = _post.Subject;

                // 카테고리 선택
                foreach (ComboBoxItem item in CategoryComboBox.Items)
                {
                    if (item.Tag?.ToString() == _post.Category)
                    {
                        CategoryComboBox.SelectedItem = item;
                        break;
                    }
                }

                // 기존 첨부파일 로드
                var files = await service.GetPostFilesByPostAsync(postNo);
                FileListBox.LoadFiles(files, _post.Category);
            }
        }
        else
        {
            // 새 글 작성 모드
            _isEditMode = false;
            _post = new Post
            {
                DateTime = DateTime.Now,
                User = Settings.UserName ?? "사용자"
            };

            // 파라미터로 카테고리/Subject 설정
            if (_parameter != null)
            {
                if (!string.IsNullOrEmpty(_parameter.DefaultCategory))
                {
                    _post.Category = _parameter.DefaultCategory;

                    // 카테고리 콤보박스에서 선택
                    foreach (ComboBoxItem item in CategoryComboBox.Items)
                    {
                        if (item.Tag?.ToString() == _parameter.DefaultCategory)
                        {
                            CategoryComboBox.SelectedItem = item;
                            break;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(_parameter.DefaultSubject))
                {
                    _post.Subject = _parameter.DefaultSubject;
                    SubjectTextBox.Text = _parameter.DefaultSubject;
                }
            }
        }

        // 카테고리 ComboBox 표시 여부
        if (_parameter != null)
        {
            CategoryComboBox.IsEnabled = _parameter.AllowCategoryChange;
            if (!_parameter.AllowCategoryChange)
            {
                CategoryStack.Visibility = Visibility.Collapsed;
            }
        }

        PageTitle.Text = _isEditMode ? "게시글 수정" : "새 글 쓰기";
    }

    /// <summary>
    /// 카테고리 변경 시 FileListBox의 Category도 업데이트
    /// </summary>
    private void CategoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CategoryComboBox.SelectedItem is ComboBoxItem selectedItem)
        {
            FileListBox.Category = selectedItem.Tag?.ToString() ?? "";
        }
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateInput())
            return;

        try
        {
            SaveButton.IsEnabled = false;

            if (_post != null)
            {
                _post.Title = TitleTextBox.Text;
                _post.Content = ContentEditor.Text;
                _post.Subject = SubjectTextBox.Text.Trim();

                if (CategoryComboBox.SelectedItem is ComboBoxItem selectedItem)
                {
                    _post.Category = selectedItem.Tag?.ToString() ?? "";
                }
                else if (_parameter != null && !string.IsNullOrEmpty(_parameter.DefaultCategory))
                {
                    // 카테고리 고정 모드: 파라미터에서 카테고리 가져오기
                    _post.Category = _parameter.DefaultCategory;
                }

                _post.DateTime = DateTime.Now;

                using var service = Board.CreateCachedService();

                // 1. Post 저장
                int postNo = await service.SavePostAsync(_post);

                if (postNo > 0)
                {
                    // 2. 기존 파일 삭제 처리
                    foreach (var fileToDelete in FileListBox.FilesToDelete)
                    {
                        await service.DeletePostFileAsync(fileToDelete.No, _post.Category);
                        Debug.WriteLine($"파일 삭제: {fileToDelete.FileName}");
                    }

                    // 3. 새 파일 저장
                    foreach (var fileBox in FileListBox.FileBoxes)
                    {
                        // OrgFilePath가 있으면 새로 추가된 파일
                        if (!string.IsNullOrEmpty(fileBox.OrgFilePath) && fileBox.PostFile != null)
                        {
                            var savedFile = await SaveFileAsync(
                                fileBox.OrgFilePath,
                                postNo,
                                _post.Category);

                            if (savedFile != null)
                            {
                                await service.AddPostFileAsync(savedFile);
                                Debug.WriteLine($"파일 저장: {savedFile.FileName}");
                            }
                        }
                    }

                    // 4. 목록으로 돌아가기 (원래 설정 유지)
                    Debug.WriteLine($"Frame.CanGoBack={Frame.CanGoBack}, BackStackDepth={Frame.BackStackDepth}");
                    if (Frame.CanGoBack)
                    {
                        Frame.GoBack();
                    }
                    else
                    {
                        // BackStack이 없는 경우 (내장 Frame 등)
                        Frame.Navigate(typeof(PostListPage), _parameter != null
                            ? new PostListPageParameter
                            {
                                Category = _parameter.DefaultCategory ?? string.Empty,
                                Subject = _parameter.DefaultSubject ?? string.Empty,
                                AllowCategoryChange = _parameter.AllowCategoryChange,
                                ShowSubjectFilter = !string.IsNullOrEmpty(_parameter.DefaultCategory),
                                Title = ""
                            }
                            : null);
                    }


                    Debug.WriteLine($"Post 저장 완료 및 목록으로 이동: ID={postNo}");
                }
                else
                {
                    await ShowErrorAsync("저장에 실패했습니다.");
                }
            }
        }
        catch (Exception ex)
        {
            await ShowErrorAsync($"오류가 발생했습니다: {ex.Message}");
            Debug.WriteLine($"SaveButton_Click 오류: {ex.Message}");
            Debug.WriteLine($"StackTrace: {ex.StackTrace}");
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// 파일 저장
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

            Debug.WriteLine($"파일 저장 완료: {destinationPath}");
            return postFile;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"파일 저장 실패: {ex.Message}");
            return null;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Frame.GoBack();
    }

    private bool ValidateInput()
    {
        if (string.IsNullOrWhiteSpace(TitleTextBox.Text))
        {
            _ = ShowErrorAsync("제목을 입력하세요.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(ContentEditor.Text))
        {
            _ = ShowErrorAsync("내용을 입력하세요.");
            return false;
        }

        // 카테고리 고정이 아닌 경우에만 선택 검증
        if (CategoryComboBox.SelectedItem == null && (_parameter == null || _parameter.AllowCategoryChange))
        {
            _ = ShowErrorAsync("카테고리를 선택하세요.");
            return false;
        }

        return true;
    }

    private async Task ShowErrorAsync(string message)
    {
        await MessageBox.ShowErrorAsync(message);
    }
}

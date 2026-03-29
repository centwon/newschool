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
using NewSchool.Dialogs;
using Windows.Storage;

namespace NewSchool.Board.Pages;

public sealed partial class PostEditPage : Page
{
    private Post? _post;
    private bool _isEditMode;
    private PostEditPageParameter? _parameter;
    private List<string> _allSubjects = new();
    private List<string> _allCategories = new();
    private string _originalCategory = string.Empty; // 수정 모드에서 카테고리 변경 감지용

    // 기본 카테고리 목록
    private static readonly List<string> _defaultCategories = new()
    {
        "업무", "수업", "학급", "동아리", "개인", "기타"
    };

    // 카테고리별 기본 제안 토픽
    private static readonly Dictionary<string, List<string>> _defaultTopics = new()
    {
        ["학급"] = new() { "통계", "학급 자료", "학생 자료", "학급 안내" },
        ["수업"] = new() { "통계", "수업 자료", "과제" },
        ["동아리"] = new() { "통계", "동아리 자료", "활동 안내" },
    };

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

        // 카테고리 목록 먼저 로드
        await LoadCategoriesAsync();

        if (postNo > 0)
        {
            // 수정 모드
            _isEditMode = true;

            using var service = Board.CreateService();
            _post = await service.GetPostAsync(postNo, incrementReadCount: false);

            if (_post != null)
            {
                _originalCategory = _post.Category;
                TitleTextBox.Text = _post.Title;
                ContentEditor.Text = _post.Content;

                // 카테고리 선택
                if (!string.IsNullOrEmpty(_post.Category))
                {
                    var catIdx = _allCategories.IndexOf(_post.Category);
                    if (catIdx >= 0)
                    {
                        CategoryComboBox.SelectedIndex = catIdx;
                    }
                    else
                    {
                        CategoryComboBox.Text = _post.Category;
                    }
                }

                await LoadSubjectsAsync(_post.Category);
                // 주제 목록에서 매칭되는 항목 선택, 없으면 텍스트 직접 설정
                if (!string.IsNullOrEmpty(_post.Subject))
                {
                    var idx = _allSubjects.IndexOf(_post.Subject);
                    if (idx >= 0)
                    {
                        SubjectComboBox.SelectedIndex = idx;
                    }
                    else
                    {
                        SubjectComboBox.Text = _post.Subject;
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
                    var catIdx = _allCategories.IndexOf(_parameter.DefaultCategory);
                    if (catIdx >= 0)
                    {
                        CategoryComboBox.SelectedIndex = catIdx;
                    }
                    else
                    {
                        CategoryComboBox.Text = _parameter.DefaultCategory;
                    }
                    await LoadSubjectsAsync(_parameter.DefaultCategory);
                }

                if (!string.IsNullOrEmpty(_parameter.DefaultSubject))
                {
                    _post.Subject = _parameter.DefaultSubject;
                    SubjectComboBox.Text = _parameter.DefaultSubject;
                }
            }
        }

        // 카테고리 ComboBox 활성화 여부 (고정 모드에서도 표시는 유지)
        if (_parameter != null)
        {
            CategoryComboBox.IsEnabled = _parameter.AllowCategoryChange;
        }

        PageTitle.Text = _isEditMode ? "게시글 수정" : "새 글 쓰기";
    }

    private async Task LoadCategoriesAsync()
    {
        try
        {
            using var service = Board.CreateService();
            var categories = await service.GetCategoriesAsync();
            _allCategories = categories.Where(c => !string.IsNullOrEmpty(c)).ToList();

            // 기본 카테고리 병합 (중복 제거)
            foreach (var cat in _defaultCategories)
            {
                if (!_allCategories.Contains(cat))
                    _allCategories.Add(cat);
            }

            CategoryComboBox.ItemsSource = _allCategories;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"카테고리 목록 로드 실패: {ex.Message}");
            _allCategories = new List<string>(_defaultCategories);
            CategoryComboBox.ItemsSource = _allCategories;
        }
    }

    /// <summary>
    /// 카테고리 변경 시 FileListBox의 Category도 업데이트
    /// </summary>
    private async void CategoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CategoryComboBox.SelectedItem is string selected)
        {
            if (_post != null)
            {
                _post.Category = selected;
            }
            FileListBox.Category = selected;
            await LoadSubjectsAsync(selected);
        }
    }

    private void CategoryComboBox_TextSubmitted(ComboBox sender, ComboBoxTextSubmittedEventArgs args)
    {
        args.Handled = true;
        var text = args.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(text))
            return;

        // 목록에 있으면 해당 항목 선택
        var idx = _allCategories.IndexOf(text);
        if (idx >= 0)
        {
            sender.SelectedIndex = idx;
        }
        else
        {
            // 목록에 없으면 새 항목 추가 후 선택
            _allCategories.Add(text);
            sender.ItemsSource = null;
            sender.ItemsSource = _allCategories;
            sender.SelectedIndex = _allCategories.Count - 1;
        }
        // _post.Category는 SelectionChanged에서 자동 반영됨
    }

    private async Task LoadSubjectsAsync(string category)
    {
        try
        {
            using var service = Board.CreateService();
            var subjects = await service.GetSubjectsAsync(category);
            _allSubjects = subjects.Where(s => !string.IsNullOrEmpty(s)).ToList();

            // 기본 제안 토픽 병합 (중복 제거)
            if (_defaultTopics.TryGetValue(category, out var defaults))
            {
                foreach (var topic in defaults)
                {
                    if (!_allSubjects.Contains(topic))
                        _allSubjects.Insert(0, topic);
                }
            }

            SubjectComboBox.ItemsSource = _allSubjects;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"주제 목록 로드 실패: {ex.Message}");
            _allSubjects.Clear();
        }
    }

    private void SubjectComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_post != null && SubjectComboBox.SelectedItem is string selected)
        {
            _post.Subject = selected;
        }
    }

    private void SubjectComboBox_TextSubmitted(ComboBox sender, ComboBoxTextSubmittedEventArgs args)
    {
        args.Handled = true;
        var text = args.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(text))
            return;

        // 목록에 있으면 해당 항목 선택
        var idx = _allSubjects.IndexOf(text);
        if (idx >= 0)
        {
            sender.SelectedIndex = idx;
        }
        else
        {
            // 목록에 없으면 새 항목 추가 후 선택
            _allSubjects.Add(text);
            sender.ItemsSource = null;
            sender.ItemsSource = _allSubjects;
            sender.SelectedIndex = _allSubjects.Count - 1;
        }
        // _post.Subject는 SelectionChanged에서 자동 반영됨
    }

    private async void InsertRosterButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new RosterTableDialog { XamlRoot = this.XamlRoot };

        // 카테고리에 따라 기본 스코프 설정
        var category = CategoryComboBox.SelectedItem as string ?? CategoryComboBox.Text?.Trim() ?? "";
        switch (category)
        {
            case "수업":
                dialog.SetScope("Course");
                break;
            case "동아리":
                dialog.SetScope("Club");
                break;
            default:
                dialog.SetScope("Class");
                break;
        }

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && !string.IsNullOrEmpty(dialog.GeneratedHtml))
        {
            // JoditEditor에 HTML 테이블 삽입
            var escaped = dialog.GeneratedHtml
                .Replace("\\", "\\\\")
                .Replace("'", "\\'")
                .Replace("\n", "\\n")
                .Replace("\r", "");
            await ContentEditor.ExecuteScriptAsync($"editor.selection.insertHTML('{escaped}');");

            // 제목이 비어있으면 표 제목으로 자동 채움
            if (string.IsNullOrWhiteSpace(TitleTextBox.Text) && !string.IsNullOrEmpty(dialog.TableTitle))
            {
                TitleTextBox.Text = dialog.TableTitle;
            }
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
                _post.Subject = SubjectComboBox.Text.Trim();

                if (CategoryComboBox.SelectedItem is string selectedCategory)
                {
                    _post.Category = selectedCategory;
                }
                else if (!string.IsNullOrWhiteSpace(CategoryComboBox.Text))
                {
                    _post.Category = CategoryComboBox.Text.Trim();
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
                    // 1.5. 카테고리 변경 시 기존 첨부파일 이동
                    if (_isEditMode && !string.IsNullOrEmpty(_originalCategory) && _originalCategory != _post.Category)
                    {
                        await MoveExistingFilesAsync(_originalCategory, _post.Category, postNo, service);
                    }

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
    /// 카테고리 변경 시 기존 첨부파일을 새 카테고리 폴더로 이동
    /// </summary>
    private async Task MoveExistingFilesAsync(string oldCategory, string newCategory, int postNo, BoardService service)
    {
        try
        {
            Board.EnsureCategoryDirectory(newCategory);

            var existingFiles = await service.GetPostFilesByPostAsync(postNo);
            foreach (var file in existingFiles)
            {
                var oldPath = Board.GetFilePath(file.FileName, oldCategory);
                var newPath = Board.GetFilePath(file.FileName, newCategory);

                if (File.Exists(oldPath) && !File.Exists(newPath))
                {
                    File.Move(oldPath, newPath);
                    Debug.WriteLine($"첨부파일 이동: {oldPath} → {newPath}");
                }
            }

            // 댓글 첨부파일도 이동
            var comments = await service.GetCommentsByPostAsync(postNo);
            foreach (var comment in comments.Where(c => c.HasFile && !string.IsNullOrEmpty(c.FileName)))
            {
                var oldPath = Board.GetFilePath(comment.FileName, oldCategory);
                var newPath = Board.GetFilePath(comment.FileName, newCategory);

                if (File.Exists(oldPath) && !File.Exists(newPath))
                {
                    File.Move(oldPath, newPath);
                    Debug.WriteLine($"댓글 파일 이동: {oldPath} → {newPath}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"첨부파일 이동 실패: {ex.Message}");
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

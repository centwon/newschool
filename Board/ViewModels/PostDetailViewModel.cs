using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.UI.Xaml;
using NewSchool.Board.Pages;
using NewSchool.Board.Services;
using NewSchool.Collections;
using Windows.Storage;

namespace NewSchool.Board.ViewModels;
/// <summary>
/// Post 상세 ViewModel
/// </summary>
public class PostDetailViewModel : INotifyPropertyChanged
{
    private readonly BoardService _service;
    private Post? _post;
    private OptimizedObservableCollection<Comment> _comments;
    private OptimizedObservableCollection<PostFile> _files;
    private bool _isLoading;
    private string _newCommentContent = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    #region Properties

    public Post? Post
    {
        get => _post;
        set
        {
            _post = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SubjectVisibility));
        }
    }

    public Visibility SubjectVisibility =>
        Post != null && !string.IsNullOrEmpty(Post.Subject)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public OptimizedObservableCollection<Comment> Comments
    {
        get => _comments;
        set
        {
            _comments = value;
            OnPropertyChanged();
        }
    }

    public OptimizedObservableCollection<PostFile> Files
    {
        get => _files;
        set
        {
            _files = value;
            OnPropertyChanged();
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            _isLoading = value;
            OnPropertyChanged();
        }
    }

    public string NewCommentContent
    {
        get => _newCommentContent;
        set
        {
            _newCommentContent = value;
            OnPropertyChanged();
        }
    }

    private Comment? _editingComment;
    public Comment? EditingComment
    {
        get => _editingComment;
        private set
        {
            _editingComment = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsEditing));
        }
    }

    public bool IsEditing => EditingComment != null;

    #endregion

    #region Commands

    public ICommand LoadPostCommand { get; }
    public ICommand AddCommentCommand { get; }
    public ICommand DeleteCommentCommand { get; }

    #endregion

    public PostDetailViewModel()
    {
        _service = Board.CreateService();
        _comments = new OptimizedObservableCollection<Comment>();
        _files = new OptimizedObservableCollection<PostFile>();

        LoadPostCommand = new RelayCommand<int>(async (postNo) => await LoadPostAsync(postNo));
        AddCommentCommand = new RelayCommand(async () => await AddCommentAsync());
        DeleteCommentCommand = new RelayCommand<Comment>(async (comment) => await DeleteCommentAsync(comment));
    }

    #region Methods

    public async Task LoadPostAsync(int postNo)
    {
        try
        {
            IsLoading = true;

            // Post 조회 (조회수 증가)
            Post = await _service.GetPostAsync(postNo, incrementReadCount: true);

            if (Post != null)
            {
                // 댓글 로드
                await LoadCommentsAsync(postNo);

                // 파일 로드
                await LoadFilesAsync(postNo);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Post 로드 실패: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadCommentsAsync(int postNo)
    {
        try
        {
            var comments = await _service.GetCommentsByPostAsync(postNo);

            Comments.ReplaceAll(comments);

            Debug.WriteLine($"댓글 로드 완료: {Comments.Count}개");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"댓글 로드 실패: {ex.Message}");
        }
    }

    private async Task LoadFilesAsync(int postNo)
    {
        try
        {
            var files = await _service.GetPostFilesByPostAsync(postNo);

            Files.ReplaceAll(files);

            Debug.WriteLine($"파일 로드 완료: {Files.Count}개");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"파일 로드 실패: {ex.Message}");
        }
    }

    public async Task AddCommentAsync()
    {
        if (Post == null || string.IsNullOrWhiteSpace(NewCommentContent))
            return;

        try
        {
            var comment = new Comment
            {
                Post = Post.No,
                User = "익명", // TODO: 실제 로그인 사용자명으로 변경
                Content = NewCommentContent,
                DateTime = DateTime.Now,
                ReplyOrder = 0,
                HasFile = false,
                FileName = "",
                FileSize = 0
            };

            int commentId = await _service.CreateCommentAsync(comment);

            if (commentId > 0)
            {
                comment.No = commentId;
                Comments.Insert(0, comment); // 최신 댓글을 맨 위에 추가
                NewCommentContent = "";

                Debug.WriteLine($"댓글 추가 완료: ID={commentId}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"댓글 추가 실패: {ex.Message}");
        }
    }
    public async Task DeleteCommentAsync(Comment? comment)
    {
        if (comment == null || Post == null) return;

        try
        {
            bool success = await _service.DeleteCommentAsync(comment.No, Post.Category);

            if (success)
            {
                Comments.Remove(comment);
                Debug.WriteLine($"댓글 삭제 완료: No={comment.No}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"댓글 삭제 실패: {ex.Message}");
        }
    }

    public void StartEdit(Comment comment)
    {
        EditingComment = comment;
        NewCommentContent = comment.Content;
    }

    public async Task UpdateCommentAsync()
    {
        if (EditingComment == null || string.IsNullOrWhiteSpace(NewCommentContent))
            return;

        try
        {
            EditingComment.Content = NewCommentContent;
            bool success = await _service.UpdateCommentAsync(EditingComment);

            if (success)
            {
                Debug.WriteLine($"댓글 수정 완료: No={EditingComment.No}");
                // ObservableCollection 내의 객체를 직접 수정했으므로 UI 갱신을 위해 목록 다시 로드
                if (Post != null)
                    await LoadCommentsAsync(Post.No);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"댓글 수정 실패: {ex.Message}");
        }
        finally
        {
            CancelEdit();
        }
    }

    public void CancelEdit()
    {
        EditingComment = null;
        NewCommentContent = "";
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    public async Task AddCommentAsync(StorageFile? attachedFile = null)
    {
        if (Post == null || string.IsNullOrWhiteSpace(NewCommentContent))
            return;

        try
        {
            var comment = new Comment
            {
                Post = Post.No,
                User = Settings.UserName ?? "익명",
                Content = NewCommentContent,
                DateTime = DateTime.Now,
                ReplyOrder = 0,
                HasFile = attachedFile != null,
                FileName = "",
                FileSize = 0
            };

            // 파일이 있으면 저장
            if (attachedFile != null)
            {
                var savedFileName = await SaveCommentFileAsync(attachedFile, Post.Category);
                if (!string.IsNullOrEmpty(savedFileName))
                {
                    comment.FileName = savedFileName;
                    var properties = await attachedFile.GetBasicPropertiesAsync();
                    comment.FileSize = (int)properties.Size;
                }
                else
                {
                    comment.HasFile = false;
                }
            }

            int commentId = await _service.CreateCommentAsync(comment);

            if (commentId > 0)
            {
                comment.No = commentId;
                Comments.Insert(0, comment);
                NewCommentContent = "";

                Debug.WriteLine($"댓글 추가 완료: ID={commentId}, 파일={comment.HasFile}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"댓글 추가 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 댓글 파일 저장
    /// </summary>
    private async Task<string> SaveCommentFileAsync(StorageFile file, string category)
    {
        try
        {
            Board.EnsureCategoryDirectory(category);

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var extension = Path.GetExtension(file.Name);
            var fileNameWithoutExt = Path.GetFileNameWithoutExtension(file.Name);
            var fileName = $"comment_{timestamp}_{fileNameWithoutExt}{extension}";

            var destinationPath = Board.GetFilePath(fileName, category);
            var destinationFolder = await StorageFolder.GetFolderFromPathAsync(
                Path.GetDirectoryName(destinationPath));

            await file.CopyAsync(destinationFolder, fileName, NameCollisionOption.ReplaceExisting);

            Debug.WriteLine($"댓글 파일 저장 완료: {destinationPath}");
            return fileName;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"댓글 파일 저장 실패: {ex.Message}");
            return string.Empty;
        }
    }
    #endregion
}


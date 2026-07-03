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
            if (_post != null) _post.PropertyChanged -= Post_PropertyChanged;
            _post = value;
            if (_post != null) _post.PropertyChanged += Post_PropertyChanged;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SubjectVisibility));
            OnPropertyChanged(nameof(IsMemoVisibility));
        }
    }

    private void Post_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Post.IsCompleted))
            OnPropertyChanged(nameof(IsCompleted));
    }

    public Visibility SubjectVisibility =>
        Post != null && !string.IsNullOrEmpty(Post.Subject)
            ? Visibility.Visible
            : Visibility.Collapsed;

    /// <summary>메모(Subject="메모")일 때만 완료(읽음 처리) 토글을 노출.</summary>
    public Visibility IsMemoVisibility =>
        Post != null && Post.Subject == "메모"
            ? Visibility.Visible
            : Visibility.Collapsed;

    public bool IsCompleted
    {
        get => Post?.IsCompleted ?? false;
        set
        {
            if (Post == null || Post.IsCompleted == value) return;
            Post.IsCompleted = value;
            _ = _service.UpdatePostIsCompletedAsync(Post.No, value);
        }
    }

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

    private Comment? _replyTargetComment;
    /// <summary>답글 대상 댓글 (null이면 최상위 댓글로 작성)</summary>
    public Comment? ReplyTargetComment
    {
        get => _replyTargetComment;
        private set
        {
            _replyTargetComment = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsReplying));
        }
    }

    public bool IsReplying => ReplyTargetComment != null;

    #endregion

    #region Commands

    public ICommand LoadPostCommand { get; }
    public ICommand AddCommentCommand { get; }
    public ICommand DeleteCommentCommand { get; }

    #endregion

    public PostDetailViewModel()
    {
        _service = Board.CreateCachedService();
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

            Comments.ReplaceAll(BuildThreadedOrder(comments));

            Debug.WriteLine($"댓글 로드 완료: {Comments.Count}개");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"댓글 로드 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 최상위 댓글은 기존 순서를 유지하고, 각 댓글의 답글은 바로 아래에 시간순으로 묶어서 배치한다.
    /// (1단계 대댓글만 지원 — 답글에 대한 답글은 원 댓글의 답글로 평탄화됨)
    /// </summary>
    private static List<Comment> BuildThreadedOrder(List<Comment> comments)
    {
        var topLevel = comments.Where(c => c.ParentNo == 0).ToList();
        var repliesByParent = comments
            .Where(c => c.ParentNo != 0)
            .GroupBy(c => c.ParentNo)
            .ToDictionary(g => g.Key, g => g.OrderBy(c => c.DateTime).ToList());

        var ordered = new List<Comment>(comments.Count);
        foreach (var parent in topLevel)
        {
            ordered.Add(parent);
            if (repliesByParent.TryGetValue(parent.No, out var replies))
            {
                ordered.AddRange(replies);
            }
        }

        return ordered;
    }

    /// <summary>
    /// 답글 작성 시작 — 답글의 답글은 원 댓글로 평탄화(1단계 대댓글만 지원)
    /// </summary>
    public void StartReply(Comment comment)
    {
        ReplyTargetComment = comment;
    }

    public void CancelReply()
    {
        ReplyTargetComment = null;
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

    /// <summary>
    /// Post 삭제 (캐시 서비스 사용 — 목록 화면 캐시가 함께 무효화됨)
    /// </summary>
    public async Task<bool> DeletePostAsync()
    {
        if (Post == null) return false;

        try
        {
            return await _service.DeletePostAsync(Post.No, Post.Category);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Post 삭제 실패: {ex.Message}");
            throw;
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
            // 답글의 답글은 1단계로 평탄화 (원 댓글을 부모로 설정)
            int parentNo = ReplyTargetComment == null
                ? 0
                : ReplyTargetComment.ParentNo != 0 ? ReplyTargetComment.ParentNo : ReplyTargetComment.No;

            var comment = new Comment
            {
                Post = Post.No,
                User = Settings.UserName ?? "익명",
                Content = NewCommentContent,
                DateTime = DateTime.Now,
                ParentNo = parentNo,
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
                NewCommentContent = "";
                CancelReply();

                // 답글은 부모 댓글 바로 아래에 위치해야 하므로 전체 목록을 다시 정렬해서 로드
                await LoadCommentsAsync(Post.No);

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


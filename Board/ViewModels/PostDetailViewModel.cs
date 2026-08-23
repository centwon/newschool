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
            // 완료 토글의 DB 반영은 fire-and-forget 이지만, 실패하면 UI 만 바뀌고 DB 는 안 바뀐
            // 채로 예외가 관측되지 않는다 → 실패 시 플래그를 원복하고 사용자에게 알린다.
            _ = PersistIsCompletedAsync(Post, value);
        }
    }

    private async Task PersistIsCompletedAsync(Post post, bool value)
    {
        try
        {
            bool ok = await _service.UpdatePostIsCompletedAsync(post.No, value);
            if (!ok)
                throw new InvalidOperationException("완료 상태를 저장하지 못했습니다(대상 없음).");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"완료 상태 저장 실패: {ex.Message}");
            post.IsCompleted = !value;              // UI 원복
            OnPropertyChanged(nameof(IsCompleted));
            await NewSchool.Controls.UserErrorReporter.ReportAsync("완료 상태 변경", ex);
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

    // ICommand 셋(LoadPost·AddComment·DeleteComment)은 지웠다(39차). 어느 XAML 도 Command 로
    // 묶지 않았고, 화면은 버튼 Click 핸들러에서 아래 메서드를 직접 부른다.
    // 구현체 RelayCommand·RelayCommand<T> 도 이 셋과 PostListViewModel 쪽 다섯이 유일한
    // 사용처여서 함께 사라졌다.

    public PostDetailViewModel()
    {
        _service = Board.CreateCachedService();
        _comments = new OptimizedObservableCollection<Comment>();
        _files = new OptimizedObservableCollection<PostFile>();
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
        var topLevelNos = new HashSet<int>(comments.Where(c => c.ParentNo == 0).Select(c => c.No));
        var repliesByParent = comments
            .Where(c => c.ParentNo != 0)
            .GroupBy(c => c.ParentNo)
            .ToDictionary(g => g.Key, g => g.OrderBy(c => c.DateTime).ToList());

        var ordered = new List<Comment>(comments.Count);
        foreach (var parent in comments.Where(c => c.ParentNo == 0))
        {
            ordered.Add(parent);
            if (repliesByParent.TryGetValue(parent.No, out var replies))
            {
                ordered.AddRange(replies);
            }
        }

        // 부모가 이미 삭제된 고아 답글(ParentNo 가 현존 최상위 댓글이 아님)은 위 루프에서 누락돼
        // 화면에서 사라진다. 예전 삭제로 이미 생긴 고아를 잃지 않도록 최상위로 승격해 표시한다.
        foreach (var reply in comments.Where(c => c.ParentNo != 0 && !topLevelNos.Contains(c.ParentNo))
                                      .OrderBy(c => c.DateTime))
        {
            ordered.Add(reply);
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
                // 최상위 댓글 삭제는 답글까지 함께 지우므로(BoardService 참고), 단건 제거가 아니라
                // 전체를 다시 읽어 화면을 DB 와 맞춘다.
                await LoadCommentsAsync(Post.No);
                Debug.WriteLine($"댓글 삭제 완료: No={comment.No}");
            }
            else
            {
                // 삭제 0행 — 목록에 그대로 남는데 사용자는 삭제됐다고 오인할 수 있어 알린다.
                await NewSchool.Controls.UserErrorReporter.ReportAsync(
                    "댓글 삭제",
                    new InvalidOperationException("이미 삭제되었거나 대상을 찾을 수 없습니다."));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"댓글 삭제 실패: {ex.Message}");
            await NewSchool.Controls.UserErrorReporter.ReportAsync("댓글 삭제", ex);
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
            else
            {
                // 0행 반영 — 수정이 저장되지 않았음을 알린다(무음 실패 방지).
                await NewSchool.Controls.UserErrorReporter.ReportAsync(
                    "댓글 수정",
                    new InvalidOperationException("수정 내용을 저장하지 못했습니다(대상 없음)."));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"댓글 수정 실패: {ex.Message}");
            await NewSchool.Controls.UserErrorReporter.ReportAsync("댓글 수정", ex);
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
            string? savedFileName = null;
            if (attachedFile != null)
            {
                savedFileName = await SaveCommentFileAsync(attachedFile, Post.Category);
                if (!string.IsNullOrEmpty(savedFileName))
                {
                    comment.FileName = savedFileName;
                    var properties = await attachedFile.GetBasicPropertiesAsync();
                    comment.FileSize = (int)properties.Size;
                }
                else
                {
                    // 첨부 저장 실패 — 파일 없이 조용히 등록되면 사용자가 첨부된 줄 오인한다.
                    comment.HasFile = false;
                    await NewSchool.Controls.UserErrorReporter.ReportAsync(
                        "첨부파일 저장",
                        new InvalidOperationException("첨부파일을 저장하지 못해 파일 없이 댓글만 등록합니다."));
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
            else
            {
                // 댓글 생성 실패 — 방금 저장한 첨부 물리 파일이 고아로 남지 않도록 정리 후 알린다.
                if (!string.IsNullOrEmpty(savedFileName))
                    DeleteCommentFileQuietly(savedFileName, Post.Category);
                await NewSchool.Controls.UserErrorReporter.ReportAsync(
                    "댓글 등록",
                    new InvalidOperationException("댓글을 저장하지 못했습니다."));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"댓글 추가 실패: {ex.Message}");
            await NewSchool.Controls.UserErrorReporter.ReportAsync("댓글 등록", ex);
        }
    }

    /// <summary>고아가 된 댓글 첨부 물리 파일을 조용히 삭제 (실패해도 무시).</summary>
    private static void DeleteCommentFileQuietly(string fileName, string category)
    {
        try
        {
            var path = Board.GetFilePath(fileName, category);
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"고아 첨부 파일 정리 실패: {ex.Message}");
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

            // 저장할 이름(희망값). 타임스탬프는 초 단위라 이것만으로는 유일하지 않다.
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var extension = Path.GetExtension(file.Name);
            var fileNameWithoutExt = Path.GetFileNameWithoutExtension(file.Name);
            var desiredName = $"comment_{timestamp}_{fileNameWithoutExt}{extension}";

            var destinationFolder = await StorageFolder.GetFolderFromPathAsync(
                Path.GetDirectoryName(Board.GetFilePath(desiredName, category)));

            // ⚠ ReplaceExisting 금지 — 같은 초에 저장되는 동명 첨부가 서로를 조용히 덮어썼다.
            // 충돌 해소는 OS 에 맡기고, 실제로 저장된 이름을 반환한다. (PostEditPage 와 동일)
            var savedFile = await file.CopyAsync(
                destinationFolder, desiredName, NameCollisionOption.GenerateUniqueName);

            Debug.WriteLine($"댓글 파일 저장 완료: {savedFile.Path}");
            return savedFile.Name;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"댓글 파일 저장 실패: {ex.Message}");
            return string.Empty;
        }
    }
    #endregion
}


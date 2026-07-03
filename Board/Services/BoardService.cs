using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NewSchool.Board.Repositories;

namespace NewSchool.Board.Services;

/// <summary>
/// Board 서비스 - 고수준 비즈니스 로직 + UnitOfWork 활용
/// </summary>
public partial class BoardService:IDisposable
{
    private readonly string _dbPath;

    public BoardService(string dbPath)
    {
        _dbPath = dbPath;

    }

    private bool _disposed;



    #region Post Operations

    /// <summary>
    /// Post 생성 또는 수정 (트랜잭션 처리)
    /// </summary>
    public virtual async Task<int> SavePostAsync(Post post)
    {
        using var uow = new UnitOfWork(_dbPath);

        try
        {
            return await uow.ExecuteInTransactionAsync(async () =>
            {
                int postId;
                if (post.No <= 0)
                {
                    // 새 Post 생성
                    postId = await uow.Posts.CreateAsync(post);
                }
                else
                {
                    // 기존 Post 수정
                    await uow.Posts.UpdateAsync(post);
                    postId = post.No;
                }

                return postId;
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Post 저장 실패: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Post 조회 (조회수 증가 포함)
    /// </summary>
    public virtual async Task<Post?> GetPostAsync(int no, bool incrementReadCount = true)
    {
        using var uow = new UnitOfWork(_dbPath);

        try
        {
            var post = await uow.Posts.GetByIdAsync(no);

            if (post != null && incrementReadCount)
            {
                await uow.Posts.IncrementReadCountAsync(no);
            }

            return post;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Post 조회 실패: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Post 목록 조회 (페이징)
    /// </summary>
    public virtual async Task<PagedResult<Post>> GetPostsPagedAsync(
        int pageNumber,
        int pageSize,
        string category = "",
        string subject = "",
        bool searchTitle = false,
        bool searchContent = false,
        string searchText = "",
        Models.PostSortOrder sortOrder = Models.PostSortOrder.NewestFirst)
    {
        using var uow = new UnitOfWork(_dbPath);

        try
        {
            int offset = (pageNumber - 1) * pageSize;

            var (posts, totalCount) = await uow.Posts.GetListWithCountAsync(
                limit: pageSize,
                offset: offset,
                category: category,
                subject: subject,
                searchTitle: searchTitle,
                searchContent: searchContent,
                searchText: searchText,
                sortOrder: sortOrder);

            return new PagedResult<Post>(posts, totalCount, pageSize, pageNumber);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Post 목록 조회 실패: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Post 삭제 (관련 파일도 함께 삭제)
    /// </summary>
    public virtual async Task<bool> DeletePostAsync(int postNo, string category)
    {
        // ✅ 각 Repository를 독립적으로 사용 (CASCADE로 DB 정합성 보장)
        try
        {
            // 1. 먼저 물리적 파일들 삭제 (데이터 조회)
            using (var commentRepo = new CommentRepository(_dbPath))
            {
                var comments = await commentRepo.GetByPostAsync(postNo);
                foreach (var comment in comments)
                {
                    if (comment.HasFile && !string.IsNullOrEmpty(comment.FileName))
                    {
                        DeletePhysicalFile(comment.FileName, category);
                    }
                }
            }

            using (var postFileRepo = new PostFileRepository(_dbPath))
            {
                var postFiles = await postFileRepo.GetByPostAsync(postNo);
                foreach (var file in postFiles)
                {
                    DeletePhysicalFile(file.FileName, category);
                }
            }

            // 2. Post 삭제 (CASCADE로 Comment, PostFile도 자동 삭제)
            using (var postRepo = new PostRepository(_dbPath))
            {
                return await postRepo.DeleteAsync(postNo);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Post 삭제 실패: {ex.Message}");
            throw;
        }
    }

    #endregion

    #region Comment Operations

    /// <summary>
    /// Comment 생성 (Post의 HasComment 플래그도 업데이트)
    /// </summary>
    public virtual async Task<int> CreateCommentAsync(Comment comment)
    {
        using var uow = new UnitOfWork(_dbPath);

        try
        {
            return await uow.ExecuteInTransactionAsync(async () =>
            {
                // 댓글 생성
                int commentId = await uow.Comments.CreateAsync(comment);

                // Post의 HasComment 플래그 업데이트
                if (commentId > 0)
                {
                    await uow.Posts.UpdateHasCommentAsync(comment.Post, true);
                }

                return commentId;
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Comment 생성 실패: {ex.Message}");
            throw;
        }
    }
    /// <summary>
    /// Comment 수정
    /// </summary>
    public virtual async Task<bool> UpdateCommentAsync(Comment comment)
    {
        using var uow = new UnitOfWork(_dbPath);

        try
        {
            return await uow.Comments.UpdateAsync(comment);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Comment 수정 실패: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Comment 삭제 (관련 파일도 삭제, Post의 HasComment 업데이트)
    /// </summary>
    public virtual async Task<bool> DeleteCommentAsync(int commentNo, string category)
    {
        using var uow = new UnitOfWork(_dbPath);
        string? fileToDelete = null;

        try
        {
            bool deleted = await uow.ExecuteInTransactionAsync(async () =>
            {
                var comment = await uow.Comments.GetByIdAsync(commentNo);
                if (comment == null)
                    return false;

                int postNo = comment.Post;

                // Comment 삭제 + 남은 댓글 확인 + Post 플래그 업데이트를 하나의 트랜잭션으로 처리
                await uow.Comments.DeleteAsync(commentNo);

                int remainingComments = await uow.Comments.GetCountByPostAsync(postNo);
                if (remainingComments == 0)
                {
                    await uow.Posts.UpdateHasCommentAsync(postNo, false);
                }

                if (comment.HasFile && !string.IsNullOrEmpty(comment.FileName))
                {
                    fileToDelete = comment.FileName;
                }

                return true;
            });

            // DB 트랜잭션 커밋이 확정된 뒤에만 물리 파일 삭제 (롤백 시 고아 파일 방지)
            if (deleted && fileToDelete != null)
            {
                DeletePhysicalFile(fileToDelete, category);
            }

            return deleted;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Comment 삭제 실패: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Post의 모든 Comment 조회
    /// </summary>
    public virtual async Task<List<Comment>> GetCommentsByPostAsync(int postNo)
    {
        using var uow = new UnitOfWork(_dbPath);

        try
        {
            return await uow.Comments.GetByPostAsync(postNo);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Comment 목록 조회 실패: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 여러 Post의 댓글 개수를 한 번의 쿼리로 일괄 조회 (목록 화면 N+1 방지).
    /// 반환 딕셔너리에 없는 Post 는 댓글 0개.
    /// </summary>
    public virtual async Task<Dictionary<int, int>> GetCommentCountsAsync(IReadOnlyList<int> postNos)
    {
        using var uow = new UnitOfWork(_dbPath);

        try
        {
            return await uow.Comments.GetCountsByPostsAsync(postNos);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Comment 개수 일괄 조회 실패: {ex.Message}");
            throw;
        }
    }

    #endregion

    #region PostFile Operations
    /// <summary>
    /// PostFile 추가 (Post의 HasFile 플래그도 업데이트)
    /// </summary>
    public virtual async Task<int> AddPostFileAsync(PostFile postFile)
    {
        using var uow = new UnitOfWork(_dbPath);

        try
        {
            return await uow.ExecuteInTransactionAsync(async () =>
            {
                // 파일 추가
                int fileId = await uow.PostFiles.CreateAsync(postFile);

                // Post의 HasFile 플래그 업데이트
                if (fileId > 0)
                {
                    await uow.Posts.UpdateHasFileAsync(postFile.Post, true);
                }

                return fileId;
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PostFile 추가 실패: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// PostFile 삭제 (물리적 파일도 삭제, Post의 HasFile 업데이트)
    /// </summary>
    public virtual async Task<bool> DeletePostFileAsync(int postFileNo, string category)
    {
        using var uow = new UnitOfWork(_dbPath);
        string? fileToDelete = null;

        try
        {
            bool deleted = await uow.ExecuteInTransactionAsync(async () =>
            {
                var postFile = await uow.PostFiles.GetByIdAsync(postFileNo);
                if (postFile == null)
                    return false;

                bool result = await uow.PostFiles.DeleteAsync(postFileNo);

                if (result)
                {
                    int remainingFiles = await uow.PostFiles.GetCountByPostAsync(postFile.Post);
                    if (remainingFiles == 0)
                    {
                        await uow.Posts.UpdateHasFileAsync(postFile.Post, false);
                    }

                    fileToDelete = postFile.FileName;
                }

                return result;
            });

            // DB 트랜잭션 커밋이 확정된 뒤에만 물리 파일 삭제 (롤백 시 고아 파일 방지)
            if (deleted && fileToDelete != null)
            {
                DeletePhysicalFile(fileToDelete, category);
            }

            return deleted;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PostFile 삭제 실패: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Post의 모든 파일 조회
    /// </summary>
    public virtual async Task<List<PostFile>> GetPostFilesByPostAsync(int postNo)
    {
        using var postFileRepo = new PostFileRepository(_dbPath);

        try
        {
            return await postFileRepo.GetByPostAsync(postNo);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PostFile 목록 조회 실패: {ex.Message}");
            throw;
        }
    }

    #endregion

    #region Memo Operations

    /// <summary>
    /// Post의 IsCompleted 플래그 업데이트
    /// </summary>
    public async Task<bool> UpdatePostIsCompletedAsync(int postNo, bool isCompleted)
    {
        using var postRepo = new PostRepository(_dbPath);

        try
        {
            return await postRepo.UpdateIsCompletedAsync(postNo, isCompleted);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"IsCompleted 업데이트 실패: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Post의 HasFile 플래그 업데이트
    /// </summary>
    public async Task<bool> UpdatePostHasFileAsync(int postNo, bool hasFile)
    {
        using var postRepo = new PostRepository(_dbPath);

        try
        {
            return await postRepo.UpdateHasFileAsync(postNo, hasFile);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"HasFile 업데이트 실패: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 메모 목록 조회 (카테고리 필터 지원)
    /// category가 비어있으면 전체 조회
    /// </summary>
    public async Task<List<Post>> GetMemosAsync(
        string category = "",
        string subject = "",
        DateTime? startDate = null,
        DateTime? endDate = null,
        bool includeCompleted = true)
    {
        using var uow = new UnitOfWork(_dbPath);

        try
        {
            return await uow.Posts.GetListAsync(
                category: category,
                subject: subject,
                startDate: startDate,
                endDate: endDate,
                includeCompleted: includeCompleted);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"메모 목록 조회 실패: {ex.Message}");
            throw;
        }
    }

    #endregion

    #region Utility Methods

    /// <summary>
    /// 카테고리 목록 조회
    /// </summary>
    public virtual async Task<List<string>> GetCategoriesAsync()
    {
        using var uow = new UnitOfWork(_dbPath);
        return await uow.Posts.GetCategoriesAsync();
    }

    /// <summary>
    /// 주제 목록 조회
    /// </summary>
    public virtual async Task<List<string>> GetSubjectsAsync(string category = "")
    {
        using var uow = new UnitOfWork(_dbPath);
        return await uow.Posts.GetSubjectsAsync(category);
    }

    /// <summary>
    /// 물리적 파일 삭제
    /// </summary>
    private void DeletePhysicalFile(string fileName, string category)
    {
        try
        {
            string filePath = Path.Combine(Board.Data_Dir, category, fileName);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                System.Diagnostics.Debug.WriteLine($"파일 삭제 완료: {filePath}");
            }
        }
        catch (Exception ex)
        {
            // DB 레코드는 이미 삭제되어 되돌릴 수 없으므로 예외를 던지지 않고 계속 진행하되,
            // 릴리스 빌드에서도 확인 가능하도록 파일 로그에 남긴다 (고아 파일 추적용).
            NewSchool.Logging.Log.Warning("BoardService", $"물리 파일 삭제 실패 (DB 레코드는 이미 삭제됨): {fileName}, {ex.Message}");
        }
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // BoardService는 상태를 유지하지 않으므로
                // 특별히 해제할 리소스 없음
                // UnitOfWork는 각 메서드에서 using으로 관리됨
            }
            _disposed = true;
        }
    }

    #endregion

}

/// <summary>
/// 페이징 결과
/// </summary>
public record PagedResult<T>(List<T> Items, int TotalCount, int PageSize, int PageNumber)
{
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
}

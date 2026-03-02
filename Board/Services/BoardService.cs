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
    public async Task<int> SavePostAsync(Post post)
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
    public async Task<Post?> GetPostAsync(int no, bool incrementReadCount = true)
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
    public async Task<PagedResult<Post>> GetPostsPagedAsync(
        int pageNumber,
        int pageSize,
        string category = "",
        string subject = "",
        bool searchTitle = false,
        bool searchContent = false,
        string searchText = "")
    {
        using var uow = new UnitOfWork(_dbPath);

        try
        {
            int offset = (pageNumber - 1) * pageSize;

            var posts = await uow.Posts.GetListAsync(
                limit: pageSize,
                offset: offset,
                category: category,
                subject: subject,
                searchTitle: searchTitle,
                searchContent: searchContent,
                searchText: searchText);

            var totalCount = await uow.Posts.GetCountAsync(
                category: category,
                subject: subject,
                searchTitle: searchTitle,
                searchContent: searchContent,
                searchText: searchText);

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
    public async Task<bool> DeletePostAsync(int postNo, string category)
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
    public async Task<int> CreateCommentAsync(Comment comment)
    {
        // UnitOfWork 대신 직접 Repository 사용
        using var commentRepo = new CommentRepository(_dbPath);
        using var postRepo = new PostRepository(_dbPath);

        try
        {
            // 댓글 생성
            int commentId = await commentRepo.CreateAsync(comment);

            // Post의 HasComment 플래그 업데이트
            if (commentId > 0)
            {
                await postRepo.UpdateHasCommentAsync(comment.Post, true);
            }

            return commentId;
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
    public async Task<bool> UpdateCommentAsync(Comment comment)
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
    public async Task<bool> DeleteCommentAsync(int commentNo, string category)
    {
        // ✅ 각 Repository를 독립적으로 사용
        try
        {
            Comment? comment;
            int postNo;

            // 1. 댓글 정보 조회
            using (var commentRepo = new CommentRepository(_dbPath))
            {
                comment = await commentRepo.GetByIdAsync(commentNo);
                if (comment == null)
                    return false;

                postNo = comment.Post;

                // 파일 삭제
                if (comment.HasFile && !string.IsNullOrEmpty(comment.FileName))
                {
                    DeletePhysicalFile(comment.FileName, category);
                }

                // Comment 삭제
                await commentRepo.DeleteAsync(commentNo);
            }

            // 2. 남은 댓글 확인 및 Post 플래그 업데이트
            using (var commentRepo = new CommentRepository(_dbPath))
            using (var postRepo = new PostRepository(_dbPath))
            {
                int remainingComments = await commentRepo.GetCountByPostAsync(postNo);
                if (remainingComments == 0)
                {
                    await postRepo.UpdateHasCommentAsync(postNo, false);
                }
            }

            return true;
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
    public async Task<List<Comment>> GetCommentsByPostAsync(int postNo)
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

    #endregion

    #region PostFile Operations
    /// <summary>
    /// PostFile 추가 (Post의 HasFile 플래그도 업데이트)
    /// </summary>
    public async Task<int> AddPostFileAsync(PostFile postFile)
    {
        // UnitOfWork 대신 직접 Repository 사용
        using var postFileRepo = new PostFileRepository(_dbPath);
        using var postRepo = new PostRepository(_dbPath);

        try
        {
            // 파일 추가
            int fileId = await postFileRepo.CreateAsync(postFile);

            // Post의 HasFile 플래그 업데이트
            if (fileId > 0)
            {
                await postRepo.UpdateHasFileAsync(postFile.Post, true);
            }

            return fileId;
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
    public async Task<bool> DeletePostFileAsync(int postFileNo, string category)
    {
        using var postFileRepo = new PostFileRepository(_dbPath);
        using var postRepo = new PostRepository(_dbPath);

        try
        {
            var postFile = await postFileRepo.GetByIdAsync(postFileNo);
            if (postFile == null)
                return false;

            // 물리적 파일 삭제
            DeletePhysicalFile(postFile.FileName, category);

            // DB에서 삭제
            bool deleted = await postFileRepo.DeleteAsync(postFileNo);

            // 남은 파일 확인
            if (deleted)
            {
                int remainingFiles = await postFileRepo.GetCountByPostAsync(postFile.Post);
                if (remainingFiles == 0)
                {
                    await postRepo.UpdateHasFileAsync(postFile.Post, false);
                }
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
    public async Task<List<PostFile>> GetPostFilesByPostAsync(int postNo)
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
            var posts = await uow.Posts.GetListAsync(
                category: category,
                subject: subject);

            // 추가 필터링
            var filtered = new List<Post>();
            foreach (var post in posts)
            {
                // 완료 필터
                if (!includeCompleted && post.IsCompleted)
                    continue;

                // 날짜 필터
                if (startDate.HasValue && post.DateTime.Date < startDate.Value.Date)
                    continue;

                if (endDate.HasValue && post.DateTime.Date > endDate.Value.Date)
                    continue;

                filtered.Add(post);
            }

            return filtered;
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
    public async Task<List<string>> GetCategoriesAsync()
    {
        using var uow = new UnitOfWork(_dbPath);
        return await uow.Posts.GetCategoriesAsync();
    }

    /// <summary>
    /// 주제 목록 조회
    /// </summary>
    public async Task<List<string>> GetSubjectsAsync(string category = "")
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
            System.Diagnostics.Debug.WriteLine($"파일 삭제 실패: {fileName}, {ex.Message}");
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

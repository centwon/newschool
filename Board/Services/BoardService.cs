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
                    if (postId <= 0)
                        throw new InvalidOperationException("글이 저장되지 않았습니다.");
                }
                else
                {
                    // 기존 Post 수정 — 반영 여부를 확인한다.
                    // 예전에는 결과를 버리고 무조건 post.No 를 돌려줘서, 갱신된 행이 없어도
                    // (이미 지워진 글 등) 호출부가 성공으로 보고 창을 닫았다 — 편집이 유실됐다.
                    if (!await uow.Posts.UpdateAsync(post))
                        throw new InvalidOperationException(
                            $"글 #{post.No} 이 갱신되지 않았습니다. 이미 지워진 글일 수 있습니다.");
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

                // 읽어온 값은 증가 이전 값이라, 그대로 돌려주면 화면의 조회수가 DB 보다
                // 항상 1 작다(글을 열었는데 그 열람이 반영 안 된 숫자가 보인다).
                // 다시 SELECT 하지 않고 메모리에서 맞춘다.
                post.ReadCount++;
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
            // 1. 삭제 대상 물리 파일명만 먼저 수집 (아직 삭제하지 않음).
            //    파일을 DB 삭제보다 먼저 지우면, DeleteAsync 가 예외로 실패했을 때
            //    "파일은 사라졌는데 게시글 행은 남는" 역-고아 상태가 된다.
            //    → DeleteComment/DeletePostFile 과 동일하게 "DB 확정 후 파일 삭제" 순서로 통일.
            var filesToDelete = new List<string>();

            using (var commentRepo = new CommentRepository(_dbPath))
            {
                var comments = await commentRepo.GetByPostAsync(postNo);
                foreach (var comment in comments)
                {
                    if (comment.HasFile && !string.IsNullOrEmpty(comment.FileName))
                    {
                        filesToDelete.Add(comment.FileName);
                    }
                }
            }

            using (var postFileRepo = new PostFileRepository(_dbPath))
            {
                var postFiles = await postFileRepo.GetByPostAsync(postNo);
                foreach (var file in postFiles)
                {
                    if (!string.IsNullOrEmpty(file.FileName))
                        filesToDelete.Add(file.FileName);
                }
            }

            // 2. Post 삭제 (CASCADE로 Comment, PostFile도 자동 삭제)
            bool deleted;
            using (var postRepo = new PostRepository(_dbPath))
            {
                deleted = await postRepo.DeleteAsync(postNo);
            }

            // 3. DB 삭제가 확정된 뒤에만 물리 파일 삭제
            if (deleted)
            {
                foreach (var fileName in filesToDelete)
                {
                    DeletePhysicalFile(fileName, category);
                }
            }

            return deleted;
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
        var filesToDelete = new List<string>();

        try
        {
            bool deleted = await uow.ExecuteInTransactionAsync(async () =>
            {
                var comment = await uow.Comments.GetByIdAsync(commentNo);
                if (comment == null)
                    return false;

                int postNo = comment.Post;

                // 최상위 댓글(ParentNo=0)을 지우면 그 답글(ParentNo=이 댓글 No)은 부모를 잃는다.
                // Comment.ParentNo 에는 FK/CASCADE 가 없어(Post 에만 있음) 답글이 DB 에 고아로 남고,
                // 화면(BuildThreadedOrder)에서는 부모가 없어 아예 사라진다 → 답글도 함께 정리한다.
                if (comment.ParentNo == 0)
                {
                    var siblings = await uow.Comments.GetByPostAsync(postNo);
                    foreach (var reply in siblings)
                    {
                        if (reply.ParentNo == commentNo)
                        {
                            if (reply.HasFile && !string.IsNullOrEmpty(reply.FileName))
                                filesToDelete.Add(reply.FileName);
                            await uow.Comments.DeleteAsync(reply.No);
                        }
                    }
                }

                // Comment 삭제 + 남은 댓글 확인 + Post 플래그 업데이트를 하나의 트랜잭션으로 처리
                if (comment.HasFile && !string.IsNullOrEmpty(comment.FileName))
                {
                    filesToDelete.Add(comment.FileName);
                }
                await uow.Comments.DeleteAsync(commentNo);

                int remainingComments = await uow.Comments.GetCountByPostAsync(postNo);
                if (remainingComments == 0)
                {
                    await uow.Posts.UpdateHasCommentAsync(postNo, false);
                }

                return true;
            });

            // DB 트랜잭션 커밋이 확정된 뒤에만 물리 파일 삭제 (롤백 시 고아 파일 방지)
            if (deleted)
            {
                foreach (var fileName in filesToDelete)
                    DeletePhysicalFile(fileName, category);
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

    // HasFile 플래그를 따로 세우는 UpdatePostHasFileAsync 는 호출부가 없어 지웠다(39차).
    // 이 플래그는 첨부를 붙이고 떼는 경로(AddPostFileAsync·DeletePostFileAsync)가 알아서 맞춘다.

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

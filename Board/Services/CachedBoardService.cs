using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NewSchool.Board.Caching;
using NewSchool.Board.Repositories;
using NewSchool.Board.Services;

namespace NewSchool.Board.Services;

/// <summary>
/// 캐싱이 적용된 BoardService
/// 읽기 작업 성능 향상 (Write-Through Cache)
/// </summary>
public class CachedBoardService : BoardService
{
    private readonly CacheManager _cache;
    private readonly string _dbPath;  // ⭐ 추가: 자체 _dbPath 필드
    private readonly TimeSpan _shortCache = TimeSpan.FromMinutes(2);   // 짧은 캐시 (Post 목록)
    private readonly TimeSpan _mediumCache = TimeSpan.FromMinutes(5);  // 중간 캐시 (Post 상세)
    private readonly TimeSpan _longCache = TimeSpan.FromMinutes(30);   // 긴 캐시 (카테고리 목록)

    public CachedBoardService(string dbPath) : base(dbPath)
    {
        _cache = CacheManager.Instance;
        _dbPath = dbPath;  // ⭐ DB 경로 저장
    }

    #region Post Operations (Cached)

    /// <summary>
    /// Post 조회 (캐시됨)
    /// </summary>
    public override async Task<Post?> GetPostAsync(int no, bool incrementReadCount = true)
    {
        string key = CacheKeys.Post(no);

        if (incrementReadCount)
        {
            // 캐시에 있으면 DB 왕복 없이 즉시 반환하고, 조회수 증가는 백그라운드로 미룬다.
            // (같은 글을 반복 열람해도 매번 Get+Update 두 번의 동기 DB 호출이 발생하던 문제 개선)
            if (_cache.TryGet<Post>(key, out var cachedPost) && cachedPost != null)
            {
                _ = IncrementReadCountInBackgroundAsync(no, cachedPost);
                return cachedPost;
            }

            var post = await base.GetPostAsync(no, true);

            if (post != null)
            {
                _cache.Set(key, post, _mediumCache);
            }

            return post;
        }

        // 캐시에서 조회 또는 생성
        return await _cache.GetOrCreateAsync(
            key,
            async () => await base.GetPostAsync(no, false),
            _mediumCache);
    }

    /// <summary>
    /// 조회수 증가를 백그라운드에서 처리 (캐시 히트 시 호출자를 블로킹하지 않음).
    /// 캐시된 Post 인스턴스도 함께 갱신해 다음 조회 시 최신 조회수가 보이도록 한다.
    /// </summary>
    private async Task IncrementReadCountInBackgroundAsync(int postNo, Post cachedPost)
    {
        try
        {
            using var postRepo = new PostRepository(_dbPath);
            await postRepo.IncrementReadCountAsync(postNo);
            cachedPost.ReadCount++;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"조회수 증가(백그라운드) 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// Post 목록 조회 (캐시됨)
    /// </summary>
    public override async Task<PagedResult<Post>> GetPostsPagedAsync(
        int pageNumber,
        int pageSize,
        string category = "",
        string subject = "",
        bool searchTitle = false,
        bool searchContent = false,
        string searchText = "",
        Models.PostSortOrder sortOrder = Models.PostSortOrder.NewestFirst)
    {
        string key = CacheKeys.Posts(pageNumber, pageSize, category, subject,
                                     searchText, searchTitle, searchContent) + $":{sortOrder}";

        return await _cache.GetOrCreateAsync(
            key,
            async () => await base.GetPostsPagedAsync(
                pageNumber, pageSize, category, subject,
                searchTitle, searchContent, searchText, sortOrder),
            _shortCache);
    }

    /// <summary>
    /// Post 저장 (캐시 무효화)
    /// </summary>
    public override async Task<int> SavePostAsync(Post post)
    {
        int result = await base.SavePostAsync(post);

        if (result > 0)
        {
            // 관련 캐시 무효화
            InvalidatePostCaches();
            _cache.Remove(CacheKeys.Post(result));
        }

        return result;
    }

    /// <summary>
    /// Post 삭제 (캐시 무효화)
    /// </summary>
    public override async Task<bool> DeletePostAsync(int postNo, string category)
    {
        bool result = await base.DeletePostAsync(postNo, category);

        if (result)
        {
            // 관련 캐시 무효화
            InvalidatePostCaches();
            _cache.Remove(CacheKeys.Post(postNo));
            _cache.Remove(CacheKeys.Comments(postNo));
            _cache.Remove(CacheKeys.PostFiles(postNo));
        }

        return result;
    }

    #endregion

    #region Comment Operations (Cached)

    /// <summary>
    /// Comment 목록 조회 (캐시됨)
    /// </summary>
    public override async Task<List<Comment>> GetCommentsByPostAsync(int postNo)
    {
        string key = CacheKeys.Comments(postNo);

        return await _cache.GetOrCreateAsync(
            key,
            async () => await base.GetCommentsByPostAsync(postNo),
            _mediumCache);
    }

    /// <summary>
    /// Comment 생성 (캐시 무효화)
    /// </summary>
    public override async Task<int> CreateCommentAsync(Comment comment)
    {
        int result = await base.CreateCommentAsync(comment);

        if (result > 0)
        {
            // 관련 캐시 무효화
            _cache.Remove(CacheKeys.Comments(comment.Post));
            _cache.Remove(CacheKeys.Post(comment.Post));
            // 첫 댓글이면 Post.HasComment 가 켜져 목록의 댓글 아이콘이 달라진다.
            InvalidatePostListCaches();
        }

        return result;
    }

    /// <summary>
    /// Comment 수정 (캐시 무효화)
    /// </summary>
    public override async Task<bool> UpdateCommentAsync(Comment comment)
    {
        bool result = await base.UpdateCommentAsync(comment);

        if (result)
        {
            _cache.Remove(CacheKeys.Comments(comment.Post));
            _cache.Remove(CacheKeys.Post(comment.Post));
            // 내용만 바뀌므로 목록에 보이는 것(댓글 아이콘)은 그대로다 — 목록 캐시는 건드리지 않는다.
        }

        return result;
    }

    /// <summary>
    /// Comment 삭제 (캐시 무효화)
    /// </summary>
    public override async Task<bool> DeleteCommentAsync(int commentNo, string category)
    {
        // Comment 정보 먼저 조회 (Post 번호 필요)
        using var uow = new UnitOfWork(_dbPath);  // ✅ 이제 접근 가능
        var comment = await uow.Comments.GetByIdAsync(commentNo);

        bool result = await base.DeleteCommentAsync(commentNo, category);

        if (result && comment != null)
        {
            // 관련 캐시 무효화
            _cache.Remove(CacheKeys.Comments(comment.Post));
            _cache.Remove(CacheKeys.Post(comment.Post));
            // 마지막 댓글이었으면 Post.HasComment 가 꺼져 목록의 댓글 아이콘이 사라진다.
            InvalidatePostListCaches();
        }

        return result;
    }

    #endregion

    #region PostFile Operations (Cached)

    /// <summary>
    /// PostFile 목록 조회 (캐시됨)
    /// </summary>
    public override async Task<List<PostFile>> GetPostFilesByPostAsync(int postNo)
    {
        string key = CacheKeys.PostFiles(postNo);

        return await _cache.GetOrCreateAsync(
            key,
            async () => await base.GetPostFilesByPostAsync(postNo),
            _mediumCache);
    }

    /// <summary>
    /// PostFile 추가 (캐시 무효화)
    /// </summary>
    public override async Task<int> AddPostFileAsync(PostFile postFile)
    {
        int result = await base.AddPostFileAsync(postFile);

        if (result > 0)
        {
            // 관련 캐시 무효화
            _cache.Remove(CacheKeys.PostFiles(postFile.Post));
            _cache.Remove(CacheKeys.Post(postFile.Post));
            // 첫 첨부면 Post.HasFile 이 켜져 목록의 클립 아이콘이 달라진다.
            InvalidatePostListCaches();
        }

        return result;
    }

    /// <summary>
    /// PostFile 이름 변경 (캐시 무효화)
    /// </summary>
    public override async Task<bool> UpdatePostFileNameAsync(int postFileNo, string fileName)
    {
        // 어느 글의 첨부인지 알아야 그 글의 캐시를 지운다.
        using var uow = new UnitOfWork(_dbPath);
        var postFile = await uow.PostFiles.GetByIdAsync(postFileNo);

        bool result = await base.UpdatePostFileNameAsync(postFileNo, fileName);

        if (result && postFile != null)
        {
            _cache.Remove(CacheKeys.PostFiles(postFile.Post));
            _cache.Remove(CacheKeys.Post(postFile.Post));
            // 이름만 바뀌므로 목록에 보이는 것(클립 아이콘)은 그대로다.
        }

        return result;
    }

    /// <summary>
    /// PostFile 삭제 (캐시 무효화)
    /// </summary>
    public override async Task<bool> DeletePostFileAsync(int postFileNo, string category)
    {
        // PostFile 정보 먼저 조회
        using var uow = new UnitOfWork(_dbPath);  // ✅ 이제 접근 가능
        var postFile = await uow.PostFiles.GetByIdAsync(postFileNo);

        bool result = await base.DeletePostFileAsync(postFileNo, category);

        if (result && postFile != null)
        {
            // 관련 캐시 무효화
            _cache.Remove(CacheKeys.PostFiles(postFile.Post));
            _cache.Remove(CacheKeys.Post(postFile.Post));
            // 마지막 첨부였으면 Post.HasFile 이 꺼져 목록의 클립 아이콘이 사라진다.
            InvalidatePostListCaches();
        }

        return result;
    }

    #endregion

    #region Utility (Cached)

    /// <summary>
    /// 카테고리 목록 조회 (캐시됨, 30분)
    /// </summary>
    public override async Task<List<string>> GetCategoriesAsync()
    {
        return await _cache.GetOrCreateAsync(
            CacheKeys.Categories(),
            async () => await base.GetCategoriesAsync(),
            _longCache);
    }

    /// <summary>
    /// 주제 목록 조회 (캐시됨, 30분)
    /// </summary>
    public override async Task<List<string>> GetSubjectsAsync(string category = "")
    {
        return await _cache.GetOrCreateAsync(
            CacheKeys.Subjects(category),
            async () => await base.GetSubjectsAsync(category),
            _longCache);
    }

    #endregion

    #region Cache Management

    /// <summary>
    /// 목록 캐시만 무효화.
    ///
    /// 목록 캐시는 카테고리·주제·검색어·페이지가 키에 섞여 있어 개별 지목이 어렵다.
    /// 어차피 짧은 캐시(2분)라 접두사로 통째로 비운다.
    ///
    /// 댓글·첨부를 더하거나 지우면 <c>Post.HasComment</c>/<c>HasFile</c> 이 DB 에서 바뀌고
    /// 그 값이 목록의 💬·📎 아이콘이 된다. 예전에는 상세 캐시(<c>board:post:N</c>)만 지워서,
    /// 댓글을 달고 목록으로 돌아와도 아이콘이 최대 2분간 안 붙었다.
    /// </summary>
    private void InvalidatePostListCaches()
    {
        _cache.RemoveByPattern("board:posts:");
    }

    /// <summary>
    /// Post 관련 캐시 무효화 — 목록에 더해 카테고리·주제 목록까지.
    /// </summary>
    private void InvalidatePostCaches()
    {
        InvalidatePostListCaches();
        // 카테고리/주제 목록은 30분 캐시라, 새 주제·카테고리로 글을 쓰면
        // 필터 콤보에 한참 안 나타난다 — 글 저장/삭제 시 함께 무효화
        _cache.Remove(CacheKeys.Categories());
        _cache.RemoveByPattern("board:subjects:");
    }

    // 전체 비우기(ClearAllCaches)·카테고리 비우기(ClearCategoryCache)·통계(GetCacheStatistics)는
    // 호출부가 없어 지웠다(39차). 캐시는 글·댓글·첨부를 쓸 때 InvalidatePostCaches 가
    // 알아서 비우고, 나머지는 짧은 만료로 스스로 사라진다.

    #endregion
}

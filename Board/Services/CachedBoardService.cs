using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NewSchool.Board.Caching;
using NewSchool.Board.Repositories;
using NewSchool.Board.Services;

namespace NewSchool.Board.Services
{
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
        public new async Task<Post?> GetPostAsync(int no, bool incrementReadCount = true)
        {
            string key = CacheKeys.Post(no);

            // 조회수 증가가 필요하면 캐시 사용 안 함
            if (incrementReadCount)
            {
                var post = await base.GetPostAsync(no, true);

                // 조회수 증가 후 캐시 갱신
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
        /// Post 목록 조회 (캐시됨)
        /// </summary>
        public new async Task<PagedResult<Post>> GetPostsPagedAsync(
            int pageNumber,
            int pageSize,
            string category = "",
            string subject = "",
            bool searchTitle = false,
            bool searchContent = false,
            string searchText = "")
        {
            string key = CacheKeys.Posts(pageNumber, pageSize, category, searchText);

            return await _cache.GetOrCreateAsync(
                key,
                async () => await base.GetPostsPagedAsync(
                    pageNumber, pageSize, category, subject,
                    searchTitle, searchContent, searchText),
                _shortCache);
        }

        /// <summary>
        /// Post 저장 (캐시 무효화)
        /// </summary>
        public new async Task<int> SavePostAsync(Post post)
        {
            int result = await base.SavePostAsync(post);

            if (result > 0)
            {
                // 관련 캐시 무효화
                InvalidatePostCaches(post.Category);
                _cache.Remove(CacheKeys.Post(result));
            }

            return result;
        }

        /// <summary>
        /// Post 삭제 (캐시 무효화)
        /// </summary>
        public new async Task<bool> DeletePostAsync(int postNo, string category)
        {
            bool result = await base.DeletePostAsync(postNo, category);

            if (result)
            {
                // 관련 캐시 무효화
                InvalidatePostCaches(category);
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
        public new async Task<List<Comment>> GetCommentsByPostAsync(int postNo)
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
        public new async Task<int> CreateCommentAsync(Comment comment)
        {
            int result = await base.CreateCommentAsync(comment);

            if (result > 0)
            {
                // 관련 캐시 무효화
                _cache.Remove(CacheKeys.Comments(comment.Post));
                _cache.Remove(CacheKeys.Post(comment.Post));
            }

            return result;
        }

        /// <summary>
        /// Comment 수정 (캐시 무효화)
        /// </summary>
        public new async Task<bool> UpdateCommentAsync(Comment comment)
        {
            bool result = await base.UpdateCommentAsync(comment);

            if (result)
            {
                _cache.Remove(CacheKeys.Comments(comment.Post));
            }

            return result;
        }

        /// <summary>
        /// Comment 삭제 (캐시 무효화)
        /// </summary>
        public new async Task<bool> DeleteCommentAsync(int commentNo, string category)
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
            }

            return result;
        }

        #endregion

        #region PostFile Operations (Cached)

        /// <summary>
        /// PostFile 목록 조회 (캐시됨)
        /// </summary>
        public new async Task<List<PostFile>> GetPostFilesByPostAsync(int postNo)
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
        public new async Task<int> AddPostFileAsync(PostFile postFile)
        {
            int result = await base.AddPostFileAsync(postFile);

            if (result > 0)
            {
                // 관련 캐시 무효화
                _cache.Remove(CacheKeys.PostFiles(postFile.Post));
                _cache.Remove(CacheKeys.Post(postFile.Post));
            }

            return result;
        }

        /// <summary>
        /// PostFile 삭제 (캐시 무효화)
        /// </summary>
        public new async Task<bool> DeletePostFileAsync(int postFileNo, string category)
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
            }

            return result;
        }

        #endregion

        #region Utility (Cached)

        /// <summary>
        /// 카테고리 목록 조회 (캐시됨, 30분)
        /// </summary>
        public new async Task<List<string>> GetCategoriesAsync()
        {
            return await _cache.GetOrCreateAsync(
                CacheKeys.Categories(),
                async () => await base.GetCategoriesAsync(),
                _longCache);
        }

        /// <summary>
        /// 주제 목록 조회 (캐시됨, 30분)
        /// </summary>
        public new async Task<List<string>> GetSubjectsAsync(string category = "")
        {
            return await _cache.GetOrCreateAsync(
                CacheKeys.Subjects(category),
                async () => await base.GetSubjectsAsync(category),
                _longCache);
        }

        #endregion

        #region Cache Management

        /// <summary>
        /// Post 관련 캐시 무효화
        /// </summary>
        private void InvalidatePostCaches(string category)
        {
            _cache.RemoveByPattern("board:posts:");
            _cache.RemoveByPattern($"board:count:{category}");
        }

        /// <summary>
        /// 모든 캐시 무효화
        /// </summary>
        public void ClearAllCaches()
        {
            _cache.Clear();
            System.Diagnostics.Debug.WriteLine("[CachedBoardService] 모든 캐시 삭제");
        }

        /// <summary>
        /// 특정 카테고리 캐시 무효화
        /// </summary>
        public void ClearCategoryCache(string category)
        {
            _cache.RemoveByPattern($":{category}");
            System.Diagnostics.Debug.WriteLine($"[CachedBoardService] {category} 캐시 삭제");
        }

        /// <summary>
        /// 캐시 통계 조회
        /// </summary>
        public CacheStatistics GetCacheStatistics()
        {
            return _cache.GetStatistics();
        }

        #endregion
    }
}

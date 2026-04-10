using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace NewSchool.Board.Repositories
{
    /// <summary>
    /// Post 리포지토리 - 비동기 + 트랜잭션 + 에러 처리
    /// </summary>
    public class PostRepository : BaseRepository
    {
        public PostRepository(string dbPath) : base(dbPath)
        {
        }

        #region Create
        /// <summary>
        /// Post 생성 (비동기)
        /// </summary>
        public async Task<int> CreateAsync(Post post)
        {
            // ✅ INSERT와 SELECT를 한 쿼리로 결합
            const string query = @"
        INSERT INTO Post (User, DateTime, Category, Subject, Title, Content, 
                         RefNo, ReplyOrder, Depth, ReadCount, HasFile, HasComment, IsCompleted)
        VALUES (@User, @DateTime, @Category, @Subject, @Title, @Content,
               @RefNo, @ReplyOrder, @Depth, @ReadCount, @HasFile, @HasComment, @IsCompleted);
        SELECT last_insert_rowid();";

            try
            {
                using var cmd = CreateCommand(query);
                AddPostParameters(cmd, post);

                // ✅ ExecuteScalar로 한 번에 실행 (INSERT + SELECT)
                var result = await cmd.ExecuteScalarAsync();
                post.No = Convert.ToInt32(result);

                LogInfo($"Post 생성 완료: No={post.No}, Title={post.Title}");
                return post.No;
            }
            catch (Exception ex)
            {
                LogError($"Post 생성 실패: Title={post.Title}", ex);
                throw;
            }
        }


        #endregion

        #region Read

        /// <summary>
        /// ID로 Post 조회 (비동기)
        /// </summary>
        public async Task<Post?> GetByIdAsync(int no)
        {
            const string query = "SELECT * FROM Post WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.Add("@No", SqliteType.Integer).Value = no;

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return MapPost(reader);
                }

                LogWarning($"Post를 찾을 수 없음: No={no}");
                return null;
            }
            catch (Exception ex)
            {
                LogError($"Post 조회 실패: No={no}", ex);
                throw;
            }
        }

        /// <summary>
        /// Post 목록 조회 (비동기, 페이징, 검색)
        /// </summary>
        public async Task<List<Post>> GetListAsync(
            int limit = 0,
            int offset = 0,
            string category = "",
            string subject = "",
            bool searchTitle = false,
            bool searchContent = false,
            string searchText = "")
        {
            var posts = new List<Post>();

            try
            {
                string query = "SELECT No, User, DateTime, Category, Subject, Title, Content, RefNo, ReplyOrder, Depth, ReadCount, HasFile, HasComment, IsCompleted FROM Post WHERE 1=1";
                using var cmd = CreateCommand(query);

                // 카테고리 필터
                if (!string.IsNullOrEmpty(category))
                {
                    query += " AND Category = @Category";
                    cmd.Parameters.AddWithValue("@Category", category);
                }

                // 주제 필터
                if (!string.IsNullOrEmpty(subject))
                {
                    query += " AND Subject = @Subject";
                    cmd.Parameters.AddWithValue("@Subject", subject);
                }

                // 검색 필터
                if (!string.IsNullOrEmpty(searchText))
                {
                    if (searchTitle && searchContent)
                    {
                        query += " AND (Title LIKE @Search OR Content LIKE @Search)";
                    }
                    else if (searchTitle)
                    {
                        query += " AND Title LIKE @Search";
                    }
                    else if (searchContent)
                    {
                        query += " AND Content LIKE @Search";
                    }

                    if (searchTitle || searchContent)
                    {
                        cmd.Parameters.AddWithValue("@Search", $"%{searchText}%");
                    }
                }

                // 정렬
                query += " ORDER BY No DESC";

                // 페이징
                if (limit > 0)
                {
                    query += " LIMIT @Limit";
                    cmd.Parameters.Add("@Limit", SqliteType.Integer).Value = limit;

                    if (offset > 0)
                    {
                        query += " OFFSET @Offset";
                        cmd.Parameters.Add("@Offset", SqliteType.Integer).Value = offset;
                    }
                }

                cmd.CommandText = query;

                posts = await ExecuteListAsync(cmd, MapPost);

                LogInfo($"Post 목록 조회 완료: {posts.Count}개");
                return posts;
            }
            catch (Exception ex)
            {
                LogError("Post 목록 조회 실패", ex);
                throw;
            }
        }

        /// <summary>
        /// Post 목록 + 전체 개수 조회 (단일 쿼리, COUNT(*) OVER() 윈도우 함수 사용)
        /// </summary>
        public async Task<(List<Post> Posts, int TotalCount)> GetListWithCountAsync(
            int limit,
            int offset,
            string category = "",
            string subject = "",
            bool searchTitle = false,
            bool searchContent = false,
            string searchText = "")
        {
            var posts = new List<Post>();
            int totalCount = 0;

            try
            {
                string query = "SELECT No, User, DateTime, Category, Subject, Title, Content, RefNo, ReplyOrder, Depth, ReadCount, HasFile, HasComment, IsCompleted, COUNT(*) OVER() AS TotalCount FROM Post WHERE 1=1";
                using var cmd = CreateCommand(query);

                // 카테고리 필터
                if (!string.IsNullOrEmpty(category))
                {
                    query += " AND Category = @Category";
                    cmd.Parameters.AddWithValue("@Category", category);
                }

                // 주제 필터
                if (!string.IsNullOrEmpty(subject))
                {
                    query += " AND Subject = @Subject";
                    cmd.Parameters.AddWithValue("@Subject", subject);
                }

                // 검색 필터
                if (!string.IsNullOrEmpty(searchText))
                {
                    if (searchTitle && searchContent)
                    {
                        query += " AND (Title LIKE @Search OR Content LIKE @Search)";
                    }
                    else if (searchTitle)
                    {
                        query += " AND Title LIKE @Search";
                    }
                    else if (searchContent)
                    {
                        query += " AND Content LIKE @Search";
                    }

                    if (searchTitle || searchContent)
                    {
                        cmd.Parameters.AddWithValue("@Search", $"%{searchText}%");
                    }
                }

                // 정렬
                query += " ORDER BY No DESC";

                // 페이징
                if (limit > 0)
                {
                    query += " LIMIT @Limit";
                    cmd.Parameters.Add("@Limit", SqliteType.Integer).Value = limit;

                    if (offset > 0)
                    {
                        query += " OFFSET @Offset";
                        cmd.Parameters.Add("@Offset", SqliteType.Integer).Value = offset;
                    }
                }

                cmd.CommandText = query;

                var cache = new ReaderColumnCache();
                using var reader = await cmd.ExecuteReaderAsync();
                cache.Initialize(reader);

                while (await reader.ReadAsync())
                {
                    posts.Add(MapPost(reader, cache));
                    if (totalCount == 0)
                    {
                        totalCount = reader.GetInt32(cache.GetOrdinal("TotalCount"));
                    }
                }

                LogInfo($"Post 목록+개수 조회 완료: {posts.Count}개 / 전체 {totalCount}개");
                return (posts, totalCount);
            }
            catch (Exception ex)
            {
                LogError("Post 목록+개수 조회 실패", ex);
                throw;
            }
        }

        /// <summary>
        /// Post 개수 조회 (비동기)
        /// </summary>
        public async Task<int> GetCountAsync(
            string category = "",
            string subject = "",
            bool searchTitle = false,
            bool searchContent = false,
            string searchText = "")
        {
            try
            {
                string query = "SELECT COUNT(*) FROM Post WHERE 1=1";
                using var cmd = CreateCommand(query);

                if (!string.IsNullOrEmpty(category))
                {
                    query += " AND Category = @Category";
                    cmd.Parameters.AddWithValue("@Category", category);
                }

                if (!string.IsNullOrEmpty(subject))
                {
                    query += " AND Subject = @Subject";
                    cmd.Parameters.AddWithValue("@Subject", subject);
                }

                if (!string.IsNullOrEmpty(searchText))
                {
                    if (searchTitle && searchContent)
                    {
                        query += " AND (Title LIKE @Search OR Content LIKE @Search)";
                    }
                    else if (searchTitle)
                    {
                        query += " AND Title LIKE @Search";
                    }
                    else if (searchContent)
                    {
                        query += " AND Content LIKE @Search";
                    }

                    if (searchTitle || searchContent)
                    {
                        cmd.Parameters.AddWithValue("@Search", $"%{searchText}%");
                    }
                }

                cmd.CommandText = query;
                var result = await cmd.ExecuteScalarAsync();
                return Convert.ToInt32(result);
            }
            catch (Exception ex)
            {
                LogError("Post 개수 조회 실패", ex);
                throw;
            }
        }

        #endregion

        #region Update

        /// <summary>
        /// Post 수정 (비동기)
        /// </summary>
        public async Task<bool> UpdateAsync(Post post)
        {
            const string query = @"
                UPDATE Post SET
                    User = @User, DateTime = @DateTime, Category = @Category,
                    Subject = @Subject, Title = @Title, Content = @Content,
                    RefNo = @RefNo, ReplyOrder = @ReplyOrder, Depth = @Depth,
                    ReadCount = @ReadCount, HasFile = @HasFile, HasComment = @HasComment,
                    IsCompleted = @IsCompleted
                WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.Add("@No", SqliteType.Integer).Value = post.No;
                AddPostParameters(cmd, post);

                int rowsAffected = await cmd.ExecuteNonQueryAsync();
                bool success = rowsAffected > 0;

                if (success)
                {
                    LogInfo($"Post 수정 완료: No={post.No}");
                }
                else
                {
                    LogWarning($"Post 수정 실패 (존재하지 않음): No={post.No}");
                }

                return success;
            }
            catch (Exception ex)
            {
                LogError($"Post 수정 실패: No={post.No}", ex);
                throw;
            }
        }

        /// <summary>
        /// HasFile 플래그 업데이트 (비동기)
        /// </summary>
        public async Task<bool> UpdateHasFileAsync(int postNo, bool hasFile)
        {
            const string query = "UPDATE Post SET HasFile = @HasFile WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.Add("@No", SqliteType.Integer).Value = postNo;
                cmd.Parameters.Add("@HasFile", SqliteType.Integer).Value = hasFile ? 1 : 0;

                int rowsAffected = await cmd.ExecuteNonQueryAsync();
                LogDebug($"Post.HasFile 업데이트: No={postNo}, HasFile={hasFile}");
                return rowsAffected > 0;
            }
            catch (Exception ex)
            {
                LogError($"HasFile 업데이트 실패: No={postNo}", ex);
                throw;
            }
        }

        /// <summary>
        /// HasComment 플래그 업데이트 (비동기)
        /// </summary>
        public async Task<bool> UpdateHasCommentAsync(int postNo, bool hasComment)
        {
            const string query = "UPDATE Post SET HasComment = @HasComment WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.Add("@No", SqliteType.Integer).Value = postNo;
                cmd.Parameters.Add("@HasComment", SqliteType.Integer).Value = hasComment ? 1 : 0;

                int rowsAffected = await cmd.ExecuteNonQueryAsync();
                LogDebug($"Post.HasComment 업데이트: No={postNo}, HasComment={hasComment}");
                return rowsAffected > 0;
            }
            catch (Exception ex)
            {
                LogError($"HasComment 업데이트 실패: No={postNo}", ex);
                throw;
            }
        }

        /// <summary>
        /// IsCompleted 플래그 업데이트 (비동기)
        /// </summary>
        public async Task<bool> UpdateIsCompletedAsync(int postNo, bool isCompleted)
        {
            const string query = "UPDATE Post SET IsCompleted = @IsCompleted WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.Add("@No", SqliteType.Integer).Value = postNo;
                cmd.Parameters.Add("@IsCompleted", SqliteType.Integer).Value = isCompleted ? 1 : 0;

                int rowsAffected = await cmd.ExecuteNonQueryAsync();
                LogDebug($"Post.IsCompleted 업데이트: No={postNo}, IsCompleted={isCompleted}");
                return rowsAffected > 0;
            }
            catch (Exception ex)
            {
                LogError($"IsCompleted 업데이트 실패: No={postNo}", ex);
                throw;
            }
        }

        /// <summary>
        /// 조회수 증가 (비동기)
        /// </summary>
        public async Task<bool> IncrementReadCountAsync(int postNo)
        {
            const string query = "UPDATE Post SET ReadCount = ReadCount + 1 WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.Add("@No", SqliteType.Integer).Value = postNo;

                int rowsAffected = await cmd.ExecuteNonQueryAsync();
                return rowsAffected > 0;
            }
            catch (Exception ex)
            {
                LogError($"조회수 증가 실패: No={postNo}", ex);
                throw;
            }
        }

        #endregion

        #region Delete

        /// <summary>
        /// Post 삭제 (비동기, CASCADE로 자동 관련 데이터 삭제)
        /// </summary>
        public async Task<bool> DeleteAsync(int postNo)
        {
            const string query = "DELETE FROM Post WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.Add("@No", SqliteType.Integer).Value = postNo;

                int rowsAffected = await cmd.ExecuteNonQueryAsync();
                bool success = rowsAffected > 0;

                if (success)
                {
                    LogInfo($"Post 삭제 완료: No={postNo} (CASCADE로 Comment, PostFile도 삭제됨)");
                }
                else
                {
                    LogWarning($"Post 삭제 실패 (존재하지 않음): No={postNo}");
                }

                return success;
            }
            catch (Exception ex)
            {
                LogError($"Post 삭제 실패: No={postNo}", ex);
                throw;
            }
        }

        #endregion

        #region Utility

        /// <summary>
        /// 카테고리 목록 조회 (비동기)
        /// </summary>
        public async Task<List<string>> GetCategoriesAsync()
        {
            const string query = "SELECT DISTINCT Category FROM Post WHERE Category IS NOT NULL AND Category != '' ORDER BY Category";

            var categories = new List<string>();

            try
            {
                using var cmd = CreateCommand(query);
                using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    categories.Add(reader.GetString(0));
                }

                return categories;
            }
            catch (Exception ex)
            {
                LogError("카테고리 목록 조회 실패", ex);
                throw;
            }
        }

        /// <summary>
        /// 주제 목록 조회 (비동기)
        /// </summary>
        public async Task<List<string>> GetSubjectsAsync(string category = "")
        {
            string query = "SELECT DISTINCT Subject FROM Post WHERE Subject IS NOT NULL AND Subject != ''";

            if (!string.IsNullOrEmpty(category))
            {
                query += " AND Category = @Category";
            }

            query += " ORDER BY Subject";

            var subjects = new List<string>();

            try
            {
                using var cmd = CreateCommand(query);
                if (!string.IsNullOrEmpty(category))
                {
                    cmd.Parameters.AddWithValue("@Category", category);
                }

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    subjects.Add(reader.GetString(0));
                }

                return subjects;
            }
            catch (Exception ex)
            {
                LogError("주제 목록 조회 실패", ex);
                throw;
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Post 파라미터 추가 (Native AOT 호환)
        /// </summary>
        private void AddPostParameters(SqliteCommand cmd, Post post)
        {
            cmd.Parameters.AddWithValue("@User", post.User);
            cmd.Parameters.AddWithValue("@DateTime", DateTimeHelper.ToStandardString(post.DateTime));
            cmd.Parameters.AddWithValue("@Category", post.Category);
            cmd.Parameters.AddWithValue("@Subject", post.Subject);
            cmd.Parameters.AddWithValue("@Title", post.Title);
            cmd.Parameters.AddWithValue("@Content", post.Content);
            cmd.Parameters.Add("@RefNo", SqliteType.Integer).Value = post.RefNo;
            cmd.Parameters.Add("@ReplyOrder", SqliteType.Integer).Value = post.ReplyOrder;
            cmd.Parameters.Add("@Depth", SqliteType.Integer).Value = post.Depth;
            cmd.Parameters.Add("@ReadCount", SqliteType.Integer).Value = post.ReadCount;
            cmd.Parameters.Add("@HasFile", SqliteType.Integer).Value = post.HasFile ? 1 : 0;
            cmd.Parameters.Add("@HasComment", SqliteType.Integer).Value = post.HasComment ? 1 : 0;
            cmd.Parameters.Add("@IsCompleted", SqliteType.Integer).Value = post.IsCompleted ? 1 : 0;
        }

        /// <summary>
        /// SqliteDataReader를 Post로 매핑 (캐시된 컬럼 인덱스 사용)
        /// </summary>
        private Post MapPost(SqliteDataReader reader, ReaderColumnCache cache)
        {
            var noOrd = cache.GetOrdinal("No");
            var userOrd = cache.GetOrdinal("User");
            var dtOrd = cache.GetOrdinal("DateTime");
            var catOrd = cache.GetOrdinal("Category");
            var subOrd = cache.GetOrdinal("Subject");
            var titleOrd = cache.GetOrdinal("Title");
            var contentOrd = cache.GetOrdinal("Content");
            var refOrd = cache.GetOrdinal("RefNo");
            var replyOrd = cache.GetOrdinal("ReplyOrder");
            var depthOrd = cache.GetOrdinal("Depth");
            var readOrd = cache.GetOrdinal("ReadCount");
            var fileOrd = cache.GetOrdinal("HasFile");
            var commentOrd = cache.GetOrdinal("HasComment");

            var post = new Post
            {
                No = reader.GetInt32(noOrd),
                User = reader.GetString(userOrd),
                DateTime = DateTimeHelper.FromDateString(reader.GetString(dtOrd)),
                Category = reader.IsDBNull(catOrd) ? "" : reader.GetString(catOrd),
                Subject = reader.IsDBNull(subOrd) ? "" : reader.GetString(subOrd),
                Title = reader.GetString(titleOrd),
                Content = reader.IsDBNull(contentOrd) ? "" : reader.GetString(contentOrd),
                RefNo = reader.GetInt32(refOrd),
                ReplyOrder = reader.GetInt32(replyOrd),
                Depth = reader.GetInt32(depthOrd),
                ReadCount = reader.GetInt32(readOrd),
                HasFile = reader.GetInt32(fileOrd) == 1,
                HasComment = reader.GetInt32(commentOrd) == 1
            };

            // IsCompleted 컬럼 (기존 DB 호환성)
            if (cache.TryGetOrdinal("IsCompleted", out var compOrd))
                post.IsCompleted = !reader.IsDBNull(compOrd) && reader.GetInt32(compOrd) == 1;

            return post;
        }

        /// <summary>
        /// 비캐시 오버로드 (단일 행 조회용)
        /// </summary>
        private Post MapPost(SqliteDataReader reader)
        {
            var cache = new ReaderColumnCache();
            cache.Initialize(reader);
            return MapPost(reader, cache);
        }

        #endregion
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace NewSchool.Board.Repositories
{
    /// <summary>
    /// Comment 리포지토리 - 비동기 + 트랜잭션 + 에러 처리
    /// </summary>
    public class CommentRepository : BaseRepository
    {
        public CommentRepository(string dbPath) : base(dbPath)
        {
        }

        /// <summary>UnitOfWork 공유 연결 생성자.</summary>
        public CommentRepository(SqliteConnection connection) : base(connection)
        {
        }

        #region Create

        /// <summary>
        /// Comment 생성 (비동기)
        /// </summary>
        public async Task<int> CreateAsync(Comment comment)
        {
            // INSERT + SELECT 를 한 번에 실행 (왕복 1회)
            const string query = @"
                INSERT INTO Comment (Post, User, DateTime, ParentNo, Content, HasFile, FileName, FileSize)
                VALUES (@Post, @User, @DateTime, @ParentNo, @Content, @HasFile, @FileName, @FileSize);
                SELECT last_insert_rowid();";

            try
            {
                using var cmd = CreateCommand(query);
                AddCommentParameters(cmd, comment);

                var result = await cmd.ExecuteScalarAsync();
                comment.No = Convert.ToInt32(result);

                LogInfo($"Comment 생성 완료: No={comment.No}, Post={comment.Post}");
                return comment.No;
            }
            catch (Exception ex)
            {
                LogError($"Comment 생성 실패: Post={comment.Post}", ex);
                throw;
            }
        }

        #endregion

        #region Read

        /// <summary>
        /// ID로 Comment 조회 (비동기)
        /// </summary>
        public async Task<Comment?> GetByIdAsync(int no)
        {
            const string query = "SELECT * FROM Comment WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.Add("@No", SqliteType.Integer).Value = no;

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return MapComment(reader);
                }

                LogWarning($"Comment를 찾을 수 없음: No={no}");
                return null;
            }
            catch (Exception ex)
            {
                LogError($"Comment 조회 실패: No={no}", ex);
                throw;
            }
        }

        /// <summary>
        /// Post의 모든 Comment 조회 (비동기)
        /// </summary>
        public async Task<List<Comment>> GetByPostAsync(int postNo)
        {
            // 대화 흐름상 자연스럽도록 오래된 순(작성 순서)으로 정렬
            const string query = "SELECT * FROM Comment WHERE Post = @Post ORDER BY DateTime ASC";

            var comments = new List<Comment>();

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.Add("@Post", SqliteType.Integer).Value = postNo;

                comments = await ExecuteListAsync(cmd, MapComment);

                LogInfo($"Comment 목록 조회 완료: Post={postNo}, Count={comments.Count}");
                return comments;
            }
            catch (Exception ex)
            {
                LogError($"Comment 목록 조회 실패: Post={postNo}", ex);
                throw;
            }
        }

        /// <summary>
        /// 여러 Post의 Comment 개수를 한 번의 쿼리로 일괄 조회 (N+1 방지)
        /// </summary>
        public async Task<Dictionary<int, int>> GetCountsByPostsAsync(IReadOnlyList<int> postNos)
        {
            var counts = new Dictionary<int, int>();
            if (postNos == null || postNos.Count == 0)
                return counts;

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < postNos.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("@p").Append(i);
            }
            var query = $"SELECT Post, COUNT(*) AS Cnt FROM Comment WHERE Post IN ({sb}) GROUP BY Post";

            try
            {
                using var cmd = CreateCommand(query);
                for (int i = 0; i < postNos.Count; i++)
                    cmd.Parameters.Add($"@p{i}", SqliteType.Integer).Value = postNos[i];

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    counts[reader.GetInt32(0)] = reader.GetInt32(1);

                return counts;
            }
            catch (Exception ex)
            {
                LogError("Comment 개수 일괄 조회 실패", ex);
                throw;
            }
        }

        /// <summary>
        /// Post의 Comment 개수 조회 (비동기)
        /// </summary>
        public async Task<int> GetCountByPostAsync(int postNo)
        {
            const string query = "SELECT COUNT(*) FROM Comment WHERE Post = @Post";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.Add("@Post", SqliteType.Integer).Value = postNo;

                var result = await cmd.ExecuteScalarAsync();
                return Convert.ToInt32(result);
            }
            catch (Exception ex)
            {
                LogError($"Comment 개수 조회 실패: Post={postNo}", ex);
                throw;
            }
        }

        #endregion

        #region Update

        /// <summary>
        /// Comment 수정 (비동기)
        /// </summary>
        public async Task<bool> UpdateAsync(Comment comment)
        {
            const string query = @"
                UPDATE Comment SET
                    Content = @Content, HasFile = @HasFile, 
                    FileName = @FileName, FileSize = @FileSize
                WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.Add("@No", SqliteType.Integer).Value = comment.No;
                cmd.Parameters.AddWithValue("@Content", comment.Content);
                cmd.Parameters.Add("@HasFile", SqliteType.Integer).Value = comment.HasFile ? 1 : 0;
                cmd.Parameters.AddWithValue("@FileName", comment.FileName);
                cmd.Parameters.Add("@FileSize", SqliteType.Integer).Value = comment.FileSize;

                int rowsAffected = await cmd.ExecuteNonQueryAsync();
                bool success = rowsAffected > 0;

                if (success)
                {
                    LogInfo($"Comment 수정 완료: No={comment.No}");
                }
                else
                {
                    LogWarning($"Comment 수정 실패 (존재하지 않음): No={comment.No}");
                }

                return success;
            }
            catch (Exception ex)
            {
                LogError($"Comment 수정 실패: No={comment.No}", ex);
                throw;
            }
        }

        /// <summary>
        /// Comment 내용만 수정 (비동기)
        /// </summary>
        public async Task<bool> UpdateContentAsync(int no, string content)
        {
            const string query = "UPDATE Comment SET Content = @Content WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.Add("@No", SqliteType.Integer).Value = no;
                cmd.Parameters.AddWithValue("@Content", content);

                int rowsAffected = await cmd.ExecuteNonQueryAsync();
                LogDebug($"Comment 내용 수정: No={no}");
                return rowsAffected > 0;
            }
            catch (Exception ex)
            {
                LogError($"Comment 내용 수정 실패: No={no}", ex);
                throw;
            }
        }

        #endregion

        #region Delete

        /// <summary>
        /// Comment 삭제 (비동기)
        /// </summary>
        public async Task<bool> DeleteAsync(int no)
        {
            const string query = "DELETE FROM Comment WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.Add("@No", SqliteType.Integer).Value = no;

                int rowsAffected = await cmd.ExecuteNonQueryAsync();
                bool success = rowsAffected > 0;

                if (success)
                {
                    LogInfo($"Comment 삭제 완료: No={no}");
                }
                else
                {
                    LogWarning($"Comment 삭제 실패 (존재하지 않음): No={no}");
                }

                return success;
            }
            catch (Exception ex)
            {
                LogError($"Comment 삭제 실패: No={no}", ex);
                throw;
            }
        }

        /// <summary>
        /// Post의 모든 Comment 삭제 (비동기)
        /// </summary>
        public async Task<int> DeleteByPostAsync(int postNo)
        {
            const string query = "DELETE FROM Comment WHERE Post = @Post";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.Add("@Post", SqliteType.Integer).Value = postNo;

                int rowsAffected = await cmd.ExecuteNonQueryAsync();
                LogInfo($"Comment 일괄 삭제 완료: Post={postNo}, Count={rowsAffected}");
                return rowsAffected;
            }
            catch (Exception ex)
            {
                LogError($"Comment 일괄 삭제 실패: Post={postNo}", ex);
                throw;
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Comment 파라미터 추가 (Native AOT 호환)
        /// </summary>
        private void AddCommentParameters(SqliteCommand cmd, Comment comment)
        {
            cmd.Parameters.Add("@Post", SqliteType.Integer).Value = comment.Post;
            cmd.Parameters.AddWithValue("@User", comment.User); // 추가
            cmd.Parameters.AddWithValue("@DateTime", DateTimeHelper.ToStandardString(comment.DateTime));
            cmd.Parameters.Add("@ParentNo", SqliteType.Integer).Value = comment.ParentNo;
            cmd.Parameters.AddWithValue("@Content", comment.Content);
            cmd.Parameters.Add("@HasFile", SqliteType.Integer).Value = comment.HasFile ? 1 : 0;
            cmd.Parameters.AddWithValue("@FileName", comment.FileName);
            cmd.Parameters.Add("@FileSize", SqliteType.Integer).Value = comment.FileSize;
        }
        /// <summary>
        /// SqliteDataReader를 Comment로 매핑 (캐시된 컬럼 인덱스 사용)
        /// </summary>
        private Comment MapComment(SqliteDataReader reader, ReaderColumnCache cache)
        {
            var noOrd = cache.GetOrdinal("No");
            var postOrd = cache.GetOrdinal("Post");
            var userOrd = cache.GetOrdinal("User");
            var dtOrd = cache.GetOrdinal("DateTime");
            var parentOrd = cache.GetOrdinal("ParentNo");
            var contentOrd = cache.GetOrdinal("Content");
            var fileOrd = cache.GetOrdinal("HasFile");
            var nameOrd = cache.GetOrdinal("FileName");
            var sizeOrd = cache.GetOrdinal("FileSize");

            return new Comment
            {
                No = reader.GetInt32(noOrd),
                Post = reader.GetInt32(postOrd),
                User = reader.IsDBNull(userOrd) ? "" : reader.GetString(userOrd),
                DateTime = DateTimeHelper.FromDateString(reader.GetString(dtOrd)),
                ParentNo = reader.GetInt32(parentOrd),
                Content = reader.IsDBNull(contentOrd) ? "" : reader.GetString(contentOrd),
                HasFile = reader.GetInt32(fileOrd) == 1,
                FileName = reader.IsDBNull(nameOrd) ? "" : reader.GetString(nameOrd),
                FileSize = reader.GetInt32(sizeOrd)
            };
        }

        /// <summary>
        /// 비캐시 오버로드 (단일 행 조회용)
        /// </summary>
        private Comment MapComment(SqliteDataReader reader)
        {
            var cache = new ReaderColumnCache();
            cache.Initialize(reader);
            return MapComment(reader, cache);
        }

        #endregion
    }
}

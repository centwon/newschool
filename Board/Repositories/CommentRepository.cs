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

        #region Create

        /// <summary>
        /// Comment 생성 (비동기)
        /// </summary>
        public async Task<int> CreateAsync(Comment comment)
        {
            const string query = @"
                                INSERT INTO Comment (Post, User, DateTime, ReplyOrder, Content, HasFile, FileName, FileSize)
                                VALUES (@Post, @User, @DateTime, @ReplyOrder, @Content, @HasFile, @FileName, @FileSize)";

            try
            {
                using var cmd = CreateCommand(query);
                AddCommentParameters(cmd, comment);

                await cmd.ExecuteNonQueryAsync();

                // 마지막 삽입 ID
                cmd.CommandText = "SELECT last_insert_rowid()";
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
                cmd.Parameters.AddWithValue("@No", no);

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
            const string query = "SELECT * FROM Comment WHERE Post = @Post ORDER BY DateTime DESC";

            var comments = new List<Comment>();

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@Post", postNo);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    comments.Add(MapComment(reader));
                }

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
        /// Post의 Comment 개수 조회 (비동기)
        /// </summary>
        public async Task<int> GetCountByPostAsync(int postNo)
        {
            const string query = "SELECT COUNT(*) FROM Comment WHERE Post = @Post";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@Post", postNo);

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
                cmd.Parameters.AddWithValue("@No", comment.No);
                cmd.Parameters.AddWithValue("@Content", comment.Content);
                cmd.Parameters.AddWithValue("@HasFile", comment.HasFile ? 1 : 0);
                cmd.Parameters.AddWithValue("@FileName", comment.FileName);
                cmd.Parameters.AddWithValue("@FileSize", comment.FileSize);

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
                cmd.Parameters.AddWithValue("@No", no);
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
                cmd.Parameters.AddWithValue("@No", no);

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
                cmd.Parameters.AddWithValue("@Post", postNo);

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
            cmd.Parameters.AddWithValue("@Post", comment.Post);
            cmd.Parameters.AddWithValue("@User", comment.User); // 추가
            cmd.Parameters.AddWithValue("@DateTime", DateTimeHelper.ToStandardString(comment.DateTime));
            cmd.Parameters.AddWithValue("@ReplyOrder", comment.ReplyOrder);
            cmd.Parameters.AddWithValue("@Content", comment.Content);
            cmd.Parameters.AddWithValue("@HasFile", comment.HasFile ? 1 : 0);
            cmd.Parameters.AddWithValue("@FileName", comment.FileName);
            cmd.Parameters.AddWithValue("@FileSize", comment.FileSize);
        }
        /// <summary>
        /// SqliteDataReader를 Comment로 매핑 (Native AOT 호환)
        /// </summary>
        private Comment MapComment(SqliteDataReader reader)
        {
            return new Comment
            {
                No = reader.GetInt32(reader.GetOrdinal("No")),
                Post = reader.GetInt32(reader.GetOrdinal("Post")),
                User = reader.IsDBNull(reader.GetOrdinal("User")) ? "" : reader.GetString(reader.GetOrdinal("User")), // 추가
                DateTime = DateTimeHelper.FromDateString(reader.GetString(reader.GetOrdinal("DateTime"))),
                ReplyOrder = reader.GetInt32(reader.GetOrdinal("ReplyOrder")),
                Content = reader.IsDBNull(reader.GetOrdinal("Content")) ? "" : reader.GetString(reader.GetOrdinal("Content")),
                HasFile = reader.GetInt32(reader.GetOrdinal("HasFile")) == 1,
                FileName = reader.IsDBNull(reader.GetOrdinal("FileName")) ? "" : reader.GetString(reader.GetOrdinal("FileName")),
                FileSize = reader.GetInt32(reader.GetOrdinal("FileSize"))
            };
        }

        #endregion
    }
}

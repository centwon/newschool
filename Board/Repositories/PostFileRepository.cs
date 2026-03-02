using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace NewSchool.Board.Repositories
{
    /// <summary>
    /// PostFile 리포지토리 - 비동기 + 트랜잭션 + 에러 처리
    /// </summary>
    public class PostFileRepository : BaseRepository
    {
        public PostFileRepository(string dbPath) : base(dbPath)
        {
        }

        #region Create

        /// <summary>
        /// PostFile 생성 (비동기)
        /// </summary>
        public async Task<int> CreateAsync(PostFile postFile)
        {
            const string query = @"
                INSERT INTO PostFile (Post, DateTime, FileName, FileSize)
                VALUES (@Post, @DateTime, @FileName, @FileSize)";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@Post", postFile.Post);
                cmd.Parameters.AddWithValue("@DateTime", DateTimeHelper.ToStandardString(postFile.DateTime));
                cmd.Parameters.AddWithValue("@FileName", postFile.FileName);
                cmd.Parameters.AddWithValue("@FileSize", postFile.FileSize);

                await cmd.ExecuteNonQueryAsync();

                // 마지막 삽입 ID
                cmd.CommandText = "SELECT last_insert_rowid()";
                var result = await cmd.ExecuteScalarAsync();
                postFile.No = Convert.ToInt32(result);

                LogInfo($"PostFile 생성 완료: No={postFile.No}, FileName={postFile.FileName}");
                return postFile.No;
            }
            catch (Exception ex)
            {
                LogError($"PostFile 생성 실패: FileName={postFile.FileName}", ex);
                throw;
            }
        }

        #endregion

        #region Read

        /// <summary>
        /// ID로 PostFile 조회 (비동기)
        /// </summary>
        public async Task<PostFile?> GetByIdAsync(int no)
        {
            const string query = "SELECT * FROM PostFile WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@No", no);

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return MapPostFile(reader);
                }

                LogWarning($"PostFile을 찾을 수 없음: No={no}");
                return null;
            }
            catch (Exception ex)
            {
                LogError($"PostFile 조회 실패: No={no}", ex);
                throw;
            }
        }

        /// <summary>
        /// Post의 모든 PostFile 조회 (비동기)
        /// </summary>
        public async Task<List<PostFile>> GetByPostAsync(int postNo)
        {
            const string query = "SELECT * FROM PostFile WHERE Post = @Post ORDER BY No ASC";

            var files = new List<PostFile>();

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@Post", postNo);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    files.Add(MapPostFile(reader));
                }

                LogInfo($"PostFile 목록 조회 완료: Post={postNo}, Count={files.Count}");
                return files;
            }
            catch (Exception ex)
            {
                LogError($"PostFile 목록 조회 실패: Post={postNo}", ex);
                throw;
            }
        }

        /// <summary>
        /// Post의 PostFile 개수 조회 (비동기)
        /// </summary>
        public async Task<int> GetCountByPostAsync(int postNo)
        {
            const string query = "SELECT COUNT(*) FROM PostFile WHERE Post = @Post";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@Post", postNo);

                var result = await cmd.ExecuteScalarAsync();
                return Convert.ToInt32(result);
            }
            catch (Exception ex)
            {
                LogError($"PostFile 개수 조회 실패: Post={postNo}", ex);
                throw;
            }
        }

        #endregion

        #region Delete

        /// <summary>
        /// PostFile 삭제 (비동기)
        /// </summary>
        public async Task<bool> DeleteAsync(int no)
        {
            const string query = "DELETE FROM PostFile WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@No", no);

                int rowsAffected = await cmd.ExecuteNonQueryAsync();
                bool success = rowsAffected > 0;

                if (success)
                {
                    LogInfo($"PostFile 삭제 완료: No={no}");
                }
                else
                {
                    LogWarning($"PostFile 삭제 실패 (존재하지 않음): No={no}");
                }

                return success;
            }
            catch (Exception ex)
            {
                LogError($"PostFile 삭제 실패: No={no}", ex);
                throw;
            }
        }

        /// <summary>
        /// Post의 모든 PostFile 삭제 (비동기)
        /// </summary>
        public async Task<int> DeleteByPostAsync(int postNo)
        {
            const string query = "DELETE FROM PostFile WHERE Post = @Post";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@Post", postNo);

                int rowsAffected = await cmd.ExecuteNonQueryAsync();
                LogInfo($"PostFile 일괄 삭제 완료: Post={postNo}, Count={rowsAffected}");
                return rowsAffected;
            }
            catch (Exception ex)
            {
                LogError($"PostFile 일괄 삭제 실패: Post={postNo}", ex);
                throw;
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// SqliteDataReader를 PostFile로 매핑 (Native AOT 호환)
        /// </summary>
        private PostFile MapPostFile(SqliteDataReader reader)
        {
            return new PostFile
            {
                No = reader.GetInt32(reader.GetOrdinal("No")),
                Post = reader.GetInt32(reader.GetOrdinal("Post")),
                DateTime = DateTimeHelper.FromDateString(reader.GetString(reader.GetOrdinal("DateTime"))),
                FileName = reader.GetString(reader.GetOrdinal("FileName")),
                FileSize = reader.GetInt32(reader.GetOrdinal("FileSize"))
            };
        }

        #endregion
    }
}

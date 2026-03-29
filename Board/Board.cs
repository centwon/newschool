using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Board.Repositories;
using NewSchool.Board.Services;
using NewSchool.Controls;

namespace NewSchool.Board
{
    /// <summary>
    /// Board 클래스 - 완전 리팩토링 버전
    /// Repository 패턴 + 비동기 + 트랜잭션
    /// </summary>
    public static class Board
    {
        public static string Data_Dir { get; set; } =
    Path.Combine(Settings.UserDataPath, "Files");

        // ✅ 매번 새로 생성하는 헬퍼
        public static BoardService CreateService() => new BoardService(DbPath);

        public static CachedBoardService CreateCachedService() => new CachedBoardService(DbPath);

        // ✅ 전체 DB 경로
        private static string DbPath => Path.Combine(Settings.UserDataPath, Settings.Board_DB.Value);



        #region Database Management



        #endregion



        #region Initialization


        public static async Task InitAsync()
        {
            try
            {
                // 데이터 디렉토리 생성
                string dataDir = Settings.DataDirectory;
                if (!Directory.Exists(dataDir))
                {
                    Directory.CreateDirectory(dataDir);
                    Debug.WriteLine($"[Board] 데이터 디렉토리 생성: {dataDir}");
                }

                // 파일 디렉토리 생성
                if (!Directory.Exists(Data_Dir))
                {
                    Directory.CreateDirectory(Data_Dir);
                    Debug.WriteLine($"[Board] 파일 디렉토리 생성: {Data_Dir}");
                }

                Debug.WriteLine($"[Board] DB 경로: {DbPath}");
                Debug.WriteLine($"[Board] DB 존재: {File.Exists(DbPath)}");
                Debug.WriteLine($"[Board] 초기화 상태: {Settings.Board_Inited.Value}");

                // 데이터베이스 초기화 (매번 실행 - 마이그레이션 포함)
                Debug.WriteLine("[Board] 데이터베이스 초기화 시작");
                bool success = await InitDatabaseAsync();

                if (success)
                {
                    if (!Settings.Board_Inited.Value)
                    {
                        Settings.Board_Inited.Set(true);
                        Debug.WriteLine("[Board] 초기화 완료 플래그 설정됨");
                    }
                }
                else
                {
                    await MessageBox.ShowAsync("데이터베이스 초기화에 실패하였습니다.","오류");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Board] 초기화 실패: {ex.Message}");
                Debug.WriteLine($"[Board] StackTrace: {ex.StackTrace}");
                await MessageBox.ShowAsync($"초기화 오류: {ex.Message}", "오류");
            }
        }

        private static async Task<bool> InitDatabaseAsync()
        {
            try
            {
                // ✅ 수정: 전체 경로 전달
                using var dbInit = new DatabaseInitializer(DbPath);
                return await dbInit.InitializeAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Board] DB 초기화 실패: {ex.Message}");
                return false;
            }
        }


        /// <summary>
        /// 데이터베이스 초기화 (비동기)
        /// </summary>

        #endregion

        #region Database Management




        /// <summary>
        /// 데이터베이스 검증 (비동기)
        /// </summary>
        public static async Task<bool> ValidateDatabaseAsync()
        {
            try
            {
                using var validator = new DatabaseValidator(DbPath);
                return await validator.ValidateAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DB 검증 실패: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 데이터베이스 최적화 (비동기)
        /// </summary>
        public static async Task<bool> OptimizeDatabaseAsync()
        {
            try
            {
                using var optimizer = new DatabaseOptimizer(DbPath);
                return await optimizer.OptimizeAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DB 최적화 실패: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 데이터베이스 백업 (비동기)
        /// </summary>
        public static async Task<bool> BackupDatabaseAsync(string backupPath)
        {
            try
            {
                await Task.Run(() => File.Copy(DbPath, backupPath, true));
                Debug.WriteLine($"DB 백업 완료: {backupPath}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DB 백업 실패: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 데이터베이스 복원 (비동기)
        /// </summary>
        public static async Task<bool> RestoreDatabaseAsync(string backupPath)
        {
            try
            {
                if (!File.Exists(backupPath))
                {
                    Debug.WriteLine("백업 파일이 존재하지 않습니다.");
                    return false;
                }

                await Task.Run(() => File.Copy(backupPath, DbPath, true));
                Debug.WriteLine($"DB 복원 완료: {backupPath}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DB 복원 실패: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 데이터베이스 완전 초기화 (비동기)
        /// </summary>
        public static async Task<bool> ResetDatabaseAsync()
        {
            try
            {
                var confirmed = await MessageBox.ShowConfirmAsync(
                    "모든 게시물, 댓글, 파일이 삭제됩니다.\n정말 초기화하시겠습니까?",
                    "데이터베이스 초기화", "초기화", "취소");
                if (!confirmed)
                    return false;

                // DB 파일 삭제
                if (File.Exists(DbPath))
                {
                    File.Delete(DbPath);
                }

                // 파일 디렉토리 삭제
                if (Directory.Exists(Data_Dir))
                {
                    Directory.Delete(Data_Dir, true);
                }

                // 재초기화
                Settings.Board_Inited.Set(false);
                await InitAsync();

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DB 초기화 실패: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// 파일 경로 가져오기
        /// </summary>
        public static string GetFilePath(string fileName, string category)
        {
            return Path.Combine(Data_Dir, category, fileName);
        }

        /// <summary>
        /// 카테고리 디렉토리 확인 및 생성
        /// </summary>
        public static void EnsureCategoryDirectory(string category)
        {
            string categoryPath = Path.Combine(Data_Dir, category);
            if (!Directory.Exists(categoryPath))
            {
                Directory.CreateDirectory(categoryPath);
            }
        }

        #endregion
    }

    #region Database Helper Classes

    /// <summary>
    /// 데이터베이스 초기화 헬퍼
    /// </summary>
    internal class DatabaseInitializer : BaseRepository
    {
        public DatabaseInitializer(string dbPath) : base(dbPath) { }

        public async Task<bool> InitializeAsync()
        {
            try
            {
                Debug.WriteLine($"[DatabaseInitializer] DB 경로: {_dbPath}");

                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                Debug.WriteLine("[DatabaseInitializer] 연결 성공");

                // WAL 모드 및 동시성 설정
                using (var pragmaCmd = connection.CreateCommand())
                {
                    pragmaCmd.CommandText = @"
                    PRAGMA journal_mode=WAL;
                    PRAGMA synchronous=NORMAL;
                    PRAGMA busy_timeout=5000;
                    PRAGMA temp_store=MEMORY;
                    PRAGMA foreign_keys=ON;
                ";
                    await pragmaCmd.ExecuteNonQueryAsync();
                    Debug.WriteLine("[DatabaseInitializer] PRAGMA 설정 완료");
                }

                // 테이블 생성
                await CreateTablesAsync(connection);

                // 인덱스 생성
                await CreateIndexesAsync(connection);

                Debug.WriteLine("[DatabaseInitializer] 초기화 완료");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DatabaseInitializer] 실패: {ex.Message}");
                Debug.WriteLine($"[DatabaseInitializer] StackTrace: {ex.StackTrace}");
                return false;
            }
        }

        private async Task CreateTablesAsync(SqliteConnection connection)
        {
            using var cmd = connection.CreateCommand();

            // Post 테이블
            cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS Post (
                No INTEGER PRIMARY KEY AUTOINCREMENT,
                User TEXT NOT NULL DEFAULT '',
                DateTime TEXT NOT NULL DEFAULT '',
                Category TEXT DEFAULT '',
                Subject TEXT DEFAULT '',
                Title TEXT NOT NULL DEFAULT '',
                Content TEXT DEFAULT '',
                RefNo INTEGER DEFAULT 0,
                ReplyOrder INTEGER DEFAULT 0,
                Depth INTEGER DEFAULT 0,
                ReadCount INTEGER DEFAULT 0,
                HasFile INTEGER DEFAULT 0,
                HasComment INTEGER DEFAULT 0,
                IsCompleted INTEGER DEFAULT 0
            )";
            await cmd.ExecuteNonQueryAsync();
            Debug.WriteLine("[DatabaseInitializer] Post 테이블 생성 완료");

            // 기존 DB에 IsCompleted 컨럼 추가 (마이그레이션)
            try
            {
                cmd.CommandText = "ALTER TABLE Post ADD COLUMN IsCompleted INTEGER DEFAULT 0";
                await cmd.ExecuteNonQueryAsync();
                Debug.WriteLine("[DatabaseInitializer] IsCompleted 컬럼 추가 완료");
            }
            catch (SqliteException)
            {
                // 이미 컬럼이 존재하는 경우 무시
            }

            // 마이그레이션: Category="Memo"인 기존 데이터의 Subject를 Category로 이동
            try
            {
                cmd.CommandText = @"
                    UPDATE Post
                    SET Category = Subject, Subject = ''
                    WHERE Category = 'Memo' AND Subject != '' AND Subject IS NOT NULL";
                int migratedCount = await cmd.ExecuteNonQueryAsync();
                if (migratedCount > 0)
                {
                    Debug.WriteLine($"[DatabaseInitializer] Memo→Category 마이그레이션 완료: {migratedCount}개");
                }
            }
            catch (SqliteException ex)
            {
                Debug.WriteLine($"[DatabaseInitializer] Memo 마이그레이션 실패: {ex.Message}");
            }

            // 마이그레이션: 남은 Category="Memo" (Subject가 비어있던 것)을 "업무"로 변경
            try
            {
                cmd.CommandText = @"
                    UPDATE Post
                    SET Category = '업무'
                    WHERE Category = 'Memo'";
                int remainingCount = await cmd.ExecuteNonQueryAsync();
                if (remainingCount > 0)
                {
                    Debug.WriteLine($"[DatabaseInitializer] 잔여 Memo→업무 마이그레이션 완료: {remainingCount}개");
                }
            }
            catch (SqliteException ex)
            {
                Debug.WriteLine($"[DatabaseInitializer] 잔여 Memo 마이그레이션 실패: {ex.Message}");
            }

            // Comment 테이블
            cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS Comment (
                No INTEGER PRIMARY KEY AUTOINCREMENT,
                Post INTEGER NOT NULL,
                User TEXT NOT NULL DEFAULT '',
                DateTime TEXT NOT NULL DEFAULT '',
                ReplyOrder INTEGER DEFAULT 0,
                Content TEXT DEFAULT '',
                HasFile INTEGER DEFAULT 0,
                FileName TEXT DEFAULT '',
                FileSize INTEGER DEFAULT 0,
                FOREIGN KEY (Post) REFERENCES Post(No) ON DELETE CASCADE
            )";
            await cmd.ExecuteNonQueryAsync();
            Debug.WriteLine("[DatabaseInitializer] Comment 테이블 생성 완료");

            // PostFile 테이블
            cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS PostFile (
                No INTEGER PRIMARY KEY AUTOINCREMENT,
                Post INTEGER NOT NULL,
                DateTime TEXT NOT NULL DEFAULT '',
                FileName TEXT NOT NULL DEFAULT '',
                FileSize INTEGER DEFAULT 0,
                FOREIGN KEY (Post) REFERENCES Post(No) ON DELETE CASCADE
            )";
            await cmd.ExecuteNonQueryAsync();
            Debug.WriteLine("[DatabaseInitializer] PostFile 테이블 생성 완료");
        }

        private async Task CreateIndexesAsync(SqliteConnection connection)
        {
            using var cmd = connection.CreateCommand();

            cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_post_category ON Post(Category)";
            await cmd.ExecuteNonQueryAsync();

            cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_post_subject ON Post(Subject)";
            await cmd.ExecuteNonQueryAsync();

            cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_post_datetime ON Post(DateTime DESC)";
            await cmd.ExecuteNonQueryAsync();

            cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_comment_post ON Comment(Post)";
            await cmd.ExecuteNonQueryAsync();

            cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_postfile_post ON PostFile(Post)";
            await cmd.ExecuteNonQueryAsync();

            Debug.WriteLine("[DatabaseInitializer] 인덱스 생성 완료");
        }
    }
    /// <summary>
    /// 데이터베이스 검증 헬퍼
    /// </summary>
    internal class DatabaseValidator : BaseRepository
    {
        public DatabaseValidator(string dbPath) : base(dbPath) { }

        public async Task<bool> ValidateAsync()
        {
            try
            {
                // 무결성 검사
                var integrityResult = await ExecuteScalarAsync("PRAGMA integrity_check");
                if (integrityResult?.ToString() != "ok")
                {
                    LogError($"무결성 검사 실패: {integrityResult}");
                    return false;
                }

                // 필수 테이블 확인
                var tables = await GetTablesAsync();
                if (!tables.Contains("Post") || !tables.Contains("Comment") || !tables.Contains("PostFile"))
                {
                    LogError("필수 테이블이 존재하지 않습니다.");
                    return false;
                }

                LogInfo("데이터베이스 검증 완료");
                return true;
            }
            catch (Exception ex)
            {
                LogError("데이터베이스 검증 실패", ex);
                return false;
            }
        }

        private async Task<List<string>> GetTablesAsync()
        {
            var tables = new List<string>();
            using var cmd = CreateCommand("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'");
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }

            return tables;
        }
    }

    /// <summary>
    /// 데이터베이스 최적화 헬퍼
    /// </summary>
    internal class DatabaseOptimizer : BaseRepository
    {
        public DatabaseOptimizer(string dbPath) : base(dbPath) { }

        public async Task<bool> OptimizeAsync()
        {
            try
            {
                await ExecuteNonQueryAsync("VACUUM");
                await ExecuteNonQueryAsync("ANALYZE");

                LogInfo("데이터베이스 최적화 완료");
                return true;
            }
            catch (Exception ex)
            {
                LogError("데이터베이스 최적화 실패", ex);
                return false;
            }
        }
    }
    public static class DatabaseHelper
    {
        public static string GetConnectionString(string dbPath)
        {
            return new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared,
                Pooling = true
            }.ToString();
        }
    }
    #endregion
}

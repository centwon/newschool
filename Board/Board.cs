using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Board.Repositories;
using NewSchool.Board.Services;
using NewSchool.Controls;

namespace NewSchool.Board;

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

            // 데이터베이스 초기화 (매번 실행 — CREATE TABLE IF NOT EXISTS 라 두 번째부터는 무해)
            Debug.WriteLine("[Board] 데이터베이스 초기화 시작");

            // 초기화 완료 플래그(Settings.Board_Inited)는 세우기만 하고 읽는 곳이 없어
            // 설정째로 지웠다(2026-08-31). 초기화는 어차피 매번 도는 자리다.
            if (!await InitDatabaseAsync())
            {
                await MessageBox.ShowAsync("데이터베이스 초기화에 실패하였습니다.", "오류");
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

    // DB 유지보수 묶음(ValidateDatabaseAsync·OptimizeDatabaseAsync·BackupDatabaseAsync·
    // RestoreDatabaseAsync·ResetDatabaseAsync)은 호출부가 한 곳도 없어 지웠다(39차).
    // 전용 헬퍼였던 DatabaseValidator·DatabaseOptimizer 도 함께 사라졌다.
    // school.db 쪽 같은 메서드들은 2026-08-16 에 이미 제거했고, 사용자에게 보이는
    // 백업·복원은 Settings 의 백업 ZIP(VACUUM INTO 스냅샷) 한 경로뿐이다.

    #region Utility Methods

    /// <summary>
    /// 파일 경로 가져오기
    /// </summary>
    public static string GetFilePath(string fileName, string category)
    {
        return Path.Combine(Data_Dir, category, fileName);
    }

    // 읽기 전용 뷰 BlockedAttachmentExtensions 는 쓰는 곳이 없어 지웠다(39차) —
    // 바깥에서는 IsBlockedAttachment 로 판정만 하면 된다.

    /// <summary>
    /// 파일명의 확장자가 실행 유발 차단 목록에 있으면 true.
    ///
    /// <para>목록 자체는 <see cref="Helpers.AttachmentPolicy"/> 로 옮겼다 — 누가기록도
    /// 첨부를 받게 되면서 게시판만의 것이 아니게 됐다. 목록이 두 벌이 되면 한쪽만 늘어나
    /// 조용히 어긋나므로 한 곳에 둔다. 게시판 쪽 호출부는 그대로 두려고 이 이름을 남긴다.</para>
    /// </summary>
    public static bool IsBlockedAttachment(string fileName) =>
        Helpers.AttachmentPolicy.IsBlocked(fileName);

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
                    PRAGMA cache_size=10000;
                    PRAGMA mmap_size=30000000;
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
                Content BLOB,
                PlainText TEXT NOT NULL DEFAULT '',
                RefNo INTEGER DEFAULT 0,
                ReplyOrder INTEGER DEFAULT 0,
                Depth INTEGER DEFAULT 0,
                ReadCount INTEGER DEFAULT 0,
                HasFile INTEGER DEFAULT 0,
                HasComment INTEGER DEFAULT 0,
                IsCompleted INTEGER DEFAULT 0,
                IsPinned INTEGER NOT NULL DEFAULT 0
            )";
        await cmd.ExecuteNonQueryAsync();
        Debug.WriteLine("[DatabaseInitializer] Post 테이블 생성 완료");

        // Comment 테이블
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS Comment (
                No INTEGER PRIMARY KEY AUTOINCREMENT,
                Post INTEGER NOT NULL,
                User TEXT NOT NULL DEFAULT '',
                DateTime TEXT NOT NULL DEFAULT '',
                ParentNo INTEGER DEFAULT 0,
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

    // ⚠ 마이그레이션(ALTER)은 **두지 않기로 한 것**이다(2026-08-25 결정, 재검토 금지).
    // 위 CREATE 는 IF NOT EXISTS 라서 이미 만들어진 파일에는 아무 일도 하지 않는다 —
    // 열이 늘면 쓰던 board.db 에 직접 넣는다. IsPinned(중요 글, 39차)도 그렇게 손으로 넣었다:
    //   ALTER TABLE Post ADD COLUMN IsPinned INTEGER NOT NULL DEFAULT 0
    //
    // 대가를 알고 내린 결정이다: 조회 쿼리가 컬럼 이름을 직접 대므로(SELECT ... IsPinned,
    // ORDER BY IsPinned DESC), 컬럼이 없는 옛 board.db 에서는 'no such column' 으로
    // 그 화면이 통째로 실패한다. 매핑 쪽 TryGetOrdinal 방어는 쿼리가 먼저 터져서 못 막는다.
    // v1.0.0 신규 설치는 위 CREATE 에 IsPinned 가 들어 있어 해당 없음.

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
// DatabaseHelper.GetConnectionString 은 호출부가 없어 지웠다(39차).
// 게시판 연결 문자열은 BaseRepository 가 자기 것을 만들어 쓴다.
#endregion

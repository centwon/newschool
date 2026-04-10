using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Models;

namespace NewSchool.Scheduler
{
    /// <summary>
    /// Scheduler 데이터베이스 초기화
    /// Board/School의 DatabaseInitializer 패턴 적용
    /// ⚠️ SchoolSchedule 테이블은 school.db에서 관리하므로 여기서는 제거
    /// </summary>
    internal class DatabaseInitializer : IDisposable
    {
        private readonly string _dbPath;
        private SqliteConnection? _connection;
        private bool _disposed;

        public DatabaseInitializer(string dbPath)
        {
            _dbPath = dbPath;
        }

        public async Task<bool> InitializeAsync()
        {
            try
            {
                Debug.WriteLine($"[SchedulerDB] DB 경로: {_dbPath}");

                _connection = new SqliteConnection($"Data Source={_dbPath}");
                await _connection.OpenAsync();

                Debug.WriteLine("[SchedulerDB] 연결 성공");

                // WAL 모드 및 동시성 설정
                using (var pragmaCmd = _connection.CreateCommand())
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
                    Debug.WriteLine("[SchedulerDB] PRAGMA 설정 완료");
                }

                // 테이블 생성
                await CreateTablesAsync();

                // 인덱스 생성
                await CreateIndexesAsync();

                Debug.WriteLine("[SchedulerDB] 초기화 완료");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SchedulerDB] 실패: {ex.Message}");
                Debug.WriteLine($"[SchedulerDB] StackTrace: {ex.StackTrace}");
                return false;
            }
        }

        private async Task CreateTablesAsync()
        {
            if (_connection == null) return;

            using var cmd = _connection.CreateCommand();

            // KCalendarList 테이블
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS KCalendarList (
                    No        INTEGER PRIMARY KEY AUTOINCREMENT,
                    GoogleId  TEXT NOT NULL DEFAULT '',
                    Title     TEXT NOT NULL DEFAULT '',
                    Color     TEXT NOT NULL DEFAULT '#4285F4',
                    SortOrder INTEGER NOT NULL DEFAULT 0,
                    IsDefault INTEGER NOT NULL DEFAULT 0,
                    IsVisible INTEGER NOT NULL DEFAULT 1,
                    Updated   TEXT NOT NULL DEFAULT '',
                    SyncMode  TEXT NOT NULL DEFAULT 'None'
                );
            ";
            await cmd.ExecuteNonQueryAsync();
            Debug.WriteLine("[SchedulerDB] KCalendarList 테이블 생성 완료");

            // KEvent 테이블 (일정 + 할일 통합)
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS KEvent (
                    No         INTEGER PRIMARY KEY AUTOINCREMENT,
                    GoogleId   TEXT NOT NULL DEFAULT '',
                    CalendarId INTEGER NOT NULL DEFAULT 0,
                    Title      TEXT NOT NULL DEFAULT '',
                    Notes      TEXT NOT NULL DEFAULT '',
                    Start      TEXT NOT NULL,
                    End        TEXT NOT NULL,
                    IsAllday   INTEGER NOT NULL DEFAULT 0,
                    Location   TEXT NOT NULL DEFAULT '',
                    Status     TEXT NOT NULL DEFAULT 'confirmed',
                    ColorId    TEXT NOT NULL DEFAULT '',
                    Recurrence TEXT NOT NULL DEFAULT '',
                    Updated    TEXT NOT NULL DEFAULT '',
                    User       TEXT NOT NULL DEFAULT '',
                    ItemType   TEXT NOT NULL DEFAULT 'event',
                    IsDone     INTEGER NOT NULL DEFAULT 0,
                    Completed  TEXT NOT NULL DEFAULT ''
                );
            ";
            await cmd.ExecuteNonQueryAsync();
            Debug.WriteLine("[SchedulerDB] KEvent 테이블 생성 완료");

            // 기존 DB 호환: 누락 컬럼 추가
            await AddColumnIfNotExistsAsync(cmd, "KEvent", "ItemType", "TEXT NOT NULL DEFAULT 'event'");
            await AddColumnIfNotExistsAsync(cmd, "KEvent", "IsDone", "INTEGER NOT NULL DEFAULT 0");
            await AddColumnIfNotExistsAsync(cmd, "KEvent", "Completed", "TEXT NOT NULL DEFAULT ''");

            // 기본 목록 생성
            await SeedDefaultListsAsync(cmd);
        }

        private static async Task AddColumnIfNotExistsAsync(SqliteCommand cmd, string table, string column, string type)
        {
            cmd.CommandText = $"PRAGMA table_info({table})";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                if (reader.GetString(reader.GetOrdinal("name")) == column)
                    return; // 이미 존재
            }
            reader.Close();

            cmd.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {type}";
            await cmd.ExecuteNonQueryAsync();
            Debug.WriteLine($"[SchedulerDB] {table}.{column} 컴럼 추가 완료");
        }

        private static async Task SeedDefaultListsAsync(SqliteCommand cmd)
        {
            // 기본 목록이 없으면 생성
            cmd.CommandText = "SELECT EXISTS(SELECT 1 FROM KtaskList)";
            var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            if (count == 1) return;

            // (title, order, syncMode)
            var defaults = new[] { (CategoryNames.Lesson, 1, "None"), (CategoryNames.Homeroom, 2, "None"), (CategoryNames.Work, 3, "None"), (CategoryNames.Personal, 4, "TwoWay") };
            foreach (var (title, order, sync) in defaults)
            {
                cmd.CommandText = @"
                    INSERT INTO KtaskList (GoogleId, Title, SortOrder, IsDefault, Updated, SyncMode)
                    VALUES ('', @Title, @Order, 1, @Updated, @SyncMode)";
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@Title", title);
                cmd.Parameters.AddWithValue("@Order", order);
                cmd.Parameters.AddWithValue("@Updated", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
                cmd.Parameters.AddWithValue("@SyncMode", sync);
                await cmd.ExecuteNonQueryAsync();
            }
            Debug.WriteLine("[SchedulerDB] 기본 목록 4개 생성 완료 (수업/학급/업무/개인)");

            // KCalendarList 기본 캘린더 생성 (없는 경우만)
            cmd.CommandText = "SELECT EXISTS(SELECT 1 FROM KCalendarList)";
            count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            if (count == 0)
            {
                // (title, order, color, syncMode)
                var calendars = new[]
                {
                    (CategoryNames.Lesson, 1, "#4285F4", "None"),   // 파란색
                    (CategoryNames.Homeroom, 2, "#0F9D58", "None"),   // 초록색
                    (CategoryNames.Work, 3, "#DB4437", "None"),   // 빨간색
                    (CategoryNames.Personal, 4, "#F4B400", "TwoWay")  // 노란색
                };
                foreach (var (title, order, color, sync) in calendars)
                {
                    cmd.CommandText = @"
                        INSERT INTO KCalendarList (GoogleId, Title, Color, SortOrder, IsDefault, IsVisible, Updated, SyncMode)
                        VALUES ('', @Title, @Color, @Order, 1, 1, @Updated, @SyncMode)";
                    cmd.Parameters.Clear();
                    cmd.Parameters.AddWithValue("@Title", title);
                    cmd.Parameters.AddWithValue("@Color", color);
                    cmd.Parameters.AddWithValue("@Order", order);
                    cmd.Parameters.AddWithValue("@Updated", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
                    cmd.Parameters.AddWithValue("@SyncMode", sync);
                    await cmd.ExecuteNonQueryAsync();
                }
                Debug.WriteLine("[SchedulerDB] 기본 캘린더 4개 생성 완료 (수업/학급/업무/개인)");
            }
        }

        private async Task CreateIndexesAsync()
        {
            if (_connection == null) return;

            using var cmd = _connection.CreateCommand();

            cmd.CommandText = @"
                CREATE INDEX IF NOT EXISTS idx_kevent_start ON KEvent(Start);
                CREATE INDEX IF NOT EXISTS idx_kevent_calendarid ON KEvent(CalendarId);
                CREATE INDEX IF NOT EXISTS idx_kevent_status ON KEvent(Status);
                CREATE INDEX IF NOT EXISTS idx_kcalendarlist_title ON KCalendarList(Title);
                CREATE INDEX IF NOT EXISTS idx_kevent_googleid ON KEvent(GoogleId);
                CREATE INDEX IF NOT EXISTS idx_kevent_itemtype ON KEvent(ItemType);
                CREATE INDEX IF NOT EXISTS idx_kevent_isdone ON KEvent(IsDone);
            ";

            await cmd.ExecuteNonQueryAsync();

            // SyncToken 컬럼 추가 (기존 DB 호환)
            using var alterCmd = _connection.CreateCommand();
            await AddColumnIfNotExistsAsync(alterCmd, "KCalendarList", "SyncToken", "TEXT NOT NULL DEFAULT ''");

            Debug.WriteLine("[SchedulerDB] 인덱스 생성 완료");
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _connection?.Dispose();
                _disposed = true;
            }
        }
    }
}

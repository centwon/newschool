using System;
using System.Diagnostics;
using Microsoft.Data.Sqlite;
using System.Threading.Tasks;
using NewSchool.Logging;

namespace NewSchool.Board.Repositories
{
    /// <summary>
    /// 모든 Repository의 기반 클래스
    /// Native AOT 호환 + 비동기 + 트랜잭션 + 에러 처리
    /// </summary>
    public abstract class BaseRepository : IDisposable
    {
        protected readonly string _dbPath;
        protected readonly SqliteConnection Connection;
        protected SqliteTransaction? Transaction;
        private bool _disposed;

        // ⭐ public getter 추가
        public SqliteTransaction? GetTransaction() => Transaction;
        public SqliteConnection GetConnection() => Connection;

        protected BaseRepository(string dbPath)
        {
            try
            {
                _dbPath = dbPath;  // ⭐ 저장

                // ✅ 동시성 개선을 위한 연결 옵션
                var connectionString = new SqliteConnectionStringBuilder
                {
                    DataSource = dbPath,
                    Mode = SqliteOpenMode.ReadWriteCreate,
                    Cache = SqliteCacheMode.Shared,
                    Pooling = true
                }.ToString();

                Connection = new SqliteConnection(connectionString);
                Connection.Open();

                // ✅ WAL 모드 활성화 (동시 읽기/쓰기 개선)
                using var cmd = Connection.CreateCommand();
                cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
                cmd.ExecuteNonQuery();

                LogDebug($"{GetType().Name} 연결 열림 (WAL 모드)");
            }
            catch (Exception ex)
            {
                LogError($"{GetType().Name} 연결 실패", ex);
                throw;
            }
        }

        #region Transaction Management

        /// <summary>
        /// ⭐ 외부에서 트랜잭션 설정 (UnitOfWork용)
        /// </summary>
        public void SetTransaction(SqliteTransaction? transaction)
        {
            Transaction = transaction;
            LogDebug($"트랜잭션 설정됨: {(transaction != null ? "활성" : "비활성")}");
        }

        /// <summary>
        /// 트랜잭션 시작
        /// </summary>
        public void BeginTransaction()
        {
            try
            {
                Transaction?.Dispose();
                Transaction = Connection.BeginTransaction();
                LogDebug("트랜잭션 시작");
            }
            catch (Exception ex)
            {
                LogError("트랜잭션 시작 실패", ex);
                throw;
            }
        }

        /// <summary>
        /// 트랜잭션 커밋
        /// </summary>
        public void Commit()
        {
            try
            {
                Transaction?.Commit();
                LogDebug("트랜잭션 커밋 완료");
            }
            catch (Exception ex)
            {
                LogError("트랜잭션 커밋 실패", ex);
                Rollback();
                throw;
            }
            finally
            {
                Transaction?.Dispose();
                Transaction = null;
            }
        }

        /// <summary>
        /// 트랜잭션 롤백
        /// </summary>
        public void Rollback()
        {
            try
            {
                Transaction?.Rollback();
                LogDebug("트랜잭션 롤백 완료");
            }
            catch (Exception ex)
            {
                LogError("트랜잭션 롤백 실패", ex);
            }
            finally
            {
                Transaction?.Dispose();
                Transaction = null;
            }
        }

        /// <summary>
        /// 트랜잭션 내에서 작업 실행
        /// </summary>
        protected async Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation)
        {
            BeginTransaction();
            try
            {
                var result = await operation();
                Commit();
                return result;
            }
            catch (Exception ex)
            {
                Rollback();
                LogError("트랜잭션 작업 실패", ex);
                throw;
            }
        }

        /// <summary>
        /// 트랜잭션 내에서 작업 실행 (반환값 없음)
        /// </summary>
        protected async Task ExecuteInTransactionAsync(Func<Task> operation)
        {
            BeginTransaction();
            try
            {
                await operation();
                Commit();
            }
            catch (Exception ex)
            {
                Rollback();
                LogError("트랜잭션 작업 실패", ex);
                throw;
            }
        }

        #endregion

        #region Command Helpers

        /// <summary>
        /// 명령 생성 헬퍼
        /// </summary>
        protected SqliteCommand CreateCommand(string query)
        {
            var cmd = Connection.CreateCommand();
            cmd.CommandText = query;
            if (Transaction != null)
            {
                cmd.Transaction = Transaction;
            }
            return cmd;
        }

        /// <summary>
        /// 비동기 NonQuery 실행
        /// </summary>
        protected async Task<int> ExecuteNonQueryAsync(string query)
        {
            try
            {
                using var cmd = CreateCommand(query);
                return await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                LogError($"ExecuteNonQuery 실패: {query}", ex);
                throw;
            }
        }

        /// <summary>
        /// 비동기 Scalar 실행
        /// </summary>
        protected async Task<object?> ExecuteScalarAsync(string query)
        {
            try
            {
                using var cmd = CreateCommand(query);
                return await cmd.ExecuteScalarAsync();
            }
            catch (Exception ex)
            {
                LogError($"ExecuteScalar 실패: {query}", ex);
                throw;
            }
        }

        #endregion

        #region Logging

        protected void LogDebug(string message)
        {
            Debug.WriteLine($"[DEBUG] {DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}");
        }

        protected void LogInfo(string message)
        {
            Debug.WriteLine($"[INFO] {DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}");
        }

        protected void LogWarning(string message)
        {
            Debug.WriteLine($"[WARNING] {DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}");
            FileLogger.Instance.Warning($"[{GetType().Name}] {message}");
        }

        protected void LogError(string message, Exception? ex = null)
        {
            Debug.WriteLine($"[ERROR] {DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}");
            if (ex != null)
            {
                Debug.WriteLine($"  Exception: {ex.GetType().Name}");
                Debug.WriteLine($"  Message: {ex.Message}");
                Debug.WriteLine($"  StackTrace: {ex.StackTrace}");
            }
            FileLogger.Instance.Error($"[{GetType().Name}] {message}", ex);
        }

        #endregion

        #region Dispose

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
                    try
                    {
                        Transaction?.Dispose();

                        // ✅ 강제로 연결 종료
                        if (Connection != null)
                        {
                            if (Connection.State == System.Data.ConnectionState.Open)
                            {
                                Connection.Close();
                            }
                            Connection.Dispose();
                        }

                        LogDebug($"{GetType().Name} 리소스 해제 완료");
                    }
                    catch (Exception ex)
                    {
                        LogError("Dispose 중 오류", ex);
                    }
                }
                _disposed = true;
            }
        }
        #endregion
    }
}

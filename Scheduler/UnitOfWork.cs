using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Scheduler.Repositories;
using NewSchool.Models;
using NewSchool.Services;
using NewSchool.Repositories;

namespace NewSchool.Scheduler
{
    /// <summary>
    /// Unit of Work 패턴 - 단일 Connection + 단일 Transaction으로 원자성 보장
    /// ✅ Ktask → KEvent 통합: KtaskRepository 제거
    /// </summary>
    public sealed class UnitOfWork : IDisposable
    {
        private readonly string _dbPath;
        private SqliteConnection? _connection;
        private SqliteTransaction? _transaction;
        private bool _disposed;

        private SchoolScheduleRepository?  _schedules;
        private KEventRepository?          _kevents;

        public SchoolScheduleRepository Schedules
        {
            get
            {
                if (_schedules == null)
                {
                    _schedules = new SchoolScheduleRepository(EnsureConnection());
                    if (_transaction != null)
                        _schedules.SetTransaction(_transaction);
                }
                return _schedules;
            }
        }

        public KEventRepository KEvents
        {
            get
            {
                if (_kevents == null)
                {
                    _kevents = new KEventRepository(EnsureConnection());
                    if (_transaction != null)
                        _kevents.SetTransaction(_transaction);
                }
                return _kevents;
            }
        }

        public UnitOfWork(string dbPath)
        {
            _dbPath = dbPath;
        }

        /// <summary>공유 연결을 한 번만 만들고 PRAGMA(WAL 등)를 적용한다. 모든 Repository 가 이 연결을 공유.</summary>
        private SqliteConnection EnsureConnection()
        {
            if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
            {
                var cs = new SqliteConnectionStringBuilder
                {
                    DataSource = _dbPath,
                    Mode = SqliteOpenMode.ReadWriteCreate,
                    Cache = SqliteCacheMode.Shared,
                    Pooling = true
                }.ToString();

                _connection = new SqliteConnection(cs);
                _connection.Open();

                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON; PRAGMA temp_store=MEMORY; PRAGMA busy_timeout=5000; PRAGMA cache_size=10000; PRAGMA mmap_size=30000000;";
                cmd.ExecuteNonQuery();
            }
            return _connection;
        }

        /// <summary>
        /// 단일 트랜잭션 시작 — 모든 Repository 가 공유 연결을 쓰므로 Schedules·KEvents 에 원자적으로 적용.
        /// </summary>
        public void BeginTransaction()
        {
            var conn = EnsureConnection();
            _transaction?.Dispose();
            _transaction = conn.BeginTransaction();

            _schedules?.SetTransaction(_transaction);
            _kevents?.SetTransaction(_transaction);
        }

        /// <summary>
        /// 단일 트랜잭션 커밋
        /// </summary>
        public void Commit()
        {
            try
            {
                _transaction?.Commit();
            }
            catch
            {
                Rollback();
                throw;
            }
            finally
            {
                ClearTransaction();
            }
        }

        /// <summary>
        /// 단일 트랜잭션 롤백
        /// </summary>
        public void Rollback()
        {
            try
            {
                _transaction?.Rollback();
            }
            finally
            {
                ClearTransaction();
            }
        }

        private void ClearTransaction()
        {
            _transaction?.Dispose();
            _transaction = null;
            _schedules?.SetTransaction(null);
            _kevents?.SetTransaction(null);
        }

        /// <summary>
        /// 트랜잭션 내에서 작업 실행 (반환값 있음)
        /// </summary>
        public async Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation)
        {
            BeginTransaction();
            try
            {
                var result = await operation();
                Commit();
                return result;
            }
            catch
            {
                Rollback();
                throw;
            }
        }

        /// <summary>
        /// 트랜잭션 내에서 작업 실행 (반환값 없음)
        /// </summary>
        public async Task ExecuteInTransactionAsync(Func<Task> operation)
        {
            BeginTransaction();
            try
            {
                await operation();
                Commit();
            }
            catch
            {
                Rollback();
                throw;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _transaction?.Dispose();

                _schedules?.Dispose();
                _kevents?.Dispose();

                _connection?.Close();
                _connection?.Dispose();
                _disposed = true;
            }
        }
    }
}

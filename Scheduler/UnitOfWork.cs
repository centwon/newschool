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
    public class UnitOfWork : IDisposable
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
                    EnsureConnection();
                    _schedules = new SchoolScheduleRepository(_dbPath);
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
                    EnsureConnection();
                    _kevents = new KEventRepository(_dbPath);
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

        private void EnsureConnection()
        {
            if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
            {
                _connection = new SqliteConnection($"Data Source={_dbPath}");
                _connection.Open();
            }
        }

        /// <summary>
        /// 단일 트랜잭션 시작 — KEventRepository의 Connection에서 시작
        /// </summary>
        public void BeginTransaction()
        {
            // KEventRepository 생성 (Connection 확보)
            if (_kevents == null)
                _ = KEvents;

            _transaction?.Dispose();

            // KEventRepository에서 트랜잭션 시작
            if (_kevents != null)
            {
                _kevents.BeginTransaction();
                _transaction = _kevents.GetTransaction();
                _connection = _kevents.GetConnection();
            }

            // 이미 생성된 다른 Repository들에도 트랜잭션 설정
            _schedules?.SetTransaction(_transaction);
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
                _transaction?.Dispose();
                _transaction = null;
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
                _transaction?.Dispose();
                _transaction = null;
            }
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

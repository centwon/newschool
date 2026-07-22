using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace NewSchool.Repositories
{
    /// <summary>
    /// Course + Lesson 을 하나의 연결·하나의 트랜잭션으로 묶는 Unit of Work.
    /// 과목과 시간표(Lesson)는 서로 다른 리포지토리지만 원자적으로 생성/삭제되어야 하므로
    /// (예: 과목 생성 후 시간표 생성 실패 시 고아 과목 방지) 공유 연결이 필요하다.
    /// Scheduler.UnitOfWork 와 동일한 패턴.
    /// </summary>
    public sealed class TimetableUnitOfWork : IDisposable
    {
        private readonly string _dbPath;
        private SqliteConnection? _connection;
        private SqliteTransaction? _transaction;
        private bool _disposed;

        private CourseRepository? _courses;
        private LessonRepository? _lessons;

        public TimetableUnitOfWork(string dbPath)
        {
            _dbPath = dbPath;
        }

        public CourseRepository Courses
        {
            get
            {
                if (_courses == null)
                {
                    _courses = new CourseRepository(EnsureConnection());
                    if (_transaction != null)
                        _courses.SetTransaction(_transaction);
                }
                return _courses;
            }
        }

        public LessonRepository Lessons
        {
            get
            {
                if (_lessons == null)
                {
                    _lessons = new LessonRepository(EnsureConnection());
                    if (_transaction != null)
                        _lessons.SetTransaction(_transaction);
                }
                return _lessons;
            }
        }

        /// <summary>공유 연결을 한 번만 만들고 PRAGMA(WAL·foreign_keys 등)를 적용한다. 모든 Repository 가 이 연결을 공유.</summary>
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

        /// <summary>단일 트랜잭션 시작 — Courses·Lessons 가 공유 연결을 쓰므로 원자적으로 적용된다.</summary>
        public void BeginTransaction()
        {
            var conn = EnsureConnection();
            _transaction?.Dispose();
            _transaction = conn.BeginTransaction();

            _courses?.SetTransaction(_transaction);
            _lessons?.SetTransaction(_transaction);
        }

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
            _courses?.SetTransaction(null);
            _lessons?.SetTransaction(null);
        }

        /// <summary>트랜잭션 내에서 작업 실행 (반환값 있음). 실패 시 자동 롤백.</summary>
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

        /// <summary>트랜잭션 내에서 작업 실행 (반환값 없음). 실패 시 자동 롤백.</summary>
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

                _courses?.Dispose();
                _lessons?.Dispose();

                _connection?.Close();
                _connection?.Dispose();
                _disposed = true;
            }
        }
    }
}

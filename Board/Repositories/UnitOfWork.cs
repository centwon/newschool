using System;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace NewSchool.Board.Repositories
{
    /// <summary>
    /// Unit of Work 패턴 - 여러 Repository가 단일 연결·단일 트랜잭션을 공유.
    /// 모든 Repository 가 같은 연결을 쓰므로 트랜잭션이 Post/Comment/PostFile 전체에 원자적으로 적용된다.
    /// </summary>
    public class UnitOfWork : IDisposable
    {
        private readonly string _dbPath;
        private SqliteConnection? _connection;
        private SqliteTransaction? _transaction;
        private bool _disposed;

        // Repositories (모두 공유 연결 사용)
        private PostRepository? _postRepository;
        private CommentRepository? _commentRepository;
        private PostFileRepository? _postFileRepository;

        public UnitOfWork(string dbPath)
        {
            _dbPath = dbPath;
        }

        #region Repository Properties

        public PostRepository Posts
        {
            get
            {
                if (_postRepository == null)
                {
                    _postRepository = new PostRepository(EnsureConnection());
                    if (_transaction != null) _postRepository.SetTransaction(_transaction);
                }
                return _postRepository;
            }
        }

        public CommentRepository Comments
        {
            get
            {
                if (_commentRepository == null)
                {
                    _commentRepository = new CommentRepository(EnsureConnection());
                    if (_transaction != null) _commentRepository.SetTransaction(_transaction);
                }
                return _commentRepository;
            }
        }

        public PostFileRepository PostFiles
        {
            get
            {
                if (_postFileRepository == null)
                {
                    _postFileRepository = new PostFileRepository(EnsureConnection());
                    if (_transaction != null) _postFileRepository.SetTransaction(_transaction);
                }
                return _postFileRepository;
            }
        }

        #endregion

        #region Transaction Management

        /// <summary>공유 연결을 한 번만 만들고 PRAGMA(WAL·foreign_keys 등)를 적용한다.</summary>
        private SqliteConnection EnsureConnection()
        {
            if (_connection == null || _connection.State != ConnectionState.Open)
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
                // foreign_keys=ON 필수: CASCADE 삭제가 실제 동작하도록 (per-connection 설정)
                cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON; PRAGMA temp_store=MEMORY; PRAGMA busy_timeout=5000; PRAGMA cache_size=10000; PRAGMA mmap_size=30000000;";
                cmd.ExecuteNonQuery();
            }
            return _connection;
        }

        /// <summary>
        /// 트랜잭션 시작 (이미 생성된 Repository 전체에 적용 — 단일 연결이라 원자적).
        /// </summary>
        public void BeginTransaction()
        {
            var conn = EnsureConnection();
            _transaction?.Dispose();
            _transaction = conn.BeginTransaction();

            _postRepository?.SetTransaction(_transaction);
            _commentRepository?.SetTransaction(_transaction);
            _postFileRepository?.SetTransaction(_transaction);
        }

        /// <summary>
        /// 트랜잭션 커밋
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
        /// 트랜잭션 롤백
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
            // 각 Repository 가 들고 있던 트랜잭션 참조도 해제
            _postRepository?.SetTransaction(null);
            _commentRepository?.SetTransaction(null);
            _postFileRepository?.SetTransaction(null);
        }

        /// <summary>
        /// 트랜잭션 내에서 작업 실행 (비동기)
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

        /// <summary>
        /// 트랜잭션 내에서 작업 실행 (비동기, 반환값 있음)
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
                    _transaction?.Dispose();

                    // Repository 들은 공유 연결을 소유하지 않으므로(_ownsConnection=false) 연결을 닫지 않는다.
                    _postRepository?.Dispose();
                    _commentRepository?.Dispose();
                    _postFileRepository?.Dispose();

                    // 연결은 UnitOfWork 가 한 번만 닫는다.
                    _connection?.Close();
                    _connection?.Dispose();
                }
                _disposed = true;
            }
        }

        #endregion
    }
}

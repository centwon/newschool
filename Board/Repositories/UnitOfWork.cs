using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace NewSchool.Board.Repositories
{
    /// <summary>
    /// Unit of Work 패턴 - 여러 Repository의 트랜잭션을 통합 관리
    /// </summary>
    public class UnitOfWork : IDisposable
    {
        private readonly string _dbPath;
        private SqliteConnection? _connection;
        private SqliteTransaction? _transaction;
        private bool _disposed;

        // Repositories
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
                    EnsureConnection();
                    _postRepository = new PostRepository(_dbPath);

                    // ✅ 트랜잭션이 있으면 설정
                    if (_transaction != null)
                    {
                        _postRepository.SetTransaction(_transaction);
                    }
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
                    EnsureConnection();
                    _commentRepository = new CommentRepository(_dbPath);

                    // ✅ 트랜잭션이 있으면 설정
                    if (_transaction != null)
                    {
                        _commentRepository.SetTransaction(_transaction);
                    }
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
                    EnsureConnection();
                    _postFileRepository = new PostFileRepository(_dbPath);

                    // ✅ 트랜잭션이 있으면 설정
                    if (_transaction != null)
                    {
                        _postFileRepository.SetTransaction(_transaction);
                    }
                }
                return _postFileRepository;
            }
        }

        #endregion

        #region Transaction Management

        private void EnsureConnection()
        {
            if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
            {
                _connection = new SqliteConnection($"Data Source={_dbPath}");
                _connection.Open();
            }
        }

        /// <summary>
        /// 트랜잭션 시작
        /// </summary>
        public void BeginTransaction()
        {
            // ✅ 첫 번째 Repository 생성 (Connection 확보)
            if (_postRepository == null)
            {
                _ = Posts;  // Property getter 호출로 Repository 생성
            }

            _transaction?.Dispose();

            // ✅ 첫 Repository에서 트랜잭션 시작
            if (_postRepository != null)
            {
                _postRepository.BeginTransaction();
                _transaction = _postRepository.GetTransaction();  // ⭐ getter 사용
                _connection = _postRepository.GetConnection();    // ⭐ getter 사용
            }

            // ✅ 이미 생성된 다른 Repository들에도 트랜잭션 설정
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
                _transaction?.Dispose();
                _transaction = null;
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
                _transaction?.Dispose();
                _transaction = null;
            }
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

                    _postRepository?.Dispose();
                    _commentRepository?.Dispose();
                    _postFileRepository?.Dispose();

                    _connection?.Close();
                    _connection?.Dispose();
                }
                _disposed = true;
            }
        }

        #endregion
    }
}

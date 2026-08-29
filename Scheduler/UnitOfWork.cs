using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.Scheduler.Repositories;
using NewSchool.Services;

namespace NewSchool.Scheduler;

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

    private KEventRepository?          _kevents;

    // 학사일정 리포지토리(Schedules)는 여기서 걷어냈다(2026-08-30).
    //
    // 아무도 이 속성을 쓰지 않았다. 그런데 SchoolScheduleRepository 는 스스로
    // ExecuteInTransactionAsync 를 쓰므로(CreateBulkAsync/DeleteBulkAsync), 여기 두면
    // "UnitOfWork 의 공유 트랜잭션 안에서 리포지토리가 자기 트랜잭션을 여는" 조합이
    // 만들어진다. 그건 앞선 작업을 조용히 버리는 길이다
    // (BaseRepository.BeginTransaction 주석 참고). 쓰지도 않는 속성으로 그 길을 열어
    // 둘 이유가 없다.
    //
    // 학사일정은 SchoolScheduleService 가 자기 리포지토리로 직접 다룬다.

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
                // Cache=Shared 를 쓰지 않는다(기본값 Private). WAL 위에 공유 캐시를 얹으면
                // 같은 프로세스의 연결들 사이에 테이블 락이 생겨 WAL 이 주는 읽기/쓰기 동시성이
                // 깎이고, 그 락은 SQLITE_BUSY 가 아니라 SQLITE_LOCKED 로 즉시 실패해
                // 아래 busy_timeout 도 듣지 않는다.
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
    /// 단일 트랜잭션 시작 — 모든 Repository 가 공유 연결을 쓰므로 KEvents 에 원자적으로 적용.
    /// </summary>
    public void BeginTransaction()
    {
        var conn = EnsureConnection();
        _transaction?.Dispose();
        _transaction = conn.BeginTransaction();

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

            _kevents?.Dispose();

            _connection?.Close();
            _connection?.Dispose();
            _disposed = true;
        }
    }
}

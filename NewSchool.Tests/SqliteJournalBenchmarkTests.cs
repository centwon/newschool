using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;

namespace NewSchool.Tests;

/// <summary>
/// SQLite journal_mode/synchronous 조합별 쓰기 성능 벤치마크.
/// WAL+NORMAL(앱 설정) vs DELETE+FULL(기본값) 등을 비교해, WAL 의 성능 기여도를 수치로 확인한다.
///
/// 타이밍 측정은 환경·부하에 민감하므로 일반 테스트 실행에는 포함하지 않는다(즉시 통과).
/// 실제 측정하려면 환경변수를 켜고 실행:
///   PowerShell:  $env:RUN_SQLITE_BENCH=1; dotnet test -p:Platform=x64 --filter FullyQualifiedName~SqliteJournalBenchmark
/// </summary>
public class SqliteJournalBenchmarkTests
{
    private readonly ITestOutputHelper _out;
    public SqliteJournalBenchmarkTests(ITestOutputHelper output) => _out = output;

    private static bool Enabled => Environment.GetEnvironmentVariable("RUN_SQLITE_BENCH") == "1";

    // 자동커밋(커밋마다 fsync 노출)은 느리므로 작게, 단일 트랜잭션은 크게
    private const int AutoCommitRows = 1_000;
    private const int SingleTxRows = 50_000;
    private const int Repeat = 3; // 각 측정을 여러 번 돌려 최소값 채택(노이즈 완화)

    [Fact]
    [Trait("Category", "Benchmark")]
    public void Benchmark_WAL_vs_Rollback_쓰기성능()
    {
        if (!Enabled)
        {
            _out.WriteLine("벤치마크 건너뜀. 실행하려면 환경변수 RUN_SQLITE_BENCH=1 설정 후 다시 실행하세요.");
            return;
        }

        var configs = new (string Journal, string Sync)[]
        {
            ("WAL",    "NORMAL"), // 현재 앱 설정
            ("WAL",    "FULL"),   // WAL 유지 + 커밋마다 sync
            ("DELETE", "NORMAL"), // 롤백 저널 + sync 완화
            ("DELETE", "FULL"),   // SQLite 기본값(이전 상태)
        };

        _out.WriteLine($"=== 자동커밋 {AutoCommitRows:N0}건 (커밋마다 1건) — 최소 {Repeat}회 ===");
        _out.WriteLine($"{"journal",-8} {"sync",-7} {"min ms",10} {"rows/s",12}");
        foreach (var c in configs)
        {
            double ms = MeasureMin(c.Journal, c.Sync, useSingleTransaction: false);
            _out.WriteLine($"{c.Journal,-8} {c.Sync,-7} {ms,10:F1} {AutoCommitRows / (ms / 1000.0),12:N0}");
        }

        _out.WriteLine("");
        _out.WriteLine($"=== 단일 트랜잭션 {SingleTxRows:N0}건 (1회 커밋) — 최소 {Repeat}회 ===");
        _out.WriteLine($"{"journal",-8} {"sync",-7} {"min ms",10} {"rows/s",12}");
        foreach (var c in configs)
        {
            double ms = MeasureMin(c.Journal, c.Sync, useSingleTransaction: true);
            _out.WriteLine($"{c.Journal,-8} {c.Sync,-7} {ms,10:F1} {SingleTxRows / (ms / 1000.0),12:N0}");
        }
    }

    private double MeasureMin(string journal, string sync, bool useSingleTransaction)
    {
        double min = double.MaxValue;
        for (int i = 0; i < Repeat; i++)
        {
            double ms = MeasureOnce(journal, sync, useSingleTransaction);
            if (ms < min) min = ms;
        }
        return min;
    }

    private double MeasureOnce(string journal, string sync, bool useSingleTransaction)
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"bench_{Guid.NewGuid():N}.db");
        int rows = useSingleTransaction ? SingleTxRows : AutoCommitRows;

        try
        {
            // Pooling=false: 닫을 때 실제로 파일 핸들을 놓아 정리/격리를 확실히 함
            var cs = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            }.ToString();

            using var conn = new SqliteConnection(cs);
            conn.Open();

            Exec(conn, $"PRAGMA journal_mode={journal};");
            Exec(conn, $"PRAGMA synchronous={sync};");
            Exec(conn, "CREATE TABLE t (id INTEGER PRIMARY KEY, a TEXT, b INTEGER);");

            var sw = Stopwatch.StartNew();

            if (useSingleTransaction)
            {
                using var tx = conn.BeginTransaction();
                InsertRows(conn, tx, rows);
                tx.Commit();
            }
            else
            {
                // 명시적 트랜잭션 없이 → 각 INSERT 가 개별 커밋(= 커밋마다 fsync 노출)
                InsertRows(conn, null, rows);
            }

            sw.Stop();
            return sw.Elapsed.TotalMilliseconds;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDelete(dbPath);
            TryDelete(dbPath + "-wal");
            TryDelete(dbPath + "-shm");
            TryDelete(dbPath + "-journal");
        }
    }

    private static void InsertRows(SqliteConnection conn, SqliteTransaction? tx, int rows)
    {
        using var cmd = conn.CreateCommand();
        if (tx != null) cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO t (a, b) VALUES (@a, @b);";
        var pa = cmd.CreateParameter(); pa.ParameterName = "@a"; cmd.Parameters.Add(pa);
        var pb = cmd.CreateParameter(); pb.ParameterName = "@b"; cmd.Parameters.Add(pb);

        for (int i = 0; i < rows; i++)
        {
            pa.Value = $"row-{i}";
            pb.Value = i;
            cmd.ExecuteNonQuery();
        }
    }

    private static void Exec(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* 무시 */ }
    }
}

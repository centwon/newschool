using System;
using System.IO;
using Microsoft.Data.Sqlite;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 백업 스냅샷(Settings.TrySnapshotDb)이 의존하는 SQLite 동작 검증 —
/// "VACUUM INTO @param" 파라미터 바인딩이 현재 SQLitePCL 번들에서 동작하는지,
/// 스냅샷에 WAL 미체크포인트 커밋까지 포함되는지 회귀 확인.
/// </summary>
public class BackupSnapshotTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ns_snapshot_test_{Guid.NewGuid():N}");

    public BackupSnapshotTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { /* 임시 폴더 정리 실패 무시 */ }
    }

    [Fact]
    public void VacuumInto_파라미터_바인딩으로_스냅샷_생성()
    {
        var dbPath = Path.Combine(_dir, "src.db");
        var snapshotPath = Path.Combine(_dir, "snapshot.db");

        // WAL 모드 원본 DB에 데이터 기록 (체크포인트 없이 -wal 에만 있는 상태 재현)
        using (var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                PRAGMA journal_mode=WAL;
                CREATE TABLE T (Id INTEGER PRIMARY KEY, Name TEXT);
                INSERT INTO T (Name) VALUES ('가나다'), ('라마바');";
            cmd.ExecuteNonQuery();

            // 원본 연결이 열려 있는 상태에서 읽기 전용 연결로 VACUUM INTO (Settings.TrySnapshotDb 와 동일)
            using var readConn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;Pooling=False");
            readConn.Open();
            using var vacuum = readConn.CreateCommand();
            vacuum.CommandText = "VACUUM INTO @Target;";
            vacuum.Parameters.AddWithValue("@Target", snapshotPath);
            vacuum.ExecuteNonQuery();
        }

        // 스냅샷은 독립 파일로 열리고 WAL 에만 있던 데이터까지 포함해야 함
        Assert.True(File.Exists(snapshotPath));
        using var check = new SqliteConnection($"Data Source={snapshotPath};Mode=ReadOnly;Pooling=False");
        check.Open();
        using var count = check.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM T;";
        Assert.Equal(2L, count.ExecuteScalar());
    }
}

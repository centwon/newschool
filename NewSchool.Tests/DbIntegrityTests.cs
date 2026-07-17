using System;
using System.IO;
using Microsoft.Data.Sqlite;
using NewSchool.Helpers;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// DbIntegrity.FindCorrupt — 시작 시 손상 게이트의 판정 로직.
/// 정상 DB 통과 / 쓰레기 파일·잘린 DB 검출 / 미존재 파일 스킵.
/// </summary>
public sealed class DbIntegrityTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"dbintegrity_{Guid.NewGuid():N}");

    public DbIntegrityTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { /* 정리 실패 무시 */ }
    }

    private string CreateHealthyDb(string name)
    {
        var path = Path.Combine(_dir, name);
        using var con = new SqliteConnection($"Data Source={path}");
        con.Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "CREATE TABLE T(Id INTEGER PRIMARY KEY, V TEXT); INSERT INTO T(V) VALUES ('x');";
        cmd.ExecuteNonQuery();
        return path;
    }

    [Fact]
    public void FindCorrupt_HealthyDb_ReturnsEmpty()
    {
        var path = CreateHealthyDb("ok.db");
        SqliteConnection.ClearAllPools();

        var corrupt = DbIntegrity.FindCorrupt(new[] { path });

        Assert.Empty(corrupt);
    }

    [Fact]
    public void FindCorrupt_GarbageFile_Detected()
    {
        // SQLite 형식이 아닌 파일 (NOTADB 케이스)
        var path = Path.Combine(_dir, "garbage.db");
        File.WriteAllText(path, "this is not a sqlite database at all — just text padding to exceed header size");

        var corrupt = DbIntegrity.FindCorrupt(new[] { path });

        Assert.Contains("garbage.db", corrupt);
    }

    [Fact]
    public void FindCorrupt_TruncatedDb_Detected()
    {
        // 정상 DB 를 만든 뒤 중간을 잘라 malformed 재현
        var path = CreateHealthyDb("truncated.db");
        SqliteConnection.ClearAllPools();
        var bytes = File.ReadAllBytes(path);
        File.WriteAllBytes(path, bytes[..(bytes.Length / 3)]);

        var corrupt = DbIntegrity.FindCorrupt(new[] { path });

        Assert.Contains("truncated.db", corrupt);
    }

    [Fact]
    public void FindCorrupt_MissingFile_Skipped()
    {
        // 신규 설치(파일 없음)는 손상이 아니다
        var corrupt = DbIntegrity.FindCorrupt(new[]
        {
            Path.Combine(_dir, "does_not_exist.db"),
            string.Empty,
        });

        Assert.Empty(corrupt);
    }

    [Fact]
    public void FindCorrupt_ExclusivelyLockedFile_NotFlagged()
    {
        // 백신·다른 프로세스가 파일을 독점 점유한 경우 — 열기 실패는 손상이 아니다
        var path = CreateHealthyDb("locked.db");
        SqliteConnection.ClearAllPools();
        using var _ = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var corrupt = DbIntegrity.FindCorrupt(new[] { path });

        Assert.Empty(corrupt);
    }

    [Fact]
    public void FindCorrupt_ReadOnlyFile_NotFlagged()
    {
        // 읽기 전용 속성 파일 — ReadWrite 열기 실패는 손상이 아니다
        var path = CreateHealthyDb("readonly.db");
        SqliteConnection.ClearAllPools();
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            var corrupt = DbIntegrity.FindCorrupt(new[] { path });

            Assert.Empty(corrupt);
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal); // Dispose 의 폴더 삭제가 실패하지 않도록
        }
    }

    [Fact]
    public void FindCorrupt_MixedSet_ReportsOnlyCorrupt()
    {
        var ok = CreateHealthyDb("mixed_ok.db");
        SqliteConnection.ClearAllPools();
        var bad = Path.Combine(_dir, "mixed_bad.db");
        File.WriteAllText(bad, "garbage garbage garbage garbage garbage garbage garbage garbage garbage");

        var corrupt = DbIntegrity.FindCorrupt(new[] { ok, bad });

        Assert.Single(corrupt);
        Assert.Contains("mixed_bad.db", corrupt);
    }
}

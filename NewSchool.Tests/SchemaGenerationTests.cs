using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Database;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// <b>옛 판이 만든 자료 파일로 새 판을 열 때</b> 무슨 일이 일어나는가 — 51차(스키마 세대 축).
///
/// <para>세대 표식은 <c>PRAGMA user_version</c> 하나뿐이라, 그 숫자를 잘못 다루면 되돌릴
/// 방법이 없다. 특히 <b>내려 찍는 것</b>이 위험하다 — 새 판으로 쓰던 자료를 옛 판으로 한 번
/// 열었을 뿐인데 표식이 낮아지면, 이미 끝난 변환이 다시 돌고 "이 파일은 더 새 판에서 왔다"
/// 는 사실이 지워진다.</para>
/// </summary>
public sealed class SchemaGenerationTests : IDisposable
{
    private readonly string _dbPath;

    static SchemaGenerationTests() => SQLitePCL.Batteries_V2.Init();

    public SchemaGenerationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "NewSchoolTests", $"gen_{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch
        {
            // 임시 폴더의 잔존 파일은 무해
        }
        GC.SuppressFinalize(this);
    }

    private async Task<bool> InitAsync()
    {
        using var initializer = new DatabaseInitializer(_dbPath);
        bool ok = await initializer.InitializeAsync();
        SqliteConnection.ClearAllPools();
        return ok;
    }

    private async Task ExecAsync(string sql)
    {
        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync();
        }
        SqliteConnection.ClearAllPools();
    }

    private async Task<T> ScalarAsync<T>(string sql)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        object? value = await cmd.ExecuteScalarAsync();
        return (T)Convert.ChangeType(value ?? 0, typeof(T));
    }

    /// <summary>빈 파일에서 시작해도 그 자리에서 최신 세대가 된다.</summary>
    [Fact]
    public async Task 새_자료_파일은_최신_세대로_찍힌다()
    {
        Assert.True(await InitAsync());

        Assert.Equal(DatabaseInitializer.SchemaVersion, await ScalarAsync<int>("PRAGMA user_version"));
    }

    /// <summary>
    /// 세대 1(= v1.0.0 출시 모양)에는 수업 일지 표가 남아 있었다. 게시판 한 곳으로 모으면서
    /// 읽는 코드가 사라졌으므로 변환 2 가 그것을 걷어 내야 한다.
    /// </summary>
    [Fact]
    public async Task 옛_세대의_버려진_표는_변환이_걷어_낸다()
    {
        Assert.True(await InitAsync());

        // 세대 1 로 되돌리고, 그때 있던 표를 되살린다.
        await ExecAsync("""
            CREATE TABLE IF NOT EXISTS LessonLog (No INTEGER PRIMARY KEY, Memo TEXT);
            INSERT INTO LessonLog (Memo) VALUES ('옛 판이 남긴 줄');
            PRAGMA user_version = 1;
            """);

        Assert.True(await InitAsync());

        Assert.Equal(0, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='LessonLog'"));
        Assert.Equal(DatabaseInitializer.SchemaVersion, await ScalarAsync<int>("PRAGMA user_version"));
    }

    /// <summary>
    /// <b>이 축의 핵심.</b> 더 새 판에서 만든 자료를 옛 판이 열어도 세대를 낮추지 않는다.
    /// 예전에는 조건 없이 대입해서, 한 번 열었을 뿐인데 표식이 내려앉았다.
    /// </summary>
    [Fact]
    public async Task 더_새로운_자료_파일의_세대를_낮춰_찍지_않는다()
    {
        Assert.True(await InitAsync());

        int future = DatabaseInitializer.SchemaVersion + 5;
        await ExecAsync($"PRAGMA user_version = {future};");

        Assert.True(await InitAsync());   // 열리기는 해야 한다 — 자기 자료를 못 보게 막지 않는다

        Assert.Equal(future, await ScalarAsync<int>("PRAGMA user_version"));
    }

    /// <summary>
    /// 더 새로운 파일에는 변환도 걸지 않는다 — 새 판이 만든 모양을 옛 규칙으로 건드리게 된다.
    /// (세대 1 의 표를 일부러 남겨 두고, 표식만 미래로 올려 확인한다.)
    /// </summary>
    [Fact]
    public async Task 더_새로운_자료_파일에는_변환을_걸지_않는다()
    {
        Assert.True(await InitAsync());

        await ExecAsync($"""
            CREATE TABLE IF NOT EXISTS LessonLog (No INTEGER PRIMARY KEY, Memo TEXT);
            PRAGMA user_version = {DatabaseInitializer.SchemaVersion + 1};
            """);

        Assert.True(await InitAsync());

        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='LessonLog'"));
    }

    /// <summary>
    /// 표를 통째로 잃어버린 파일(옛 판이 아직 안 만들던 표)은 초기화가 다시 만든다 —
    /// <c>CREATE TABLE IF NOT EXISTS</c> 라서 <b>표</b>는 이렇게 되살아난다.
    /// (<b>칸</b>은 그렇지 않다 — 그래서 <c>SchemaFingerprintTests</c> 가 따로 있다.)
    /// </summary>
    [Fact]
    public async Task 없어진_표는_초기화가_다시_만든다()
    {
        Assert.True(await InitAsync());
        await ExecAsync("DROP TABLE StudentLogFile;");

        Assert.True(await InitAsync());

        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='StudentLogFile'"));
    }
}

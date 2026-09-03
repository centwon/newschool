using System;
using System.IO;
using Microsoft.Data.Sqlite;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 복원이 <b>덮어쓰기 전에 무엇을 확인하는가</b> — 48차(백업·복원 축)에서 세운 두 문지기.
///
/// <para>둘 다 "복원했다" 는 말이 사실이 아니게 되는 길을 막는다. 하나는 <b>아무것도 되돌리지
/// 않고</b> 성공으로 끝나던 길이고(엉뚱한 ZIP·한 겹 더 압축된 폴더·빈 폴더), 다른 하나는
/// <b>엉뚱한 파일로</b> 설정 DB 를 덮어쓰던 길이다.</para>
///
/// <para>파일만 들여다보는 판정이라 실제 데이터 폴더를 건드리지 않는다 — 그래서 테스트가
/// 이 두 함수를 직접 부른다(전체 복원은 사용자의 진짜 DB 를 갈아 끼우므로 여기서 부르지 않는다).</para>
/// </summary>
public sealed class RestoreGuardTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), $"newschool_restoreguard_{Guid.NewGuid():N}");

    public RestoreGuardTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }
        catch { /* 임시 폴더 정리 실패 무시 */ }
    }

    private string MakeDb(string fileName, string tableName)
    {
        var path = Path.Combine(_dir, fileName);
        using var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"CREATE TABLE {tableName} (Key TEXT PRIMARY KEY, Value TEXT NOT NULL);";
        cmd.ExecuteNonQuery();
        SqliteConnection.ClearAllPools();   // 파일을 쥔 채로 남지 않게
        return path;
    }

    #region 되돌릴 것이 있는가

    /// <summary><b>규칙: 빈 폴더는 백업이 아니다.</b> 예전에는 이것도 "복원 완료" 였다.</summary>
    [Fact]
    public void 빈_폴더에는_되돌릴_것이_없다()
        => Assert.False(Settings.ContainsRestorableDb(_dir));

    /// <summary>
    /// 아는 이름이 하나도 없는 폴더도 마찬가지다 — 엉뚱한 ZIP 을 풀었거나,
    /// 백업을 손으로 다시 압축해 폴더가 한 겹 더 생긴 경우가 여기 걸린다.
    /// </summary>
    [Fact]
    public void 아는_DB_가_없는_폴더에는_되돌릴_것이_없다()
    {
        File.WriteAllText(Path.Combine(_dir, "메모.txt"), "백업이 아니다");
        MakeDb("something-else.db", "Whatever");

        Assert.False(Settings.ContainsRestorableDb(_dir));
    }

    /// <summary>
    /// 반대로 <b>하나라도</b> 있으면 복원 대상이다 — 백업 시점에 없던 DB 는 백업에도 없으므로
    /// (예: 게시판을 한 번도 안 쓴 사용자) 네 개가 다 있어야 한다고 하면 멀쩡한 백업이 막힌다.
    /// </summary>
    [Theory]
    [InlineData("Settings.db")]
    [InlineData("school.db")]
    [InlineData("board.db")]
    [InlineData("scheduler.db")]
    public void 아는_DB_가_하나라도_있으면_복원_대상이다(string fileName)
    {
        File.WriteAllText(Path.Combine(_dir, fileName), "");

        Assert.True(Settings.ContainsRestorableDb(_dir));
    }

    #endregion

    #region 이것이 설정 DB 인가

    /// <summary>Settings 테이블이 있으면 설정 DB 다.</summary>
    [Fact]
    public void Settings_테이블이_있으면_설정_DB_다()
        => Assert.True(SettingsDb.LooksLikeSettingsDb(MakeDb("Settings.db", "Settings")));

    /// <summary>
    /// <b>규칙: 다른 DB 를 골랐으면 덮어쓰지 않는다.</b> 사용자가 [데이터 폴더 열기] 로 보는
    /// 폴더에는 school.db·board.db·scheduler.db 가 나란히 있고, 파일 선택기가 .db 를 열어 준다.
    /// 예전에는 이 중 하나를 골라도 설정 DB 위에 그대로 복사한 뒤 "복원 완료" 라고 말했다.
    /// </summary>
    [Fact]
    public void 다른_테이블뿐인_DB_는_설정_DB_가_아니다()
        => Assert.False(SettingsDb.LooksLikeSettingsDb(MakeDb("school.db", "Student")));

    /// <summary>SQLite 가 아예 아닌 파일도 막는다(이름만 .db 인 경우).</summary>
    [Fact]
    public void SQLite_가_아닌_파일은_설정_DB_가_아니다()
    {
        var fake = Path.Combine(_dir, "fake.db");
        File.WriteAllText(fake, "이건 그냥 텍스트다");

        Assert.False(SettingsDb.LooksLikeSettingsDb(fake));
    }

    /// <summary>없는 파일도 조용히 false — 예외로 새어 나가면 복원 대화상자가 터진다.</summary>
    [Fact]
    public void 없는_파일은_설정_DB_가_아니다()
        => Assert.False(SettingsDb.LooksLikeSettingsDb(Path.Combine(_dir, "없는파일.db")));

    #endregion
}

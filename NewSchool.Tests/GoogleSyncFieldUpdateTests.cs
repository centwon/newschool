using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Scheduler;
using NewSchool.Scheduler.Repositories;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 구글 업로드 후 식별자 되쓰기 회귀 테스트.
///
/// 업로드는 대화상자를 닫은 뒤 배경에서 돈다(그래야 저장 버튼이 네트워크를 기다리며
/// 멈춰 있지 않다). 그런데 끝날 때 전체 행을 쓰는 <c>UpdateAsync</c> 로 되돌려 쓰면,
/// 그동안 사용자가 같은 항목을 수정한 내용이 조용히 사라진다.
/// 그래서 <c>GoogleId</c>·<c>Updated</c> 두 열만 갱신해야 한다.
/// </summary>
public class GoogleSyncFieldUpdateTests : IAsyncLifetime
{
    private string _dbPath = string.Empty;

    public async Task InitializeAsync()
    {
        SQLitePCL.Batteries_V2.Init();

        _dbPath = Path.Combine(Path.GetTempPath(), "NewSchoolTests", $"sched_{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);

        using var init = new NewSchool.Scheduler.DatabaseInitializer(_dbPath);
        await init.InitializeAsync();
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { /* 임시 파일 — OS 가 정리 */ }
        return Task.CompletedTask;
    }

    private static KEvent NewEvent(string title) => new()
    {
        Title = title,
        Notes = "원본 메모",
        CalendarId = 1,
        Start = new DateTime(2026, 3, 2, 9, 0, 0),
        End = new DateTime(2026, 3, 2, 10, 0, 0),
        ItemType = "event",
        Status = "confirmed",
    };

    [Fact]
    public async Task 식별자만_갱신하고_다른_열은_건드리지_않는다()
    {
        using var repo = new KEventRepository(_dbPath);

        int no = await repo.CreateAsync(NewEvent("원래 제목"));
        Assert.True(no > 0);

        // 업로드가 도는 동안 사용자가 제목·메모를 고쳤다고 가정
        var edited = await repo.GetByIdAsync(no);
        Assert.NotNull(edited);
        edited!.Title = "사용자가 고친 제목";
        edited.Notes = "사용자가 고친 메모";
        Assert.True(await repo.UpdateAsync(edited));

        // 뒤늦게 끝난 업로드가 식별자를 되써 넣는다
        Assert.True(await repo.UpdateGoogleSyncFieldsAsync(no, "google-event-id", "2026-03-02T00:00:00.000Z"));

        var after = await repo.GetByIdAsync(no);
        Assert.NotNull(after);

        // 식별자는 반영되고
        Assert.Equal("google-event-id", after!.GoogleId);
        Assert.Equal("2026-03-02T00:00:00.000Z", after.Updated);

        // 사용자의 편집은 살아 있어야 한다 (전체 행을 덮어썼다면 "원래 제목"으로 돌아간다)
        Assert.Equal("사용자가 고친 제목", after.Title);
        Assert.Equal("사용자가 고친 메모", after.Notes);
    }

    [Fact]
    public async Task 없는_항목이면_false()
    {
        using var repo = new KEventRepository(_dbPath);
        Assert.False(await repo.UpdateGoogleSyncFieldsAsync(999_999, "x", "y"));
    }
}

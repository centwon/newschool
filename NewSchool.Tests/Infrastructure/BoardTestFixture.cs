using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Xunit;
using BoardInitializer = NewSchool.Board.DatabaseInitializer;

namespace NewSchool.Tests.Infrastructure;

/// <summary>
/// 게시판(board.db) 전용 임시 DB 픽스처.
/// Post/Comment/PostFile 스키마는 <see cref="NewSchool.Board.DatabaseInitializer"/>(internal,
/// InternalsVisibleTo 로 접근)가 생성한다. FK 시드가 필요 없어 school.db 픽스처보다 단순하다.
/// </summary>
public sealed class BoardTestFixture : IAsyncLifetime
{
    static BoardTestFixture()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    public string DbPath { get; } = Path.Combine(
        Path.GetTempPath(), "NewSchoolTests", $"board_{Guid.NewGuid():N}.db");

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);

        using var initializer = new BoardInitializer(DbPath);
        bool ok = await initializer.InitializeAsync();
        if (!ok)
            throw new InvalidOperationException($"테스트 board.db 초기화 실패: {DbPath}");
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(DbPath)) File.Delete(DbPath);
        }
        catch
        {
            // 임시 폴더의 잔존 파일은 무해
        }
        return Task.CompletedTask;
    }
}

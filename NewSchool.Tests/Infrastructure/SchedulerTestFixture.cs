using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Xunit;
using SchedulerInitializer = NewSchool.Scheduler.DatabaseInitializer;

namespace NewSchool.Tests.Infrastructure;

/// <summary>
/// 스케줄러(scheduler.db) 전용 임시 DB 픽스처.
///
/// <para>KEvent/KCalendarList 스키마는 <see cref="NewSchool.Scheduler.DatabaseInitializer"/>
/// (internal, InternalsVisibleTo 로 접근)가 만든다. 초기화가 기본 캘린더 4개(수업·학급·업무·개인)를
/// 함께 심으므로, 테스트는 그 캘린더들을 그대로 쓸 수 있다.</para>
///
/// <para>게시판에는 <c>BoardTestFixture</c> 가 있었지만 스케줄러 쪽에는 DB 픽스처가 없어
/// 리포지토리·서비스 왕복이 하나도 검증되지 않았다(2026-08-31 감사).</para>
/// </summary>
public sealed class SchedulerTestFixture : IAsyncLifetime
{
    static SchedulerTestFixture()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    public string DbPath { get; } = Path.Combine(
        Path.GetTempPath(), "NewSchoolTests", $"scheduler_{Guid.NewGuid():N}.db");

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);

        using var initializer = new SchedulerInitializer(DbPath);
        bool ok = await initializer.InitializeAsync();
        if (!ok)
            throw new InvalidOperationException($"테스트 scheduler.db 초기화 실패: {DbPath}");
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

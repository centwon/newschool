using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.Tests.Infrastructure;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 스키마 소유권 회귀 테스트 — 전수 조사 21차 1단계.
///
/// 예전에는 Schedule·CourseSection·SubjectYearPlan 등 8개 테이블이 각자의 리포지토리
/// 생성자에서만 만들어졌다. 이들 중 일부는 다른 테이블의 FK 부모라, 부모 리포지토리를
/// 한 번도 만들지 않은 상태에서 자식 테이블에 쓰면 <c>no such table</c> 로 실패했다.
/// 앱에서는 화면들이 우연히 부모를 먼저 열어서 가려져 있었을 뿐이다.
///
/// 이 픽스처는 <see cref="SqliteTestFixture"/> 가 DatabaseInitializer 만 돌린 DB 이므로,
/// 아래 테스트들은 "초기화기만으로 스키마가 완비되는가" 를 그대로 검증한다.
/// </summary>
public class SchemaOwnershipTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _fx;

    public SchemaOwnershipTests(SqliteTestFixture fx) => _fx = fx;

    private async Task<List<string>> TableNamesAsync()
    {
        using var conn = new SqliteConnection($"Data Source={_fx.DbPath}");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
        var names = new List<string>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            names.Add(reader.GetString(0));
        return names;
    }

    [Fact]
    public async Task 초기화_후_스키마_버전이_찍힌다()
    {
        using var conn = new SqliteConnection($"Data Source={_fx.DbPath}");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA user_version";
        var version = Convert.ToInt32(await cmd.ExecuteScalarAsync());

        Assert.Equal(NewSchool.Database.DatabaseInitializer.SchemaVersion, version);
        Assert.True(version >= 1);
    }

    [Theory]
    [InlineData("CourseSection")]
    [InlineData("Schedule")]
    [InlineData("ScheduleUnitMap")]
    [InlineData("LessonProgress")]
    [InlineData("SubjectYearPlan")]
    [InlineData("WeeklyLessonHours")]
    [InlineData("WeeklyUnitPlan")]
    [InlineData("UndoHistory")]
    public async Task 초기화기만으로_리포지토리_소유_테이블이_만들어진다(string table)
    {
        var names = await TableNamesAsync();
        Assert.Contains(table, names);
    }

    /// <summary>
    /// 21차에서 실제로 밟았던 실패 재현 — ScheduleRepository 를 만들지 않은 채
    /// LessonProgress 를 갱신하면 FK 부모(Schedule) 부재로 터졌다.
    /// </summary>
    [Fact]
    public async Task 부모_리포지토리_없이도_LessonProgress_갱신이_동작한다()
    {
        using var repo = new LessonProgressRepository(_fx.DbPath);

        var ghost = new LessonProgress
        {
            No = 999_999,
            CourseSectionId = 1,
            Room = "2-3",
            ProgressType = ProgressType.Normal,
            UpdatedAt = DateTime.Now,
        };

        // 갱신 대상이 없으니 false — 예외 없이 여기까지 오는 것이 핵심
        Assert.False(await repo.UpdateAsync(ghost));
    }

    /// <summary>
    /// LessonLog 는 초기화기와 리포지토리가 서로 다른 정의를 갖고 있었다.
    /// 정본 하나로 합쳤으므로, 초기화기만 돈 DB 에도 리포지토리가 기대하는
    /// 확장 컬럼과 FK 가 모두 있어야 한다.
    /// </summary>
    [Fact]
    public async Task LessonLog_는_초기화기만으로_확장_컬럼과_FK_를_갖춘다()
    {
        using var conn = new SqliteConnection($"Data Source={_fx.DbPath}");
        await conn.OpenAsync();

        var columns = new List<string>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM pragma_table_info('LessonLog')";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                columns.Add(reader.GetString(0));
        }

        foreach (var expected in new[]
                 { "Grade", "Class", "CourseSectionNo", "SectionName", "Note", "CreatedAt", "UpdatedAt" })
        {
            Assert.Contains(expected, columns);
        }

        int fkCount;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM pragma_foreign_key_list('LessonLog')";
            fkCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        Assert.Equal(2, fkCount);
    }

    /// <summary>
    /// 리포지토리를 나중에 만들어도 스키마가 달라지지 않아야 한다
    /// (정의가 하나뿐이므로 CREATE 는 no-op, 마이그레이션은 멱등).
    /// </summary>
    [Fact]
    public async Task 리포지토리_생성_후에도_LessonLog_컬럼_집합이_그대로다()
    {
        async Task<List<string>> ColumnsAsync()
        {
            using var conn = new SqliteConnection($"Data Source={_fx.DbPath}");
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name FROM pragma_table_info('LessonLog') ORDER BY cid";
            var cols = new List<string>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                cols.Add(reader.GetString(0));
            return cols;
        }

        var before = await ColumnsAsync();

        using (var _ = new LessonLogRepository(_fx.DbPath)) { }

        Assert.Equal(before, await ColumnsAsync());
    }
}

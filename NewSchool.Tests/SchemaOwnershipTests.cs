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
/// 예전에는 Schedule·CourseSection 등 여러 테이블이 각자의 리포지토리
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

    private async Task<List<string>> ColumnsAsync(string table)
    {
        using var conn = new SqliteConnection($"Data Source={_fx.DbPath}");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT name FROM pragma_table_info('{table}') ORDER BY cid";
        var cols = new List<string>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            cols.Add(reader.GetString(0));
        return cols;
    }

    /// <summary>
    /// v1.0.0 을 첫 배포로 잡으면서 그 이전의 컬럼 추가 마이그레이션(ALTER TABLE)을 전부 없애고
    /// <c>CREATE TABLE</c> 정의에 접어 넣었다. 접어 넣기를 빠뜨리면 <b>새로 만드는 DB 에서만</b>
    /// 컬럼이 사라지는데, 기존 개발 DB 에는 남아 있어 눈치채기 어렵다.
    ///
    /// 그래서 예전에 ALTER 로만 붙던 컬럼들을 여기에 못박아 둔다.
    /// 초기화기만 돌린 DB 가 이 컬럼들을 모두 갖고 있어야 한다.
    /// </summary>
    [Theory]
    [InlineData("CourseSection", "LessonPlan,SectionType,IsPinned,PinnedDate,LearningObjective,MaterialPath,MaterialUrl,Memo")]
    [InlineData("ClassDiary", "CreatedAt,UpdatedAt")]
    [InlineData("LessonLog", "Grade,Class,CourseSectionNo,SectionName,Note,CreatedAt,UpdatedAt")]
    [InlineData("StudentSpecial", "Semester")]
    public async Task 마이그레이션으로_붙던_컬럼이_CREATE_TABLE_에_들어있다(string table, string expectedCsv)
    {
        var actual = await ColumnsAsync(table);
        Assert.NotEmpty(actual);

        foreach (var col in expectedCsv.Split(','))
            Assert.True(actual.Contains(col), $"{table}.{col} 컬럼이 없다 — CREATE TABLE 정의에서 빠졌다");
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
    public async Task 초기화기만으로_리포지토리_소유_테이블이_만들어진다(string table)
    {
        var names = await TableNamesAsync();
        Assert.Contains(table, names);
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

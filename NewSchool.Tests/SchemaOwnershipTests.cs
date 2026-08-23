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
    [InlineData("ClassDiary", "CreatedAt,UpdatedAt")]
    [InlineData("LessonChange", "CourseNo,SubjectText,Room,Memo")]
    [InlineData("CourseWeeklyHours", "Room,Week,PlannedHours")]
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
    [InlineData("LessonProgress")]
    [InlineData("CourseWeeklyHours")]
    [InlineData("LessonChange")]
    public async Task 초기화기만으로_리포지토리_소유_테이블이_만들어진다(string table)
    {
        var names = await TableNamesAsync();
        Assert.Contains(table, names);
    }

    /// <summary>
    /// 휴강은 <c>CourseNo</c> 가 NULL 인 행으로 남는다. 0 같은 특수값을 쓰면
    /// <c>Course(No)</c> 에 그런 행이 없어 FK 가 걸린다 — SQLite 는 NULL 인 FK 만 통과시킨다.
    /// </summary>
    [Fact]
    public async Task LessonChange_의_CourseNo_는_NULL_을_허용한다()
    {
        var notNull = await NotNullColumnsAsync("LessonChange");

        Assert.DoesNotContain("CourseNo", notNull);
        Assert.Contains("Date", notNull);
        Assert.Contains("Period", notNull);
    }

    private async Task<List<string>> NotNullColumnsAsync(string table)
    {
        using var conn = new SqliteConnection($"Data Source={_fx.DbPath}");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT name FROM pragma_table_info('{table}') WHERE [notnull] = 1";
        var cols = new List<string>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            cols.Add(reader.GetString(0));
        return cols;
    }

    /// <summary>
    /// 되살린 <c>LessonProgress</c> 는 사라진 <c>Schedule</c> 테이블을 가리키면 안 된다.
    /// 없는 부모를 가리키는 FK 는 <c>foreign_keys=ON</c> 에서 INSERT 를 준비 단계부터 막는다
    /// (그리고 그 실패는 "진도가 저장되지 않는다" 로만 보인다).
    /// </summary>
    [Fact]
    public async Task LessonProgress_는_사라진_Schedule_을_참조하지_않는다()
    {
        using var conn = new SqliteConnection($"Data Source={_fx.DbPath}");
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT \"table\" FROM pragma_foreign_key_list('LessonProgress')";

        var parents = new List<string>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            parents.Add(reader.GetString(0));

        Assert.Contains("CourseSection", parents);
        Assert.DoesNotContain("Schedule", parents);

        Assert.DoesNotContain("ScheduleId", await ColumnsAsync("LessonProgress"));
    }

    /// <summary>
    /// 주차별 시수는 <b>손으로 고친 칸만</b> 남긴다. 같은 (수업, 학급, 주차) 가 두 줄이면
    /// 어느 값이 진짜인지 알 수 없으므로 UNIQUE 로 막아 둔다(리포지토리의 UPSERT 도 여기 기댄다).
    ///
    /// <c>Room</c> 이 빠지면 같은 주차의 둘째 학급이 조용히 저장되지 않으므로 열 구성까지 못박는다.
    /// </summary>
    [Fact]
    public async Task CourseWeeklyHours_는_수업_학급_주차가_유일하다()
    {
        using var conn = new SqliteConnection($"Data Source={_fx.DbPath}");
        await conn.OpenAsync();

        string? uniqueIndex;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT name FROM pragma_index_list('CourseWeeklyHours') WHERE ""unique"" = 1";
            uniqueIndex = (await cmd.ExecuteScalarAsync())?.ToString();
        }

        Assert.False(string.IsNullOrEmpty(uniqueIndex));

        var columns = new List<string>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"SELECT name FROM pragma_index_info('{uniqueIndex}')";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                columns.Add(reader.GetString(0));
        }

        Assert.Contains("CourseNo", columns);
        Assert.Contains("Room", columns);
        Assert.Contains("Week", columns);
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Tests.Infrastructure;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// <b>표에 칸을 더하면 이 시험이 먼저 걸린다.</b> — 51차(스키마 세대 축).
///
/// <para>이 프로젝트는 <b>ALTER 마이그레이션을 두지 않기로</b> 했다(<c>Board.cs</c> 의 결정
/// 주석, 2026-08-25). 표 정의는 전부 <c>CREATE TABLE IF NOT EXISTS</c> 라서, 칸을 하나
/// 더하면 <b>새로 만드는 자료 파일에만</b> 생기고 이미 쓰던 파일에는 영영 생기지 않는다.
/// 그런데 조회 쿼리는 칸 이름을 직접 대므로, 쓰던 파일에서는 그 화면이
/// <c>no such column</c> 으로 통째로 실패한다.</para>
///
/// <para>그 대가를 알고 내린 결정이니 자동 변환을 넣지는 않는다. 대신 <b>모르고 지나가는
/// 일만 막는다</b> — 지문(<c>docs/schema.txt</c>)이 어긋나면 여기서 걸리고, 그때
/// 쓰던 파일에 넣을 <c>ALTER</c> 문을 그 문서에 함께 적으면 된다.</para>
///
/// <para>지문은 <b>실제로 초기화한 자료 파일</b>에서 뜬다 — 소스의 CREATE 문을 읽는 것이
/// 아니라 <c>pragma_table_info</c> 로 확인하므로, 리포지토리가 따로 만드는 표(51차 기준
/// 네 개)와 뷰까지 그대로 걸린다.</para>
/// </summary>
public class SchemaFingerprintTests : IClassFixture<SqliteTestFixture>,
                                      IClassFixture<BoardTestFixture>,
                                      IClassFixture<SchedulerTestFixture>
{
    private readonly SqliteTestFixture _school;
    private readonly BoardTestFixture _board;
    private readonly SchedulerTestFixture _scheduler;

    public SchemaFingerprintTests(SqliteTestFixture school, BoardTestFixture board, SchedulerTestFixture scheduler)
    {
        _school = school;
        _board = board;
        _scheduler = scheduler;
    }

    private const string Header = """
        # 자료 파일 스키마 지문 — 표마다 "파일:표=칸,칸,..." 한 줄.
        #
        # 이 파일은 손으로 고치는 것이 아니라, 스키마를 바꾼 뒤 시험이 일러 주는 대로 갱신한다.
        # 이 프로젝트는 ALTER 마이그레이션을 두지 않으므로(Board.cs 의 결정 주석),
        # 칸을 더했다면 이미 쓰던 자료 파일에는 손으로 넣어야 한다. 그 문장을 아래에 적어 둘 것.
        #
        # 손으로 넣은 변경 (넣은 날짜 / 문장):
        #   2026-08-24  ALTER TABLE Post ADD COLUMN IsPinned INTEGER NOT NULL DEFAULT 0   (board.db, 39차)
        """;

    /// <summary>지문 파일 위치 — 저장소 뿌리의 <c>docs/schema.txt</c>.</summary>
    private static string FingerprintPath()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "NewSchool.csproj")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "docs", "schema.txt");
    }

    /// <summary><c>Settings.cs</c> 의 <c>CREATE TABLE IF NOT EXISTS Settings (...)</c> 에서 칸 이름을 읽는다.</summary>
    private static List<string> SettingsTableColumns()
    {
        string source = File.ReadAllText(Path.Combine(
            Path.GetDirectoryName(FingerprintPath())!, "..", "Settings.cs"));

        int start = source.IndexOf("CREATE TABLE IF NOT EXISTS Settings (", StringComparison.Ordinal);
        Assert.True(start >= 0, "Settings.cs 에서 Settings 표 정의를 찾지 못했다");

        int open = source.IndexOf('(', start);
        int close = source.IndexOf(')', open);
        var cols = new List<string>();

        foreach (var raw in source[(open + 1)..close].Split('\n'))
        {
            string line = raw.Trim().TrimEnd(',');
            if (line.Length == 0 || line.StartsWith("--", StringComparison.Ordinal)) continue;
            cols.Add(line.Split(' ')[0]);
        }

        return cols;
    }

    private static async Task<List<string>> DumpAsync(string label, string dbPath)
    {
        var lines = new List<string>();

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var names = new List<string>();
        using (var cmd = conn.CreateCommand())
        {
            // 뷰도 함께 본다 — 뷰는 초기화 때마다 다시 만들어지므로 세대 문제는 없지만,
            // 뷰가 사라지거나 칸이 바뀌는 것도 알아야 한다.
            cmd.CommandText =
                "SELECT name FROM sqlite_master WHERE type IN ('table','view') " +
                "AND name NOT LIKE 'sqlite_%' ORDER BY name";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                names.Add(reader.GetString(0));
        }

        foreach (var name in names)
        {
            var cols = new List<string>();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT name FROM pragma_table_info('{name}') ORDER BY cid";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                cols.Add(reader.GetString(0));

            lines.Add($"{label}:{name}={string.Join(",", cols)}");
        }

        return lines;
    }

    [Fact]
    public async Task 자료_파일의_표와_칸이_지문과_같다()
    {
        var actual = new List<string>();
        actual.AddRange(await DumpAsync("school", _school.DbPath));
        actual.AddRange(await DumpAsync("board", _board.DbPath));
        actual.AddRange(await DumpAsync("scheduler", _scheduler.DbPath));

        // 설정 DB 는 Key/Value 한 표뿐이라 세대 문제가 없다(설정이 늘어도 행이 늘 뿐이다).
        // 그 사실 자체가 바뀌면 알아야 하므로 함께 적는다. 이쪽만 소스에서 읽는 이유는
        // SettingsDb 가 Settings.UserDataPath 에 붙어 있어 임시 파일로 열 수 없기 때문이다.
        actual.Add("settings:Settings=" + string.Join(",", SettingsTableColumns()));

        string path = FingerprintPath();
        string body = Header + Environment.NewLine + string.Join(Environment.NewLine, actual) + Environment.NewLine;

        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, body, new UTF8Encoding(false));
            Assert.Fail($"지문 파일이 없어 새로 만들었다: {path}\n내용을 확인하고 커밋할 것.");
        }

        var expected = File.ReadAllLines(path)
            .Where(l => !l.TrimStart().StartsWith('#') && l.Trim().Length > 0)
            .ToList();

        var added = actual.Except(expected).ToList();
        var removed = expected.Except(actual).ToList();

        if (added.Count == 0 && removed.Count == 0) return;

        var report = new StringBuilder();
        report.AppendLine("자료 파일의 스키마가 지문과 다르다.");
        report.AppendLine();
        if (added.Count > 0)
        {
            report.AppendLine("[지금 코드가 만드는 것]");
            foreach (var l in added) report.AppendLine("  " + l);
        }
        if (removed.Count > 0)
        {
            report.AppendLine("[지문에 적힌 것]");
            foreach (var l in removed) report.AppendLine("  " + l);
        }
        report.AppendLine();
        report.AppendLine("칸을 더했다면: 이미 쓰던 자료 파일에는 생기지 않는다(이 프로젝트는 ALTER");
        report.AppendLine("마이그레이션을 두지 않는다). 넣을 ALTER 문을 docs/schema.txt 머리말에 적고,");
        report.AppendLine("그 파일의 해당 줄을 위 [지금 코드가 만드는 것] 으로 갈아 끼울 것.");

        Assert.Fail(report.ToString());
    }
}

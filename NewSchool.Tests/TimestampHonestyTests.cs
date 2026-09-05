using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Helpers;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.Services;
using NewSchool.Tests.Infrastructure;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// <b>읽으면서 없는 시각을 지어내지 않는다.</b>
///
/// <para><c>ClassDiaryRepository</c> 는 시각 칸을 읽다 무엇이 터지든
/// <see cref="DateTime.Now"/> 를 돌려주었다(옛 이름 <c>GetDateTimeSafe</c>). 그래서
/// <b>칸 자체가 없는</b> 옛 자료 파일까지 삼켰다 — 51차(스키마 세대 축)가 만든
/// <see cref="MissingColumnException"/> 과 <see cref="DbErrorText"/> 안내가 여기서만 죽어,
/// 일지 화면은 옛 파일을 열고도 아무 말 없이 "방금 만든 일지" 인 척했다.</para>
///
/// <para>지어내기를 걷어내면 <b>저장 쪽이 드러난다</b> — 아무도 <c>UpdatedAt</c> 을 세우지
/// 않아 읽어 온 값이 그대로 다시 저장되고 있었다(수정일시가 맨 처음 시각에 멈춰 있었다).
/// 지어낸 <c>Now</c> 가 그 자리를 가려 주고 있었던 것이라, 둘은 한 짝이다.</para>
/// </summary>
public class TimestampHonestyTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _db;

    public TimestampHonestyTests(SqliteTestFixture db) => _db = db;

    private static ClassDiary NewDiary(int grade, int classNum, DateTime date) =>
        new(TestData.SchoolCode, TestData.Year, 1, grade, classNum, date, TestData.TeacherId);

    [Fact]
    public async Task 알아볼_수_없는_시각은_지어내지_않고_비워_둔다()
    {
        using var repo = new ClassDiaryRepository(_db.DbPath);
        int no = await repo.CreateAsync(NewDiary(1, 1, new DateTime(2026, 3, 2)));

        await ExecuteAsync(
            "UPDATE ClassDiary SET CreatedAt = '알 수 없는 값', UpdatedAt = NULL WHERE No = @no", no);

        var loaded = await repo.GetByNoAsync(no);

        Assert.NotNull(loaded);
        Assert.Equal(default, loaded!.CreatedAt);
        Assert.Equal(default, loaded.UpdatedAt);
    }

    [Fact]
    public async Task 시각_칸이_없는_옛_자료_파일은_안내로_이어진다()
    {
        string path = Path.Combine(Path.GetTempPath(), "NewSchoolTests", $"old_{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        try
        {
            await CreateOldClassDiaryTableAsync(path);
            using var repo = new ClassDiaryRepository(path);

            // 예전에는 이 예외를 삼키고 "지금" 을 돌려주어, 옛 파일인 줄 아무도 몰랐다.
            var ex = await Assert.ThrowsAsync<MissingColumnException>(() => repo.GetByNoAsync(1));

            Assert.Equal("CreatedAt", ex.ColumnName);
            Assert.Contains("오래된 자료 파일", DbErrorText.Explain(ex)!);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task 일지를_고쳐_저장하면_수정일시가_앞으로_간다()
    {
        using var repo = new ClassDiaryRepository(_db.DbPath);
        using var service = new ClassDiaryService(_db.DbPath);

        var diary = NewDiary(2, 3, new DateTime(2026, 3, 3));
        diary.Memo = "처음";
        await service.CreateOrUpdateAsync(diary);

        // 저장 형식이 초 단위라 같은 초 안에서 두 번 저장하면 구분되지 않는다.
        // 과거로 밀어 놓고, 다시 저장했을 때 따라오는지 본다.
        var past = new DateTime(2000, 1, 1);
        await ExecuteAsync("UPDATE ClassDiary SET UpdatedAt = @past WHERE No = @no",
            diary.No, ("@past", past.ToString("yyyy-MM-dd HH:mm:ss")));

        var reloaded = await repo.GetByNoAsync(diary.No);
        Assert.Equal(past, reloaded!.UpdatedAt);

        reloaded.Memo = "고쳤다";
        await service.CreateOrUpdateAsync(reloaded);

        var after = await repo.GetByNoAsync(diary.No);
        Assert.True(after!.UpdatedAt > past,
            $"수정일시가 그대로다: {after.UpdatedAt:yyyy-MM-dd HH:mm:ss}");
    }

    /// <summary>
    /// 자료 계층 어디에도 "읽으면서 지금 시각을 지어내는" 자리가 없어야 한다.
    /// 같은 무늬가 세 곳(학급일지·진도·좌석)에 있었다.
    /// </summary>
    [Fact]
    public void 읽으면서_현재_시각을_지어내는_자리는_없다()
    {
        var readsRow = new Regex(@"reader\.|\br\.Get|ReadDate\(|TryParse\(|IsDBNull");
        var inventsNow = new Regex(@"DateTime\.(Now|Today)");

        var offenders = new List<string>();
        foreach (string folder in new[] { "Repositories", "Services", "Board", "Scheduler" })
        {
            string root = Path.Combine(RepoRoot(), folder);
            foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (line.TrimStart().StartsWith("//")) continue;   // 이유를 적어 둔 주석은 센다고 치지 않는다
                    if (readsRow.IsMatch(line) && inventsNow.IsMatch(line))
                        offenders.Add($"{Path.GetRelativePath(RepoRoot(), file)}:{i + 1}  {line.Trim()}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "읽으면서 현재 시각을 지어내는 자리:\n" + string.Join("\n", offenders));
    }

    private async Task ExecuteAsync(string sql, int no, params (string Name, object Value)[] extra)
    {
        using var conn = new SqliteConnection($"Data Source={_db.DbPath}");
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@no", no);
        foreach (var (name, value) in extra)
            cmd.Parameters.AddWithValue(name, value);

        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>CreatedAt·UpdatedAt 이 없던 시절의 ClassDiary 표를 흉내 낸다.</summary>
    private static async Task CreateOldClassDiaryTableAsync(string path)
    {
        using var conn = new SqliteConnection($"Data Source={path}");
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE ClassDiary (
                No INTEGER PRIMARY KEY AUTOINCREMENT,
                SchoolCode TEXT NOT NULL,
                TeacherID TEXT NULL,
                Year INTEGER NOT NULL,
                Semester INTEGER NOT NULL,
                Date TEXT NOT NULL,
                Grade INTEGER NOT NULL,
                Class INTEGER NOT NULL,
                Absent TEXT DEFAULT '',
                Late TEXT DEFAULT '',
                LeaveEarly TEXT DEFAULT '',
                Memo TEXT DEFAULT '',
                Notice TEXT DEFAULT '',
                Life TEXT DEFAULT ''
            );
            INSERT INTO ClassDiary (SchoolCode, TeacherID, Year, Semester, Date, Grade, Class)
            VALUES ('7530072', 'T0001', 2026, 1, '2026-03-02', 1, 1);
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "NewSchool.csproj")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}

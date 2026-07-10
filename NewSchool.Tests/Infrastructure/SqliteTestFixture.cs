using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Database;
using Xunit;

namespace NewSchool.Tests.Infrastructure;

/// <summary>
/// 임시 파일 SQLite DB 픽스처 (TEST_PLAN 0단계).
/// 실제 스키마(<see cref="DatabaseInitializer"/>)로 초기화하고,
/// 종료 시 커넥션 풀을 비운 뒤 파일을 삭제한다.
///
/// 사용: <c>IClassFixture&lt;SqliteTestFixture&gt;</c> — 테스트 클래스 단위로 DB 1개 공유.
/// 리포지토리/서비스에는 <see cref="DbPath"/>를 주입한다.
/// </summary>
public sealed class SqliteTestFixture : IAsyncLifetime
{
    static SqliteTestFixture()
    {
        // 앱에서는 App.xaml.cs 가 호출하지만, 테스트 호스트에는 App 이 없으므로 여기서 1회 초기화
        SQLitePCL.Batteries_V2.Init();
    }

    /// <summary>이 픽스처 전용 임시 DB 파일 경로.</summary>
    public string DbPath { get; } = Path.Combine(
        Path.GetTempPath(), "NewSchoolTests", $"school_{Guid.NewGuid():N}.db");

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);

        using var initializer = new DatabaseInitializer(DbPath);
        bool ok = await initializer.InitializeAsync();
        if (!ok)
            throw new InvalidOperationException($"테스트 DB 초기화 실패: {DbPath}");

        await SeedBaseRowsAsync();
    }

    /// <summary>
    /// FK 대상 기본 행 시드 — Enrollment 등은 School(SchoolCode)·Teacher(TeacherID)를
    /// 참조하므로, 시드 없이는 INSERT 가 FK 제약으로 실패한다.
    /// </summary>
    private async Task SeedBaseRowsAsync()
    {
        using var conn = new SqliteConnection($"Data Source={DbPath}");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        // CreatedAt/UpdatedAt 은 NOT NULL — 누락 시 INSERT 가 실패한다.
        // (OR IGNORE 를 쓰면 이 실패가 조용히 삼켜져 FK 오류로 뒤늦게 드러나므로 사용하지 않음)
        cmd.CommandText = """
            INSERT INTO School (SchoolCode, SchoolName, CreatedAt, UpdatedAt)
            VALUES (@sc, '테스트학교', @now, @now);
            INSERT INTO Teacher (TeacherID, LoginID, Name, CreatedAt, UpdatedAt)
            VALUES (@tid, @tid, '테스트교사', @now, @now);
            """;
        cmd.Parameters.AddWithValue("@sc", TestData.SchoolCode);
        cmd.Parameters.AddWithValue("@tid", TestData.TeacherId);
        cmd.Parameters.AddWithValue("@now", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Student 행을 만들고 StudentID 를 반환. Enrollment·StudentLog 등
    /// Student FK 를 참조하는 행을 넣기 전에 호출한다.
    /// </summary>
    public async Task<string> NewStudentInDbAsync(string name = "홍길동", string sex = "남")
    {
        using var repo = new Repositories.StudentRepository(DbPath);
        var student = TestData.NewStudent(name: name, sex: sex);
        await repo.CreateAsync(student);
        return student.StudentID;
    }

    public Task DisposeAsync()
    {
        // 풀에 남은 커넥션이 파일을 잡고 있으면 삭제가 실패하므로 먼저 비운다
        SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(DbPath)) File.Delete(DbPath);
        }
        catch
        {
            // 임시 폴더의 잔존 파일은 무해 — OS 가 정리
        }
        return Task.CompletedTask;
    }
}

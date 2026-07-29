using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Database;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// <c>StudentSpecial.Semester</c> 컬럼 추가 마이그레이션.
///
/// Semester 컬럼이 없던 구버전 DB 를 직접 만들어 초기화기를 태우고,
/// (1) 컬럼이 붙는지 (2) 교과활동의 학기가 Course 에서 백필되는지
/// (3) 학년 단위 기록과 교과목이 삭제된 기록은 0 으로 남는지 확인한다.
/// </summary>
public sealed class StudentSpecialSemesterMigrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "NewSchoolTests", "SpecSemesterMig_" + Guid.NewGuid().ToString("N")[..8]);

    public StudentSpecialSemesterMigrationTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { /* 정리 실패는 테스트 결과와 무관 */ }
    }

    /// <summary>
    /// "Semester 컬럼이 없던 시절의 DB" 를 재현한다.
    /// 가짜 스키마를 손으로 쓰면 실제 초기화기와 어긋나 엉뚱한 실패가 나므로,
    /// 실제 초기화기로 전체 스키마를 만든 뒤 Semester 컬럼만 떼어낸다.
    /// </summary>
    private static async Task CreateLegacyDbAsync(string path)
    {
        using (var init = new DatabaseInitializer(path))
        {
            Assert.True(await init.InitializeAsync());
        }
        SqliteConnection.ClearAllPools();

        using var conn = new SqliteConnection($"Data Source={path}");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();

        // 구버전 재현: Semester 컬럼 제거
        cmd.CommandText = "ALTER TABLE StudentSpecial DROP COLUMN Semester;";
        await cmd.ExecuteNonQueryAsync();

        // 백필 대상 데이터 — 1학기/2학기 같은 과목 + 학년단위 + 교과목이 삭제된 행
        cmd.CommandText = @"
            INSERT INTO School (SchoolCode, SchoolName, CreatedAt, UpdatedAt)
                VALUES ('SC1', '테스트학교', '2026-01-01', '2026-01-01');
            INSERT INTO Teacher (TeacherID, LoginID, Name, CreatedAt, UpdatedAt)
                VALUES ('T1', 't1', '교사', '2026-01-01', '2026-01-01');

            INSERT INTO Course (No, SchoolCode, Year, Semester, TeacherID, Grade, Subject)
                VALUES (11, 'SC1', 2026, 1, 'T1', 1, '국어');
            INSERT INTO Course (No, SchoolCode, Year, Semester, TeacherID, Grade, Subject)
                VALUES (22, 'SC1', 2026, 2, 'T1', 1, '국어');

            INSERT INTO Student (StudentID, Name, CreatedAt, UpdatedAt)
                VALUES ('S1', '학생', '2026-01-01', '2026-01-01');

            INSERT INTO StudentSpecial (StudentID, Year, Type, Title, Content, Date, CourseNo, SubjectName)
                VALUES ('S1', 2026, '교과활동', '국어', '1학기', '2026-05-01', 11, '국어');
            INSERT INTO StudentSpecial (StudentID, Year, Type, Title, Content, Date, CourseNo, SubjectName)
                VALUES ('S1', 2026, '교과활동', '국어', '2학기', '2026-10-01', 22, '국어');
            INSERT INTO StudentSpecial (StudentID, Year, Type, Title, Content, Date, CourseNo, SubjectName)
                VALUES ('S1', 2026, '진로활동', '', '연간', '2026-05-01', NULL, '');
            INSERT INTO StudentSpecial (StudentID, Year, Type, Title, Content, Date, CourseNo, SubjectName)
                VALUES ('S1', 2026, '교과활동', '수학', '과목삭제됨', '2026-05-01', NULL, '수학');";
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<int> QuerySemesterAsync(string path, string content)
    {
        using var conn = new SqliteConnection($"Data Source={path}");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Semester FROM StudentSpecial WHERE Content = $c;";
        cmd.Parameters.AddWithValue("$c", content);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    [Fact]
    public async Task 구버전DB_초기화하면_Semester가_추가되고_교과활동만_백필된다()
    {
        string path = Path.Combine(_dir, "legacy.db");
        await CreateLegacyDbAsync(path);

        using (var init = new DatabaseInitializer(path))
        {
            Assert.True(await init.InitializeAsync());
        }

        // 교과활동은 Course 의 학기로 백필
        Assert.Equal(1, await QuerySemesterAsync(path, "1학기"));
        Assert.Equal(2, await QuerySemesterAsync(path, "2학기"));

        // 학년 단위 기록은 0
        Assert.Equal(0, await QuerySemesterAsync(path, "연간"));

        // 교과목이 이미 삭제돼 CourseNo 가 NULL 인 교과활동은 복원 불가 → 0
        Assert.Equal(0, await QuerySemesterAsync(path, "과목삭제됨"));
    }

    [Fact]
    public async Task 두번_초기화해도_안전하다()
    {
        string path = Path.Combine(_dir, "twice.db");
        await CreateLegacyDbAsync(path);

        using (var init = new DatabaseInitializer(path)) Assert.True(await init.InitializeAsync());
        using (var init = new DatabaseInitializer(path)) Assert.True(await init.InitializeAsync());

        Assert.Equal(1, await QuerySemesterAsync(path, "1학기"));
        Assert.Equal(2, await QuerySemesterAsync(path, "2학기"));
    }

    [Fact]
    public async Task 스키마_버전이_기록된다()
    {
        string path = Path.Combine(_dir, "version.db");
        await CreateLegacyDbAsync(path);

        using (var init = new DatabaseInitializer(path)) Assert.True(await init.InitializeAsync());

        using var conn = new SqliteConnection($"Data Source={path}");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        Assert.Equal(DatabaseInitializer.SchemaVersion, Convert.ToInt32(await cmd.ExecuteScalarAsync()));
    }
}

using System;
using System.Linq;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.Tests.Infrastructure;
using Xunit;

namespace NewSchool.Tests;

/// <summary>Enrollment 리포지토리 경계 테스트 (TEST_PLAN 1단계) — 스모크 외 추가 케이스.</summary>
public class EnrollmentRepositoryTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _db;

    public EnrollmentRepositoryTests(SqliteTestFixture db) => _db = db;

    [Fact]
    public async Task DeleteAsync_논리삭제_후_GetById는_null()
    {
        using var repo = new EnrollmentRepository(_db.DbPath);
        var id = await _db.NewStudentInDbAsync("삭제대상");
        int no = await repo.CreateAsync(TestData.NewEnrollment(id, "삭제대상", classNum: 9, number: 1));

        Assert.True(await repo.DeleteAsync(no));

        // IsDeleted=1 이 되어 기본 조회에서 제외
        Assert.Null(await repo.GetByIdAsync(no));
    }

    [Fact]
    public async Task GetById_존재하지않는_No는_null()
    {
        using var repo = new EnrollmentRepository(_db.DbPath);
        Assert.Null(await repo.GetByIdAsync(999_999));
    }

    [Fact]
    public async Task TeacherID_왕복_보존()
    {
        // 회귀: MapEnrollment 가 TeacherID 를 매핑하지 않아 조회→저장 시 담임이 유실되던 버그 (2026-07-10)
        using var repo = new EnrollmentRepository(_db.DbPath);
        var id = await _db.NewStudentInDbAsync("담임확인");
        int no = await repo.CreateAsync(TestData.NewEnrollment(id, "담임확인", classNum: 9, number: 2));

        var loaded = await repo.GetByIdAsync(no);
        Assert.Equal(TestData.TeacherId, loaded!.TeacherID);

        // 로드한 객체 그대로 재저장해도 FK 위반 없이 성공해야 한다
        Assert.True(await repo.UpdateAsync(loaded));
        var again = await repo.GetByIdAsync(no);
        Assert.Equal(TestData.TeacherId, again!.TeacherID);
    }

    [Fact]
    public async Task ApplyChangeAsync_졸업처리시_변동과_일자와_재적여부가_함께_바뀐다()
    {
        using var repo = new EnrollmentRepository(_db.DbPath);
        var id = await _db.NewStudentInDbAsync("졸업생");
        int no = await repo.CreateAsync(TestData.NewEnrollment(id, "졸업생", classNum: 9, number: 3));

        var gradDate = new DateTime(TestData.Year + 1, 2, 28);
        Assert.True(await repo.ApplyChangeAsync(no, EnrollmentChange.Graduated, gradDate));

        var loaded = await repo.GetByIdAsync(no);
        Assert.Equal(EnrollmentChange.Graduated, loaded!.ChangeType);
        Assert.Equal(gradDate.Date, loaded.ChangeDate);

        // 졸업생은 명단에서 빠진다 — 이것이 어긋나면 명렬표에 계속 남는다.
        Assert.False(loaded.IsActive);
    }

    [Fact]
    public async Task 전출한_학생은_명단_조회에서_빠지고_학생관리에서만_보인다()
    {
        using var repo = new EnrollmentRepository(_db.DbPath);
        int year = TestData.Year + 5;

        var staying = await _db.NewStudentInDbAsync("남는학생");
        var leaving = await _db.NewStudentInDbAsync("전출학생");
        await repo.CreateAsync(TestData.NewEnrollment(staying, "남는학생", year: year, classNum: 1, number: 1));
        int leftNo = await repo.CreateAsync(TestData.NewEnrollment(leaving, "전출학생", year: year, classNum: 1, number: 2));

        await repo.ApplyChangeAsync(leftNo, EnrollmentChange.TransferredOut, new DateTime(year, 5, 10));

        var roster = await repo.GetEnrollmentsAsync(TestData.SchoolCode, year, 1, 1);
        var everyone = await repo.GetEnrollmentsAsync(TestData.SchoolCode, year, 1, 1, includeInactive: true);

        Assert.Single(roster);
        Assert.Equal(staying, roster[0].StudentID);
        Assert.Equal(2, everyone.Count);
    }

    // ── 변동 일자는 DateTime? 다 (2026-08-30) ────────────────────────────
    //
    // 컬럼은 TEXT("yyyy-MM-dd")이고 모델은 DateTime? 다. 그 경계가 이 리포지토리 하나뿐이라
    // 여기서 못박는다. 특히 **null 이 진짜 상태**라는 것 — DateTime.MinValue 같은 특수값으로
    // 바꿔 놓으면 EnrollmentGuard 가 "아주 오래전에 떠났다" 로 읽어 늘 경고를 띄운다.

    [Fact]
    public async Task 변동_일자가_왕복한다()
    {
        int year = TestData.Year + 70;
        using var repo = new EnrollmentRepository(_db.DbPath);
        var sid = await _db.NewStudentInDbAsync("날짜있는학생");

        var e = TestData.NewEnrollment(sid, year: year);
        e.ApplyChange(EnrollmentChange.TransferredOut, new DateTime(year, 5, 10));
        int no = await repo.CreateAsync(e);

        var loaded = await repo.GetByIdAsync(no);
        Assert.Equal(new DateTime(year, 5, 10), loaded!.ChangeDate);
    }

    [Fact]
    public async Task 변동_일자가_없으면_null_로_돌아온다()
    {
        int year = TestData.Year + 71;
        using var repo = new EnrollmentRepository(_db.DbPath);
        var sid = await _db.NewStudentInDbAsync("날짜없는학생");

        var e = TestData.NewEnrollment(sid, year: year);
        e.ChangeDate = null;
        int no = await repo.CreateAsync(e);

        var loaded = await repo.GetByIdAsync(no);
        Assert.Null(loaded!.ChangeDate);
    }

    /// <summary>
    /// ⚠ 형식이 어긋난 글자가 들어 있어도 <b>던지지 않는다</b>.
    ///
    /// <para>이 매퍼는 명렬표 조회가 모두 지나는 길목이라, 여기서 터지면 날짜 하나가 아니라
    /// <b>학생 목록이 통째로 안 뜬다.</b> 문자열이던 시절에 아무 글자나 들어갈 수 있었으므로
    /// 옛 DB 에 그런 행이 남아 있을 수 있다.</para>
    /// </summary>
    [Fact]
    public async Task 깨진_날짜_글자는_null_로_읽고_터지지_않는다()
    {
        int year = TestData.Year + 72;
        using var repo = new EnrollmentRepository(_db.DbPath);
        var sid = await _db.NewStudentInDbAsync("깨진날짜학생");
        int no = await repo.CreateAsync(TestData.NewEnrollment(sid, year: year));

        using (var con = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_db.DbPath}"))
        {
            await con.OpenAsync();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "UPDATE Enrollment SET ChangeDate = '날짜아님' WHERE No = @no";
            cmd.Parameters.AddWithValue("@no", no);
            await cmd.ExecuteNonQueryAsync();
        }

        var loaded = await repo.GetByIdAsync(no);   // 던지면 여기서 실패한다
        Assert.NotNull(loaded);
        Assert.Null(loaded!.ChangeDate);
    }

    /// <summary>
    /// <c>GetBySchoolAndYearAsync</c> 를 지우면서 "<c>GetEnrollmentsAsync</c> 가 같은 일을
    /// 한다" 고 적었다(2026-08-30). 그 말이 사실인지 여기서 못박는다.
    ///
    /// <para>학교·학년도로 좁히는 것은 물론이고, <c>includeInactive: true</c> 를 주면
    /// 전출·졸업까지 포함하는 것도 같아야 한다 — 지운 쪽은 <c>IsActive</c> 를 아예 보지
    /// 않았으므로, 그 동작을 대신하려면 이 인자가 그 자리를 채워야 한다.</para>
    /// </summary>
    [Fact]
    public async Task 학교_학년도_조회는_GetEnrollmentsAsync_가_대신한다()
    {
        int year = TestData.Year + 73;
        using var repo = new EnrollmentRepository(_db.DbPath);

        var staying = await _db.NewStudentInDbAsync("남는학생");
        var leaving = await _db.NewStudentInDbAsync("전출학생");
        await repo.CreateAsync(TestData.NewEnrollment(staying, year: year, number: 1));
        int leftNo = await repo.CreateAsync(TestData.NewEnrollment(leaving, year: year, number: 2));
        await repo.ApplyChangeAsync(leftNo, EnrollmentChange.TransferredOut, new DateTime(year, 5, 10));

        // 기본: 재적자만
        var onRoll = await repo.GetEnrollmentsAsync(TestData.SchoolCode, year);
        Assert.Contains(onRoll, e => e.StudentID == staying);
        Assert.DoesNotContain(onRoll, e => e.StudentID == leaving);

        // includeInactive: 지운 메서드처럼 전출까지 포함
        var everyone = await repo.GetEnrollmentsAsync(TestData.SchoolCode, year, includeInactive: true);
        Assert.Contains(everyone, e => e.StudentID == staying);
        Assert.Contains(everyone, e => e.StudentID == leaving);

        // 다른 학년도는 섞이지 않는다
        Assert.All(everyone.Where(e => e.StudentID == staying || e.StudentID == leaving),
                   e => Assert.Equal(year, e.Year));
    }
}

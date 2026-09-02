using System;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.Tests.Infrastructure;
using Xunit;

namespace NewSchool.Tests;

/// <summary>StudentLog 리포지토리 CRUD·경계 테스트 (TEST_PLAN 1단계).</summary>
public class StudentLogRepositoryTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _db;

    public StudentLogRepositoryTests(SqliteTestFixture db) => _db = db;

    [Fact]
    public async Task CRUD_왕복()
    {
        using var repo = new StudentLogRepository(_db.DbPath);
        var id = await _db.NewStudentInDbAsync("기록학생");

        int no = await repo.CreateAsync(TestData.NewStudentLog(id, log: "첫 기록"));
        Assert.True(no > 0);

        var loaded = await repo.GetByIdAsync(no);
        Assert.NotNull(loaded);
        Assert.Equal("첫 기록", loaded!.Log);
        Assert.Equal(LogCategory.기타, loaded.Category);

        loaded.Log = "수정된 기록";
        loaded.Topic = "주제";
        Assert.True(await repo.UpdateAsync(loaded));
        var updated = await repo.GetByIdAsync(no);
        Assert.Equal("수정된 기록", updated!.Log);
        Assert.Equal("주제", updated.Topic);

        Assert.True(await repo.DeleteAsync(no));
        Assert.Null(await repo.GetByIdAsync(no));
    }

    [Fact]
    public async Task 동아리정보가_저장되고_왕복한다()
    {
        // 회귀 방지: ClubNo/ClubName 은 파라미터로만 채워지고 INSERT/UPDATE 문에는 빠져 있었다.
        //   SQLite 가 안 쓰이는 파라미터를 조용히 무시해 오류 없이 동아리 정보만 사라졌다.
        using var repo = new StudentLogRepository(_db.DbPath);
        var id = await _db.NewStudentInDbAsync("동아리학생");

        var log = TestData.NewStudentLog(id, category: LogCategory.동아리활동);
        log.ClubNo = 7;
        log.ClubName = "천체관측반";

        int no = await repo.CreateAsync(log);
        var created = await repo.GetByIdAsync(no);
        Assert.Equal(7, created!.ClubNo);
        Assert.Equal("천체관측반", created.ClubName);

        created.ClubNo = 9;
        created.ClubName = "방송반";
        Assert.True(await repo.UpdateAsync(created));

        var updated = await repo.GetByIdAsync(no);
        Assert.Equal(9, updated!.ClubNo);
        Assert.Equal("방송반", updated.ClubName);
    }

    [Fact]
    public async Task CourseNo_0은_NULL로_저장되어_FK위반_없음()
    {
        // Course 행이 없어도 CourseNo=0(미지정) 로그는 저장돼야 한다 (0→NULL 변환 계약)
        using var repo = new StudentLogRepository(_db.DbPath);
        var id = await _db.NewStudentInDbAsync("과목없음");

        var log = TestData.NewStudentLog(id);
        log.CourseNo = 0;
        int no = await repo.CreateAsync(log);

        var loaded = await repo.GetByIdAsync(no);
        Assert.Equal(0, loaded!.CourseNo); // NULL → 0 으로 왕복
    }

    [Fact]
    public async Task GetByStudent_semester0은_전체학기()
    {
        using var repo = new StudentLogRepository(_db.DbPath);
        var id = await _db.NewStudentInDbAsync("학기필터");
        await repo.CreateAsync(TestData.NewStudentLog(id, semester: 1, log: "1학기"));
        await repo.CreateAsync(TestData.NewStudentLog(id, semester: 2, log: "2학기"));

        var all = await repo.GetByStudentAsync(id, TestData.Year, semester: 0);
        var s1 = await repo.GetByStudentAsync(id, TestData.Year, semester: 1);
        var s2 = await repo.GetByStudentAsync(id, TestData.Year, semester: 2);

        Assert.Equal(2, all.Count);
        Assert.Single(s1);
        Assert.Equal("1학기", s1[0].Log);
        Assert.Single(s2);
    }

    [Fact]
    public async Task GetByStudent_다른학년도는_제외()
    {
        using var repo = new StudentLogRepository(_db.DbPath);
        var id = await _db.NewStudentInDbAsync("연도필터");
        await repo.CreateAsync(TestData.NewStudentLog(id, year: TestData.Year));
        await repo.CreateAsync(TestData.NewStudentLog(id, year: TestData.Year + 1));

        var thisYear = await repo.GetByStudentAsync(id, TestData.Year);
        Assert.Single(thisYear);
    }

    [Fact]
    public async Task 배치조회_학생별_그룹핑과_빈목록()
    {
        using var repo = new StudentLogRepository(_db.DbPath);
        var id1 = await _db.NewStudentInDbAsync("배치일");
        var id2 = await _db.NewStudentInDbAsync("배치이");
        await repo.CreateAsync(TestData.NewStudentLog(id1, log: "일-1"));
        await repo.CreateAsync(TestData.NewStudentLog(id1, log: "일-2"));
        await repo.CreateAsync(TestData.NewStudentLog(id2, log: "이-1"));

        var empty = await repo.GetByStudentIdsAsync([], TestData.Year);
        Assert.Empty(empty);

        // 계약: 요청한 모든 ID 에 키가 존재하고, 기록 없는 학생은 빈 리스트
        var grouped = await repo.GetByStudentIdsAsync([id1, id2, "없는ID"], TestData.Year);
        Assert.Equal(3, grouped.Count);
        Assert.Equal(2, grouped[id1].Count);
        Assert.Single(grouped[id2]);
        Assert.Empty(grouped["없는ID"]);
    }

    [Fact]
    public async Task 배치조회_semester필터_적용()
    {
        using var repo = new StudentLogRepository(_db.DbPath);
        var id = await _db.NewStudentInDbAsync("배치학기");
        await repo.CreateAsync(TestData.NewStudentLog(id, semester: 1));
        await repo.CreateAsync(TestData.NewStudentLog(id, semester: 2));

        var s2Only = await repo.GetByStudentIdsAsync([id], TestData.Year, semester: 2);
        Assert.Single(s2Only[id]);
        Assert.Equal(2, s2Only[id][0].Semester);
    }

    /// <summary>
    /// <b>규칙: 기간 조회는 마지막 날을 포함한다.</b>
    ///
    /// <para><c>Date</c> 는 TEXT 라, 시각이 붙은 행("2026-03-31 10:00")이 있으면
    /// 문자열 비교 <c>&lt;= '2026-03-31'</c> 에 걸리지 않아 마지막 날이 통째로 빠졌다.
    /// 하루 조회는 처음부터 <c>date()</c> 로 감쌌는데 기간 조회만 맨몸이었다 —
    /// 한 파일 안의 형제 함수가 서로 다른 규칙을 쓰고 있었다.</para>
    /// </summary>
    [Fact]
    public async Task 기간_조회는_시각이_붙은_행도_마지막_날에_포함한다()
    {
        using var repo = new StudentLogRepository(_db.DbPath);
        using var enrollRepo = new EnrollmentRepository(_db.DbPath);

        var id = await _db.NewStudentInDbAsync("기간조회");
        await enrollRepo.CreateAsync(TestData.NewEnrollment(id, name: "기간조회", grade: 3, classNum: 7));

        var end = new DateTime(TestData.Year, 3, 31);
        await repo.CreateAsync(TestData.NewStudentLog(id, date: end));

        // 지금 저장 경로는 날짜만 넣는다. 그렇지 않던 시절의 행을 흉내 내어,
        // 문자열 비교였다면 마지막 날에서 빠졌을 값을 직접 넣는다.
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_db.DbPath}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE StudentLog SET Date = @d WHERE StudentID = @s";
            cmd.Parameters.AddWithValue("@d", $"{end:yyyy-MM-dd} 10:00:00");
            cmd.Parameters.AddWithValue("@s", id);
            await cmd.ExecuteNonQueryAsync();
        }

        var found = await repo.GetByClassAndDateRangeAsync(
            TestData.SchoolCode, TestData.Year, grade: 3, classroom: 7,
            new DateTime(TestData.Year, 3, 1), end);

        Assert.Single(found);
    }

    // 카테고리별 조회 테스트는 GetByCategoryAsync 와 함께 지웠다(44차).
    // 그 메서드는 어느 화면에서도 부르지 않았고 서비스에 감싼 것도 없어, 이 테스트만이
    // 유일한 호출부였다 — 아무도 쓰지 않는 코드를 테스트가 붙들고 있던 셈이다.
    // 카테고리 거르기는 화면이 학기 전체를 받아 메모리에서 한다.
}

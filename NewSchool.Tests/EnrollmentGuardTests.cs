using System;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.Services;
using NewSchool.Tests.Infrastructure;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 학교를 떠난 학생에게 기록을 남길 때의 경고 판정.
///
/// <para>이 판정은 <b>막지 않고 알리기만 한다.</b> 그래서 잘못 울리는 쪽(양치기 소년)이
/// 잘못 침묵하는 쪽보다 나쁘다 — 근거가 없을 때 조용한지를 촘촘히 고정해 둔다.</para>
/// </summary>
public sealed class EnrollmentGuardTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _db;

    public EnrollmentGuardTests(SqliteTestFixture db) => _db = db;

    private async Task<string> LeftStudentAsync(string changeType, string changeDate,
                                                int year, int number)
    {
        var sid = await _db.NewStudentInDbAsync("떠난학생");
        using var repo = new EnrollmentRepository(_db.DbPath);

        int no = await repo.CreateAsync(
            TestData.NewEnrollment(sid, "떠난학생", year: year, number: number));
        await repo.ApplyChangeAsync(no, changeType, DateTime.Parse(changeDate));

        return sid;
    }

    // ── 알려야 하는 경우 ──────────────────────────────────────

    [Theory]
    [InlineData(EnrollmentChange.TransferredOut)]
    [InlineData(EnrollmentChange.Graduated)]
    [InlineData(EnrollmentChange.Withdrawn)]
    [InlineData(EnrollmentChange.Expelled)]
    public async Task 떠난_뒤_날짜면_알린다(string changeType)
    {
        int year = TestData.Year + 30;
        var sid = await LeftStudentAsync(changeType, $"{year}-05-10", year, 1);

        var notice = await EnrollmentGuard.DescribeRecordAfterLeavingAsync(
            sid, year, new DateTime(year, 5, 20), _db.DbPath, TestData.SchoolCode);

        Assert.NotNull(notice);
        Assert.Contains(changeType, notice);
    }

    // ── 조용해야 하는 경우 ────────────────────────────────────

    [Fact]
    public async Task 떠나기_전_날짜면_알리지_않는다()
    {
        int year = TestData.Year + 31;
        var sid = await LeftStudentAsync(EnrollmentChange.TransferredOut, $"{year}-05-10", year, 2);

        Assert.Null(await EnrollmentGuard.DescribeRecordAfterLeavingAsync(
            sid, year, new DateTime(year, 5, 1), _db.DbPath, TestData.SchoolCode));
    }

    [Fact]
    public async Task 떠난_당일은_알리지_않는다()
    {
        // 전출일 당일까지는 우리 학생이었다.
        int year = TestData.Year + 32;
        var sid = await LeftStudentAsync(EnrollmentChange.TransferredOut, $"{year}-05-10", year, 3);

        Assert.Null(await EnrollmentGuard.DescribeRecordAfterLeavingAsync(
            sid, year, new DateTime(year, 5, 10), _db.DbPath, TestData.SchoolCode));
    }

    [Theory]
    [InlineData(EnrollmentChange.OnLeave)]     // 휴학
    [InlineData(EnrollmentChange.Deferred)]    // 유예
    [InlineData(EnrollmentChange.OutOfQuota)]  // 정원외
    public async Task 학적이_살아_있는_비활성은_알리지_않는다(string changeType)
    {
        // 명단에는 안 나오지만 학적은 살아 있어 그 사이에도 기록할 일이 있다.
        int year = TestData.Year + 33;
        var sid = await LeftStudentAsync(changeType, $"{year}-05-10", year, 4);

        Assert.Null(await EnrollmentGuard.DescribeRecordAfterLeavingAsync(
            sid, year, new DateTime(year, 9, 1), _db.DbPath, TestData.SchoolCode));
    }

    [Fact]
    public async Task 재학_중이면_알리지_않는다()
    {
        int year = TestData.Year + 34;
        var sid = await _db.NewStudentInDbAsync("재학생");
        using var repo = new EnrollmentRepository(_db.DbPath);
        await repo.CreateAsync(TestData.NewEnrollment(sid, "재학생", year: year, number: 5));

        Assert.Null(await EnrollmentGuard.DescribeRecordAfterLeavingAsync(
            sid, year, new DateTime(year, 12, 31), _db.DbPath, TestData.SchoolCode));
    }

    [Fact]
    public async Task 변동일자를_모르면_알리지_않는다()
    {
        // 기준이 없는데 경고를 띄우면 사람이 경고를 무시하는 법을 배운다.
        int year = TestData.Year + 35;
        var sid = await _db.NewStudentInDbAsync("날짜없음");
        using var repo = new EnrollmentRepository(_db.DbPath);

        int no = await repo.CreateAsync(TestData.NewEnrollment(sid, "날짜없음", year: year, number: 6));
        await repo.ApplyChangeAsync(no, EnrollmentChange.TransferredOut);   // 날짜 생략

        // ChangeDate 를 비워 둔다 — ApplyChangeAsync 는 날짜를 안 주면 옛 값을 지킨다.
        using var cmd = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_db.DbPath}");
        await cmd.OpenAsync();
        using var upd = cmd.CreateCommand();
        upd.CommandText = "UPDATE Enrollment SET ChangeDate = NULL WHERE No = @no";
        upd.Parameters.AddWithValue("@no", no);
        await upd.ExecuteNonQueryAsync();

        Assert.Null(await EnrollmentGuard.DescribeRecordAfterLeavingAsync(
            sid, year, new DateTime(year, 12, 31), _db.DbPath, TestData.SchoolCode));
    }

    [Fact]
    public async Task 그_학년도_학적이_없으면_알리지_않는다()
    {
        int year = TestData.Year + 36;
        var sid = await LeftStudentAsync(EnrollmentChange.TransferredOut, $"{year}-05-10", year, 7);

        // 다른 학년도로 물으면 근거가 없다.
        Assert.Null(await EnrollmentGuard.DescribeRecordAfterLeavingAsync(
            sid, year + 1, new DateTime(year + 1, 5, 20), _db.DbPath, TestData.SchoolCode));
    }

    [Fact]
    public async Task 학생ID_가_비면_알리지_않는다()
    {
        Assert.Null(await EnrollmentGuard.DescribeRecordAfterLeavingAsync(
            "", TestData.Year, DateTime.Today, _db.DbPath, TestData.SchoolCode));
    }

    // ── 반대 방향: 변동일자 뒤에 이미 남은 기록 세기 ──────────

    [Fact]
    public async Task 전출일_뒤에_남은_누가기록을_센다()
    {
        int year = TestData.Year + 37;
        var sid = await LeftStudentAsync(EnrollmentChange.TransferredOut, $"{year}-05-10", year, 8);

        using var logRepo = new StudentLogRepository(_db.DbPath);
        await logRepo.CreateAsync(TestData.NewStudentLog(sid, year: year, date: new DateTime(year, 5, 1)));
        await logRepo.CreateAsync(TestData.NewStudentLog(sid, year: year, date: new DateTime(year, 6, 1)));
        await logRepo.CreateAsync(TestData.NewStudentLog(sid, year: year, date: new DateTime(year, 7, 1)));

        var notice = await EnrollmentGuard.DescribeExistingRecordsAfterAsync(
            sid, year, EnrollmentChange.TransferredOut, $"{year}-05-10", _db.DbPath);

        Assert.NotNull(notice);
        Assert.Contains("2건", notice);   // 6월·7월만. 5월 1일은 전출 전이다
    }

    [Fact]
    public async Task 뒤에_남은_기록이_없으면_알리지_않는다()
    {
        int year = TestData.Year + 38;
        var sid = await LeftStudentAsync(EnrollmentChange.TransferredOut, $"{year}-05-10", year, 9);

        using var logRepo = new StudentLogRepository(_db.DbPath);
        await logRepo.CreateAsync(TestData.NewStudentLog(sid, year: year, date: new DateTime(year, 3, 1)));

        Assert.Null(await EnrollmentGuard.DescribeExistingRecordsAfterAsync(
            sid, year, EnrollmentChange.TransferredOut, $"{year}-05-10", _db.DbPath));
    }

    [Fact]
    public async Task 떠난_변동이_아니면_세지_않는다()
    {
        int year = TestData.Year + 39;
        var sid = await _db.NewStudentInDbAsync("진급생");

        Assert.Null(await EnrollmentGuard.DescribeExistingRecordsAfterAsync(
            sid, year, EnrollmentChange.Promoted, $"{year}-03-02", _db.DbPath));
    }
}

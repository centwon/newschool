using System.Collections.Generic;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Services;
using NewSchool.Tests.Infrastructure;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 좌석 이력 회차 회귀 테스트 (2026-08-25, 40차).
///
/// 회차(Round)는 <b>서로 다른 자리 배치</b>를 세는 것이지 저장 버튼을 누른 횟수가 아니다.
/// 예전에는 저장할 때마다 무조건 <c>MAX(Round)+1</c> 이라, 자리를 한 번 짜면서 안내 문구를
/// 고치고 고정 좌석을 잡느라 여러 번 저장하면 "최근 N회차의 짝 회피" 창이 같은 배치로 차버려
/// 지난 배치를 전혀 회피하지 못했다.
/// </summary>
public class SeatRoundTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _db;

    public SeatRoundTests(SqliteTestFixture db) => _db = db;

    private const string Sc = TestData.SchoolCode;
    private const int Year = 2026;

    /// <summary>2×2 짝 배치 하나. <paramref name="students"/> 는 (Row,Col) 순서대로 놓인다.</summary>
    private static SeatArrangement Arrangement(int grade, int classNo, params string[] students)
    {
        var a = new SeatArrangement
        {
            SchoolCode = Sc,
            Year = Year,
            Grade = grade,
            Class = classNo,
            Jul = 2,
            Jjak = 2,
            Rows = 2,
        };

        for (int i = 0; i < students.Length; i++)
            a.Assignments.Add(new SeatAssignment { Row = i / 2, Col = i % 2, StudentID = students[i] });

        return a;
    }

    [Fact]
    public async Task 같은_배치를_두_번_저장해도_회차는_늘지_않는다()
    {
        using var svc = new SeatService(_db.DbPath);
        var options = new SeatOptions();

        await svc.SaveAsync(Arrangement(1, 1, "S1", "S2", "S3", "S4"), options, 2);
        Assert.Equal(1, await svc.GetRoundCountAsync(Sc, Year, 1, 1));

        // 자리는 그대로 두고 다시 저장 — 안내 문구·고정 좌석만 손봤을 때의 상황
        await svc.SaveAsync(Arrangement(1, 1, "S1", "S2", "S3", "S4"), options, 2);
        Assert.Equal(1, await svc.GetRoundCountAsync(Sc, Year, 1, 1));

        await svc.SaveAsync(Arrangement(1, 1, "S1", "S2", "S3", "S4"), options, 2);
        Assert.Equal(1, await svc.GetRoundCountAsync(Sc, Year, 1, 1));
    }

    [Fact]
    public async Task 자리를_바꿔_저장하면_회차가_늘어난다()
    {
        using var svc = new SeatService(_db.DbPath);
        var options = new SeatOptions();

        await svc.SaveAsync(Arrangement(2, 1, "S1", "S2", "S3", "S4"), options, 2);
        Assert.Equal(1, await svc.GetRoundCountAsync(Sc, Year, 2, 1));

        // 짝을 바꿨다 — 이건 진짜 새 회차다
        await svc.SaveAsync(Arrangement(2, 1, "S1", "S3", "S2", "S4"), options, 2);
        Assert.Equal(2, await svc.GetRoundCountAsync(Sc, Year, 2, 1));

        // 다시 그대로 저장하면 늘지 않는다
        await svc.SaveAsync(Arrangement(2, 1, "S1", "S3", "S2", "S4"), options, 2);
        Assert.Equal(2, await svc.GetRoundCountAsync(Sc, Year, 2, 1));
    }

    [Fact]
    public async Task 짝모드가_바뀌면_자리가_같아도_새_회차다()
    {
        using var svc = new SeatService(_db.DbPath);
        var options = new SeatOptions();

        // 짝(2인석) — S1·S2 와 S3·S4 가 짝
        await svc.SaveAsync(Arrangement(3, 1, "S1", "S2", "S3", "S4"), options, 2);
        Assert.Equal(1, await svc.GetRoundCountAsync(Sc, Year, 3, 1));

        // 자리는 그대로지만 1인석이 되면 짝 관계가 사라진다 — 같은 배치로 볼 수 없다
        await svc.SaveAsync(Arrangement(3, 1, "S1", "S2", "S3", "S4"), options, 1);
        Assert.Equal(2, await svc.GetRoundCountAsync(Sc, Year, 3, 1));
    }

    [Fact]
    public async Task 일인석_학급도_회차가_쌓인다()
    {
        // 회귀: 짝 이력만 세던 시절 1인석 학급은 아무리 저장해도 0 회차로 보였다.
        using var svc = new SeatService(_db.DbPath);
        var options = new SeatOptions();

        await svc.SaveAsync(Arrangement(4, 1, "S1", "S2", "S3", "S4"), options, 1);
        Assert.Equal(1, await svc.GetRoundCountAsync(Sc, Year, 4, 1));

        await svc.SaveAsync(Arrangement(4, 1, "S1", "S2", "S3", "S4"), options, 1);
        Assert.Equal(1, await svc.GetRoundCountAsync(Sc, Year, 4, 1));

        await svc.SaveAsync(Arrangement(4, 1, "S4", "S3", "S2", "S1"), options, 1);
        Assert.Equal(2, await svc.GetRoundCountAsync(Sc, Year, 4, 1));
    }
}

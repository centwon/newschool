using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Tests.Infrastructure;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 좌석 표 셋(<c>SeatAssignment</c>·<c>SeatHistory</c>·<c>SeatPosHistory</c>)은
/// <c>StudentID</c> 를 직접 들고 있으면서 <b>FK 가 없다</b>. 그래서 학생을 지워도 아무것도
/// 따라 지워지지 않고, 없는 학생을 가리키는 행이 영영 쌓였다.
///
/// <para>학적 재설계 3b(좌석도 <c>EnrollmentNo</c> 를 가리키게)를 하지 않기로 하면서
/// (근거는 <c>docs/enrollment-redesign.md</c> 7.5 — 학년도 범위가 이미 잡혀 있어 버그가 아니다)
/// 남는 것은 이 <b>찌꺼기</b> 하나였다. 초기화기의 고아 정리에 넣어 끝낸다.</para>
///
/// <para>좌석과 이력의 처리가 다른 것에 뜻이 있다 — 좌석 행에는 미사용·숨김·고정 같은
/// <b>배치 상태</b>가 함께 들어 있어 지우면 자리 모양이 무너진다. 그래서 비우기만 한다.</para>
/// </summary>
public class SeatOrphanCleanupTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _db;

    public SeatOrphanCleanupTests(SqliteTestFixture db) => _db = db;

    private async Task<SqliteConnection> OpenAsync()
    {
        var con = new SqliteConnection($"Data Source={_db.DbPath}");
        await con.OpenAsync();
        return con;
    }

    private async Task ExecAsync(SqliteConnection con, string sql)
    {
        using var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<long> ScalarAsync(SqliteConnection con, string sql)
    {
        using var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(await cmd.ExecuteScalarAsync() ?? 0L);
    }

    /// <summary>초기화기를 한 번 더 돌린다 — 고아 정리는 그 안에서 돈다.</summary>
    private async Task RunInitializerAsync()
    {
        using var init = new NewSchool.Database.DatabaseInitializer(_db.DbPath);
        await init.InitializeAsync();
    }

    [Fact]
    public async Task 없는_학생을_가리키는_좌석은_비워지고_배치_상태는_남는다()
    {
        const string ghost = "9999999900009999";
        int arrangementNo;

        using (var con = await OpenAsync())
        {
            await ExecAsync(con, $@"
                INSERT INTO SeatArrangement
                    (SchoolCode, Year, Grade, Class, Jul, Jjak, Rows, CreatedAt, UpdatedAt)
                VALUES ('{TestData.SchoolCode}', 2999, 9, 9, 5, 1, 5, '2999-01-01', '2999-01-01');");
            arrangementNo = (int)await ScalarAsync(con, "SELECT last_insert_rowid()");

            // 없는 학생을 가리키면서 '고정' 상태를 함께 든 좌석
            await ExecAsync(con, $@"
                INSERT INTO SeatAssignment (ArrangementNo, Row, Col, StudentID, IsFixed)
                VALUES ({arrangementNo}, 0, 0, '{ghost}', 1);");
        }

        await RunInitializerAsync();

        using (var con = await OpenAsync())
        {
            long rows = await ScalarAsync(con,
                $"SELECT COUNT(*) FROM SeatAssignment WHERE ArrangementNo = {arrangementNo}");
            long ghosts = await ScalarAsync(con,
                $"SELECT COUNT(*) FROM SeatAssignment WHERE ArrangementNo = {arrangementNo} AND StudentID IS NOT NULL");
            long fixedKept = await ScalarAsync(con,
                $"SELECT IsFixed FROM SeatAssignment WHERE ArrangementNo = {arrangementNo}");

            Assert.Equal(1, rows);          // 행은 남는다 — 자리 모양이 무너지면 안 된다
            Assert.Equal(0, ghosts);        // 학생만 비워진다
            Assert.Equal(1, fixedKept);     // 배치 상태는 그대로
        }
    }

    [Fact]
    public async Task 없는_학생의_짝_이력과_자리_이력은_지워진다()
    {
        const string ghostA = "9999999900008881";
        const string ghostB = "9999999900008882";

        using (var con = await OpenAsync())
        {
            await ExecAsync(con, $@"
                INSERT INTO SeatHistory
                    (SchoolCode, Year, Grade, Class, StudentID_A, StudentID_B, Round, Kind, SavedAt)
                VALUES ('{TestData.SchoolCode}', 2999, 9, 9, '{ghostA}', '{ghostB}', 1, 'Pair', '2999-01-01');

                INSERT INTO SeatPosHistory
                    (SchoolCode, Year, Grade, Class, StudentID, Row, Col, Round, SavedAt)
                VALUES ('{TestData.SchoolCode}', 2999, 9, 9, '{ghostA}', 0, 0, 1, '2999-01-01');");
        }

        await RunInitializerAsync();

        using (var con = await OpenAsync())
        {
            Assert.Equal(0, await ScalarAsync(con,
                $"SELECT COUNT(*) FROM SeatHistory WHERE StudentID_A = '{ghostA}'"));
            Assert.Equal(0, await ScalarAsync(con,
                $"SELECT COUNT(*) FROM SeatPosHistory WHERE StudentID = '{ghostA}'"));
        }
    }

    /// <summary>
    /// ⚠ 멀쩡한 학생의 좌석·이력까지 쓸어 가면 안 된다. 고아 정리는 초기화 때마다 돌므로
    /// 조건이 한 글자만 넓어도 <b>앱을 켤 때마다</b> 자리 배치가 사라진다.
    /// </summary>
    [Fact]
    public async Task 살아_있는_학생의_좌석과_이력은_건드리지_않는다()
    {
        var sid = await _db.NewStudentInDbAsync("좌석주인");
        int arrangementNo;

        using (var con = await OpenAsync())
        {
            await ExecAsync(con, $@"
                INSERT INTO SeatArrangement
                    (SchoolCode, Year, Grade, Class, Jul, Jjak, Rows, CreatedAt, UpdatedAt)
                VALUES ('{TestData.SchoolCode}', 2998, 8, 8, 5, 1, 5, '2998-01-01', '2998-01-01');");
            arrangementNo = (int)await ScalarAsync(con, "SELECT last_insert_rowid()");

            await ExecAsync(con, $@"
                INSERT INTO SeatAssignment (ArrangementNo, Row, Col, StudentID)
                VALUES ({arrangementNo}, 1, 1, '{sid}');

                INSERT INTO SeatPosHistory
                    (SchoolCode, Year, Grade, Class, StudentID, Row, Col, Round, SavedAt)
                VALUES ('{TestData.SchoolCode}', 2998, 8, 8, '{sid}', 1, 1, 1, '2998-01-01');");
        }

        await RunInitializerAsync();

        using (var con = await OpenAsync())
        {
            Assert.Equal(1, await ScalarAsync(con,
                $"SELECT COUNT(*) FROM SeatAssignment WHERE ArrangementNo = {arrangementNo} AND StudentID = '{sid}'"));
            Assert.Equal(1, await ScalarAsync(con,
                $"SELECT COUNT(*) FROM SeatPosHistory WHERE StudentID = '{sid}'"));
        }
    }
}

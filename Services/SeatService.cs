using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Models;

namespace NewSchool.Services;

/// <summary>
/// 좌석 배치 저장/로드/이력 관리 서비스.
/// (SchoolCode, Year, Grade, Class)가 논리 키 — 학급별로 1개 배치가 현재값.
/// 저장 시점마다 짝·위치 이력이 누적되어 "지난 짝 배제" 옵션에서 사용된다.
/// </summary>
public class SeatService : IDisposable
{
    private readonly SqliteConnection _connection;
    private bool _disposed;

    public SeatService()
    {
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = SchoolDatabase.DbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString();
        _connection = new SqliteConnection(cs);
        _connection.Open();

        using var pragma = _connection.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON;";
        pragma.ExecuteNonQuery();
    }

    #region 저장 / 로드

    /// <summary>
    /// 학급 좌석 배치 저장 (upsert). 같은 학급 기존 배치는 덮어쓴다.
    /// 저장 시 짝·위치 이력도 함께 누적한다.
    /// </summary>
    /// <param name="arrangement">Assignments 포함</param>
    /// <param name="options">배치 옵션 (JSON 직렬화)</param>
    /// <param name="jjakForHistory">짝 인접 판정용 Jjak(=2면 짝모드)</param>
    public async Task<int> SaveAsync(SeatArrangement arrangement, SeatOptions options, int jjakForHistory)
    {
        using var tx = _connection.BeginTransaction();
        try
        {
            arrangement.OptionsJson = JsonSerializer.Serialize(options, SeatOptionsJsonContext.Default.SeatOptions);
            arrangement.UpdatedAt = DateTime.Now;

            // 1) SeatArrangement upsert
            int arrangementNo = await UpsertArrangementAsync(arrangement, tx);

            // 2) 기존 SeatAssignment 삭제 후 일괄 삽입
            using (var del = _connection.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = "DELETE FROM SeatAssignment WHERE ArrangementNo = $no;";
                del.Parameters.AddWithValue("$no", arrangementNo);
                await del.ExecuteNonQueryAsync();
            }

            foreach (var a in arrangement.Assignments)
            {
                using var ins = _connection.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = @"
                    INSERT INTO SeatAssignment
                    (ArrangementNo, Row, Col, StudentID, IsUnUsed, IsHidden, IsFixed)
                    VALUES ($no, $row, $col, $sid, $unused, $hidden, $fixed);";
                ins.Parameters.AddWithValue("$no", arrangementNo);
                ins.Parameters.AddWithValue("$row", a.Row);
                ins.Parameters.AddWithValue("$col", a.Col);
                ins.Parameters.AddWithValue("$sid", (object?)a.StudentID ?? DBNull.Value);
                ins.Parameters.AddWithValue("$unused", a.IsUnUsed ? 1 : 0);
                ins.Parameters.AddWithValue("$hidden", a.IsHidden ? 1 : 0);
                ins.Parameters.AddWithValue("$fixed", a.IsFixed ? 1 : 0);
                await ins.ExecuteNonQueryAsync();
            }

            // 3) 이력 누적 — 다음 Round 번호 조회
            int nextRound = await GetNextRoundAsync(
                arrangement.SchoolCode, arrangement.Year, arrangement.Grade, arrangement.Class, tx);

            await InsertPairHistoryAsync(arrangement, jjakForHistory, nextRound, tx);
            await InsertPosHistoryAsync(arrangement, nextRound, tx);

            tx.Commit();
            return arrangementNo;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private async Task<int> UpsertArrangementAsync(SeatArrangement a, SqliteTransaction tx)
    {
        // 기존 레코드 조회
        using var q = _connection.CreateCommand();
        q.Transaction = tx;
        q.CommandText = @"
            SELECT No FROM SeatArrangement
            WHERE SchoolCode=$sc AND Year=$y AND Grade=$g AND Class=$c LIMIT 1;";
        q.Parameters.AddWithValue("$sc", a.SchoolCode);
        q.Parameters.AddWithValue("$y", a.Year);
        q.Parameters.AddWithValue("$g", a.Grade);
        q.Parameters.AddWithValue("$c", a.Class);
        var existing = await q.ExecuteScalarAsync();

        if (existing != null && existing != DBNull.Value)
        {
            int no = Convert.ToInt32(existing);
            using var upd = _connection.CreateCommand();
            upd.Transaction = tx;
            upd.CommandText = @"
                UPDATE SeatArrangement
                SET Jul=$jul, Jjak=$jjak, Rows=$rows, ShowPhoto=$sp,
                    Message=$msg, IsLocked=$lock, OptionsJson=$opts, UpdatedAt=$upd
                WHERE No=$no;";
            upd.Parameters.AddWithValue("$jul", a.Jul);
            upd.Parameters.AddWithValue("$jjak", a.Jjak);
            upd.Parameters.AddWithValue("$rows", a.Rows);
            upd.Parameters.AddWithValue("$sp", a.ShowPhoto ? 1 : 0);
            upd.Parameters.AddWithValue("$msg", a.Message ?? string.Empty);
            upd.Parameters.AddWithValue("$lock", a.IsLocked ? 1 : 0);
            upd.Parameters.AddWithValue("$opts", a.OptionsJson ?? string.Empty);
            upd.Parameters.AddWithValue("$upd", a.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            upd.Parameters.AddWithValue("$no", no);
            await upd.ExecuteNonQueryAsync();
            a.No = no;
            return no;
        }

        using var ins = _connection.CreateCommand();
        ins.Transaction = tx;
        ins.CommandText = @"
            INSERT INTO SeatArrangement
            (SchoolCode, Year, Grade, Class, Jul, Jjak, Rows, ShowPhoto, Message,
             IsLocked, OptionsJson, CreatedAt, UpdatedAt)
            VALUES ($sc,$y,$g,$c,$jul,$jjak,$rows,$sp,$msg,$lock,$opts,$cre,$upd);
            SELECT last_insert_rowid();";
        ins.Parameters.AddWithValue("$sc", a.SchoolCode);
        ins.Parameters.AddWithValue("$y", a.Year);
        ins.Parameters.AddWithValue("$g", a.Grade);
        ins.Parameters.AddWithValue("$c", a.Class);
        ins.Parameters.AddWithValue("$jul", a.Jul);
        ins.Parameters.AddWithValue("$jjak", a.Jjak);
        ins.Parameters.AddWithValue("$rows", a.Rows);
        ins.Parameters.AddWithValue("$sp", a.ShowPhoto ? 1 : 0);
        ins.Parameters.AddWithValue("$msg", a.Message ?? string.Empty);
        ins.Parameters.AddWithValue("$lock", a.IsLocked ? 1 : 0);
        ins.Parameters.AddWithValue("$opts", a.OptionsJson ?? string.Empty);
        ins.Parameters.AddWithValue("$cre", a.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        ins.Parameters.AddWithValue("$upd", a.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        var inserted = await ins.ExecuteScalarAsync();
        int newNo = Convert.ToInt32(inserted);
        a.No = newNo;
        return newNo;
    }

    /// <summary>
    /// 학급 좌석 배치 로드. 없으면 null.
    /// </summary>
    public async Task<SeatArrangement?> LoadAsync(string schoolCode, int year, int grade, int classNo)
    {
        SeatArrangement? a = null;
        using (var q = _connection.CreateCommand())
        {
            q.CommandText = @"
                SELECT No, SchoolCode, Year, Grade, Class, Jul, Jjak, Rows, ShowPhoto,
                       Message, IsLocked, OptionsJson, CreatedAt, UpdatedAt
                FROM SeatArrangement
                WHERE SchoolCode=$sc AND Year=$y AND Grade=$g AND Class=$c LIMIT 1;";
            q.Parameters.AddWithValue("$sc", schoolCode);
            q.Parameters.AddWithValue("$y", year);
            q.Parameters.AddWithValue("$g", grade);
            q.Parameters.AddWithValue("$c", classNo);
            using var r = await q.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                a = new SeatArrangement
                {
                    No = r.GetInt32(0),
                    SchoolCode = r.GetString(1),
                    Year = r.GetInt32(2),
                    Grade = r.GetInt32(3),
                    Class = r.GetInt32(4),
                    Jul = r.GetInt32(5),
                    Jjak = r.GetInt32(6),
                    Rows = r.GetInt32(7),
                    ShowPhoto = r.GetInt32(8) == 1,
                    Message = r.IsDBNull(9) ? string.Empty : r.GetString(9),
                    IsLocked = r.GetInt32(10) == 1,
                    OptionsJson = r.IsDBNull(11) ? string.Empty : r.GetString(11),
                    CreatedAt = DateTime.TryParse(r.IsDBNull(12) ? null : r.GetString(12), out var cre) ? cre : DateTime.Now,
                    UpdatedAt = DateTime.TryParse(r.IsDBNull(13) ? null : r.GetString(13), out var upd) ? upd : DateTime.Now,
                };
            }
        }

        if (a == null) return null;

        using (var q2 = _connection.CreateCommand())
        {
            q2.CommandText = @"
                SELECT No, ArrangementNo, Row, Col, StudentID, IsUnUsed, IsHidden, IsFixed
                FROM SeatAssignment WHERE ArrangementNo=$no;";
            q2.Parameters.AddWithValue("$no", a.No);
            using var r = await q2.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                a.Assignments.Add(new SeatAssignment
                {
                    No = r.GetInt32(0),
                    ArrangementNo = r.GetInt32(1),
                    Row = r.GetInt32(2),
                    Col = r.GetInt32(3),
                    StudentID = r.IsDBNull(4) ? null : r.GetString(4),
                    IsUnUsed = r.GetInt32(5) == 1,
                    IsHidden = r.GetInt32(6) == 1,
                    IsFixed = r.GetInt32(7) == 1
                });
            }
        }

        return a;
    }

    /// <summary>배치 존재 여부</summary>
    public async Task<bool> ExistsAsync(string schoolCode, int year, int grade, int classNo)
    {
        using var q = _connection.CreateCommand();
        q.CommandText = @"
            SELECT 1 FROM SeatArrangement
            WHERE SchoolCode=$sc AND Year=$y AND Grade=$g AND Class=$c LIMIT 1;";
        q.Parameters.AddWithValue("$sc", schoolCode);
        q.Parameters.AddWithValue("$y", year);
        q.Parameters.AddWithValue("$g", grade);
        q.Parameters.AddWithValue("$c", classNo);
        var v = await q.ExecuteScalarAsync();
        return v != null && v != DBNull.Value;
    }

    /// <summary>저장된 옵션만 빠르게 로드 (없으면 기본값)</summary>
    public async Task<SeatOptions> LoadOptionsAsync(string schoolCode, int year, int grade, int classNo)
    {
        using var q = _connection.CreateCommand();
        q.CommandText = @"
            SELECT OptionsJson FROM SeatArrangement
            WHERE SchoolCode=$sc AND Year=$y AND Grade=$g AND Class=$c LIMIT 1;";
        q.Parameters.AddWithValue("$sc", schoolCode);
        q.Parameters.AddWithValue("$y", year);
        q.Parameters.AddWithValue("$g", grade);
        q.Parameters.AddWithValue("$c", classNo);
        var v = await q.ExecuteScalarAsync() as string;

        if (string.IsNullOrWhiteSpace(v)) return new SeatOptions();
        try { return JsonSerializer.Deserialize(v, SeatOptionsJsonContext.Default.SeatOptions) ?? new SeatOptions(); }
        catch { return new SeatOptions(); }
    }

    #endregion

    #region 이력

    private async Task<int> GetNextRoundAsync(string sc, int y, int g, int c, SqliteTransaction tx)
    {
        using var q = _connection.CreateCommand();
        q.Transaction = tx;
        q.CommandText = @"
            SELECT COALESCE(MAX(Round), 0) + 1 FROM SeatHistory
            WHERE SchoolCode=$sc AND Year=$y AND Grade=$g AND Class=$c;";
        q.Parameters.AddWithValue("$sc", sc);
        q.Parameters.AddWithValue("$y", y);
        q.Parameters.AddWithValue("$g", g);
        q.Parameters.AddWithValue("$c", c);
        var v = await q.ExecuteScalarAsync();
        return Convert.ToInt32(v);
    }

    private async Task InsertPairHistoryAsync(SeatArrangement a, int jjak, int round, SqliteTransaction tx)
    {
        if (jjak < 2) return; // 1인석은 짝 이력 없음
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        // 같은 Row, 같은 Jjak 그룹 내 인접(Col 차이 1) 학생 쌍 찾기
        var placed = a.Assignments
            .Where(x => !string.IsNullOrEmpty(x.StudentID) && !x.IsUnUsed && !x.IsHidden)
            .ToList();

        foreach (var p in placed)
        {
            foreach (var q2 in placed)
            {
                if (p == q2) continue;
                if (p.Row != q2.Row) continue;
                int gA = p.Col / jjak;
                int gB = q2.Col / jjak;
                if (gA != gB) continue;
                if (Math.Abs(p.Col - q2.Col) != 1) continue;
                // 중복 방지: A < B 정렬된 한 방향만
                if (string.Compare(p.StudentID, q2.StudentID, StringComparison.Ordinal) >= 0) continue;

                using var ins = _connection.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = @"
                    INSERT INTO SeatHistory
                    (SchoolCode, Year, Grade, Class, StudentID_A, StudentID_B, Round, Kind, SavedAt)
                    VALUES ($sc,$y,$g,$c,$a,$b,$r,'Pair',$t);";
                ins.Parameters.AddWithValue("$sc", a.SchoolCode);
                ins.Parameters.AddWithValue("$y", a.Year);
                ins.Parameters.AddWithValue("$g", a.Grade);
                ins.Parameters.AddWithValue("$c", a.Class);
                ins.Parameters.AddWithValue("$a", p.StudentID!);
                ins.Parameters.AddWithValue("$b", q2.StudentID!);
                ins.Parameters.AddWithValue("$r", round);
                ins.Parameters.AddWithValue("$t", now);
                await ins.ExecuteNonQueryAsync();
            }
        }
    }

    private async Task InsertPosHistoryAsync(SeatArrangement a, int round, SqliteTransaction tx)
    {
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        foreach (var x in a.Assignments)
        {
            if (string.IsNullOrEmpty(x.StudentID)) continue;
            if (x.IsUnUsed || x.IsHidden) continue;

            using var ins = _connection.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = @"
                INSERT INTO SeatPosHistory
                (SchoolCode, Year, Grade, Class, StudentID, Row, Col, Round, SavedAt)
                VALUES ($sc,$y,$g,$c,$s,$r,$co,$rd,$t);";
            ins.Parameters.AddWithValue("$sc", a.SchoolCode);
            ins.Parameters.AddWithValue("$y", a.Year);
            ins.Parameters.AddWithValue("$g", a.Grade);
            ins.Parameters.AddWithValue("$c", a.Class);
            ins.Parameters.AddWithValue("$s", x.StudentID!);
            ins.Parameters.AddWithValue("$r", x.Row);
            ins.Parameters.AddWithValue("$co", x.Col);
            ins.Parameters.AddWithValue("$rd", round);
            ins.Parameters.AddWithValue("$t", now);
            await ins.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// 최근 N회 짝 이력에서 등장한 학생 쌍을 반환 (IdA, IdB 정렬됨).
    /// </summary>
    public async Task<HashSet<(string, string)>> GetRecentPairsAsync(
        string sc, int y, int g, int c, int recentRounds)
    {
        var set = new HashSet<(string, string)>();
        if (recentRounds <= 0) return set;

        using var q = _connection.CreateCommand();
        q.CommandText = @"
            WITH recent AS (
                SELECT DISTINCT Round FROM SeatHistory
                WHERE SchoolCode=$sc AND Year=$y AND Grade=$g AND Class=$c
                ORDER BY Round DESC LIMIT $n
            )
            SELECT StudentID_A, StudentID_B FROM SeatHistory
            WHERE SchoolCode=$sc AND Year=$y AND Grade=$g AND Class=$c
              AND Round IN (SELECT Round FROM recent);";
        q.Parameters.AddWithValue("$sc", sc);
        q.Parameters.AddWithValue("$y", y);
        q.Parameters.AddWithValue("$g", g);
        q.Parameters.AddWithValue("$c", c);
        q.Parameters.AddWithValue("$n", recentRounds);

        using var r = await q.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var a = r.GetString(0);
            var b = r.GetString(1);
            var pair = string.Compare(a, b, StringComparison.Ordinal) < 0 ? (a, b) : (b, a);
            set.Add(pair);
        }
        return set;
    }

    /// <summary>
    /// 최근 N회 같은 자리 이력 조회. StudentID → {(Row,Col)}.
    /// </summary>
    public async Task<Dictionary<string, HashSet<(int Row, int Col)>>> GetRecentPositionsAsync(
        string sc, int y, int g, int c, int recentRounds)
    {
        var map = new Dictionary<string, HashSet<(int, int)>>();
        if (recentRounds <= 0) return map;

        using var q = _connection.CreateCommand();
        q.CommandText = @"
            WITH recent AS (
                SELECT DISTINCT Round FROM SeatPosHistory
                WHERE SchoolCode=$sc AND Year=$y AND Grade=$g AND Class=$c
                ORDER BY Round DESC LIMIT $n
            )
            SELECT StudentID, Row, Col FROM SeatPosHistory
            WHERE SchoolCode=$sc AND Year=$y AND Grade=$g AND Class=$c
              AND Round IN (SELECT Round FROM recent);";
        q.Parameters.AddWithValue("$sc", sc);
        q.Parameters.AddWithValue("$y", y);
        q.Parameters.AddWithValue("$g", g);
        q.Parameters.AddWithValue("$c", c);
        q.Parameters.AddWithValue("$n", recentRounds);

        using var r = await q.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var sid = r.GetString(0);
            var row = r.GetInt32(1);
            var col = r.GetInt32(2);
            if (!map.TryGetValue(sid, out var set))
            {
                set = new HashSet<(int, int)>();
                map[sid] = set;
            }
            set.Add((row, col));
        }
        return map;
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _connection?.Dispose();
    }
}

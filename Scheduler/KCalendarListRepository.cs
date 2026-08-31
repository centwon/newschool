using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Repositories;

namespace NewSchool.Scheduler.Repositories;

/// <summary>
/// KCalendarList 리포지토리 — 캘린더 목록(카테고리+색상) 관리
/// </summary>
public class KCalendarListRepository : BaseRepository
{
    public KCalendarListRepository(string dbPath) : base(dbPath) { }

    public async Task<int> CreateAsync(KCalendarList cal)
    {
        const string query = @"
            INSERT INTO KCalendarList (GoogleId, Title, Color, SortOrder, IsDefault, IsVisible, Updated, SyncMode, SyncToken, SchoolCode)
            VALUES (@GoogleId, @Title, @Color, @SortOrder, @IsDefault, @IsVisible, @Updated, @SyncMode, @SyncToken, @SchoolCode);
            SELECT last_insert_rowid();";
        try
        {
            using var cmd = CreateCommand(query);
            AddParameters(cmd, cal);
            var result = await cmd.ExecuteScalarAsync();
            cal.No = Convert.ToInt32(result);
            LogInfo($"KCalendarList 생성: No={cal.No}, Title='{cal.Title}'");
            return cal.No;
        }
        catch (Exception ex)
        {
            LogError($"KCalendarList 생성 실패: '{cal.Title}'", ex);
            throw;
        }
    }

    public async Task<List<KCalendarList>> GetAllAsync()
    {
        const string query = "SELECT * FROM KCalendarList ORDER BY SortOrder, Title";
        var list = new List<KCalendarList>();
        try
        {
            using var cmd = CreateCommand(query);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                list.Add(Map(reader));
            return list;
        }
        catch (Exception ex)
        {
            LogError("KCalendarList 전체 조회 실패", ex);
            throw;
        }
    }

    public async Task<KCalendarList?> GetByTitleAsync(string title)
    {
        const string query = "SELECT * FROM KCalendarList WHERE Title = @Title LIMIT 1";
        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@Title", title);
            using var reader = await cmd.ExecuteReaderAsync();
            return await reader.ReadAsync() ? Map(reader) : null;
        }
        catch (Exception ex)
        {
            LogError($"KCalendarList 조회 실패: '{title}'", ex);
            throw;
        }
    }

    public async Task<int> GetOrCreateAsync(string title, string color = "#4285F4", bool isDefault = false)
    {
        var existing = await GetByTitleAsync(title);
        if (existing != null) return existing.No;
        return await CreateNewAsync(title, color, isDefault, schoolCode: string.Empty);
    }

    /// <summary>학교 코드로 조회 (학사일정처럼 학교별로 분리되는 캘린더 전용)</summary>
    public async Task<KCalendarList?> GetByTitleAndSchoolCodeAsync(string title, string schoolCode)
    {
        const string query = "SELECT * FROM KCalendarList WHERE Title = @Title AND SchoolCode = @SchoolCode LIMIT 1";
        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@Title", title);
            cmd.Parameters.AddWithValue("@SchoolCode", schoolCode);
            using var reader = await cmd.ExecuteReaderAsync();
            return await reader.ReadAsync() ? Map(reader) : null;
        }
        catch (Exception ex)
        {
            LogError($"KCalendarList 학교별 조회 실패: '{title}', SchoolCode={schoolCode}", ex);
            throw;
        }
    }

    /// <summary>
    /// 학교 코드로 조회 후 없으면 생성 (학사일정 캘린더 전용). 같은 이름이라도 학교 코드가 다르면
    /// 별도 행으로 분리되어, 학교를 옮겨도 예전 학교 학사일정과 섞이지 않는다.
    /// </summary>
    public async Task<KCalendarList> GetOrCreateForSchoolAsync(string title, string schoolCode, string color)
    {
        var existing = await GetByTitleAndSchoolCodeAsync(title, schoolCode);
        if (existing != null) return existing;

        int no = await CreateNewAsync(title, color, isDefault: false, schoolCode: schoolCode);
        return (await GetByTitleAndSchoolCodeAsync(title, schoolCode))
            ?? throw new InvalidOperationException($"캘린더 생성 후 조회 실패: No={no}");
    }

    private async Task<int> CreateNewAsync(string title, string color, bool isDefault, string schoolCode)
    {
        int maxOrder = 0;
        try
        {
            using var cmd = CreateCommand("SELECT COALESCE(MAX(SortOrder), 0) FROM KCalendarList");
            maxOrder = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }
        catch (Exception ex)
        {
            // 정렬 순서를 못 읽어도 달력 만들기를 막지 않는다 — maxOrder 는 0 으로 남고
            // 새 달력이 SortOrder=1 로, 즉 목록 맨 앞에 꽂힐 뿐이다. 순서는 사람이 다시
            // 바꿀 수 있는 값이라, 그것 때문에 만들기 자체를 실패시키는 편이 더 나쁘다.
            //
            // 다만 조용히 넘기지는 않는다. 이 조회가 실패한다면 DB 쪽에 다른 문제가 있다는
            // 뜻이고, 그 신호까지 지우면 "왜 새 달력이 자꾸 맨 앞에 오지" 로만 보인다.
            LogWarning($"달력 정렬 순서 조회 실패 — 맨 앞(SortOrder=1)으로 만든다: {ex.Message}");
        }

        var newCal = new KCalendarList
        {
            Title      = title,
            Color      = color,
            SortOrder  = maxOrder + 1,
            IsDefault  = isDefault,
            IsVisible  = true,
            Updated    = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            SchoolCode = schoolCode
        };
        return await CreateAsync(newCal);
    }

    public async Task<bool> UpdateAsync(KCalendarList cal)
    {
        const string query = @"
            UPDATE KCalendarList SET GoogleId=@GoogleId, Title=@Title, Color=@Color,
                SortOrder=@SortOrder, IsDefault=@IsDefault, IsVisible=@IsVisible,
                Updated=@Updated, SyncMode=@SyncMode, SyncToken=@SyncToken, SchoolCode=@SchoolCode
            WHERE No = @No";
        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@No", cal.No);
            AddParameters(cmd, cal);
            return await cmd.ExecuteNonQueryAsync() > 0;
        }
        catch (Exception ex)
        {
            LogError($"KCalendarList 수정 실패: No={cal.No}", ex);
            throw;
        }
    }

    /// <summary>
    /// 캘린더 삭제 + 소속 KEvent 전체 삭제(고아 이벤트 방지, 두 테이블을 한 트랜잭션으로).
    ///
    /// <para>⚠ 기본 캘린더(<c>IsDefault=1</c>)는 지우지 않는다. 예전에는 <b>일정을 먼저 지우고</b>
    /// 캘린더 삭제에 <c>AND IsDefault = 0</c> 을 걸어 둔 뒤 결과와 무관하게 커밋했다. 그래서
    /// 기본 캘린더를 넘기면 <b>소속 일정만 몽땅 사라지고 캘린더는 남은 채 false 가 돌아왔다</b> —
    /// 부르는 쪽은 "삭제 실패"로 보는데 데이터는 이미 없었다. 이제 지울 수 있는지 <b>먼저</b>
    /// 확인하고, 아니면 아무것도 건드리지 않는다.</para>
    /// </summary>
    /// <returns>지웠으면 true. 없는 캘린더이거나 기본 캘린더면 false(아무 것도 지우지 않음).</returns>
    public async Task<bool> DeleteAsync(int no)
    {
        try
        {
            BeginTransaction();

            // 1. 지워도 되는 캘린더인지 먼저 확인 — 아니면 일정도 건드리지 않는다.
            bool deletable;
            using (var check = CreateCommand(
                "SELECT EXISTS(SELECT 1 FROM KCalendarList WHERE No = @No AND IsDefault = 0)"))
            {
                check.Parameters.AddWithValue("@No", no);
                deletable = Convert.ToInt32(await check.ExecuteScalarAsync()) == 1;
            }

            if (!deletable)
            {
                Rollback();
                LogWarning($"KCalendarList 삭제 건너뜀(없거나 기본 캘린더): No={no}");
                return false;
            }

            // 2. 소속 일정 → 캘린더 순으로 지운다.
            using (var delEvents = CreateCommand("DELETE FROM KEvent WHERE CalendarId = @No"))
            {
                delEvents.Parameters.AddWithValue("@No", no);
                await delEvents.ExecuteNonQueryAsync();
            }

            bool ok;
            using (var cmd = CreateCommand("DELETE FROM KCalendarList WHERE No = @No AND IsDefault = 0"))
            {
                cmd.Parameters.AddWithValue("@No", no);
                ok = await cmd.ExecuteNonQueryAsync() > 0;
            }

            Commit();
            return ok;
        }
        catch (Exception ex)
        {
            Rollback();
            LogError($"KCalendarList 삭제 실패: No={no}", ex);
            throw;
        }
    }

    private static void AddParameters(SqliteCommand cmd, KCalendarList cal)
    {
        cmd.Parameters.AddWithValue("@GoogleId",  cal.GoogleId  ?? string.Empty);
        cmd.Parameters.AddWithValue("@Title",     cal.Title     ?? string.Empty);
        cmd.Parameters.AddWithValue("@Color",     cal.Color     ?? "#4285F4");
        cmd.Parameters.AddWithValue("@SortOrder", cal.SortOrder);
        cmd.Parameters.AddWithValue("@IsDefault", cal.IsDefault  ? 1 : 0);
        cmd.Parameters.AddWithValue("@IsVisible", cal.IsVisible  ? 1 : 0);
        cmd.Parameters.AddWithValue("@Updated",   cal.Updated   ?? string.Empty);
        cmd.Parameters.AddWithValue("@SyncMode",  cal.SyncMode  ?? "None");
        cmd.Parameters.AddWithValue("@SyncToken", cal.SyncToken ?? string.Empty);
        cmd.Parameters.AddWithValue("@SchoolCode", cal.SchoolCode ?? string.Empty);
    }

    private static KCalendarList Map(SqliteDataReader r)
    {
        var cal = new KCalendarList
        {
            No        = r.GetInt32(r.GetOrdinal("No")),
            GoogleId  = r.GetString(r.GetOrdinal("GoogleId")),
            Title     = r.GetString(r.GetOrdinal("Title")),
            Color     = r.GetString(r.GetOrdinal("Color")),
            SortOrder = r.GetInt32(r.GetOrdinal("SortOrder")),
            IsDefault = r.GetInt32(r.GetOrdinal("IsDefault")) == 1,
            IsVisible = r.GetInt32(r.GetOrdinal("IsVisible")) == 1,
            Updated   = r.GetString(r.GetOrdinal("Updated")),
            SyncMode  = r.GetString(r.GetOrdinal("SyncMode"))
        };
        // 예전에는 try/catch 로 GetOrdinal 의 예외를 받아 넘겼다. 행마다 예외를 던지는 비용도
        // 비용이지만, catch 가 모든 예외를 삼켜서 진짜 읽기 오류까지 빈 문자열이 됐다.
        // 형제 파일 KEventRepository 처럼 있는지 물어보고 읽는다.
        cal.SyncToken  = ReadOptional(r, "SyncToken");
        cal.SchoolCode = ReadOptional(r, "SchoolCode");
        return cal;
    }

    /// <summary>
    /// 있으면 읽고 없으면 빈 문자열. 컬럼이 없는 옛 DB 를 위한 자리다
    /// (없는 컬럼에 <c>GetOrdinal</c> 을 부르면 예외가 난다).
    /// </summary>
    private static string ReadOptional(SqliteDataReader r, string column)
    {
        for (int i = 0; i < r.FieldCount; i++)
        {
            if (!string.Equals(r.GetName(i), column, StringComparison.OrdinalIgnoreCase)) continue;
            return r.IsDBNull(i) ? string.Empty : r.GetString(i);
        }
        return string.Empty;
    }

    public async Task<KCalendarList?> GetByGoogleIdAsync(string googleId)
    {
        const string query = "SELECT * FROM KCalendarList WHERE GoogleId = @GoogleId LIMIT 1";
        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@GoogleId", googleId);
            using var reader = await cmd.ExecuteReaderAsync();
            return await reader.ReadAsync() ? Map(reader) : null;
        }
        catch (Exception ex)
        {
            LogError($"KCalendarList GoogleId 조회 실패: '{googleId}'", ex);
            throw;
        }
    }

    public async Task<List<KCalendarList>> GetSyncableAsync()
    {
        const string query = "SELECT * FROM KCalendarList WHERE SyncMode IN ('OneWay', 'TwoWay') AND GoogleId <> '' ORDER BY SortOrder";
        var list = new List<KCalendarList>();
        try
        {
            using var cmd = CreateCommand(query);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                list.Add(Map(reader));
            return list;
        }
        catch (Exception ex)
        {
            LogError("동기화 대상 캘린더 조회 실패", ex);
            throw;
        }
    }
}

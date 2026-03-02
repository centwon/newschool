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
            INSERT INTO KCalendarList (GoogleId, Title, Color, SortOrder, IsDefault, IsVisible, Updated, SyncMode, SyncToken)
            VALUES (@GoogleId, @Title, @Color, @SortOrder, @IsDefault, @IsVisible, @Updated, @SyncMode, @SyncToken);
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

        int maxOrder = 0;
        try
        {
            using var cmd = CreateCommand("SELECT COALESCE(MAX(SortOrder), 0) FROM KCalendarList");
            maxOrder = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }
        catch { /* ignore */ }

        var newCal = new KCalendarList
        {
            Title     = title,
            Color     = color,
            SortOrder = maxOrder + 1,
            IsDefault = isDefault,
            IsVisible = true,
            Updated   = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
        };
        return await CreateAsync(newCal);
    }

    public async Task<bool> UpdateAsync(KCalendarList cal)
    {
        const string query = @"
            UPDATE KCalendarList SET GoogleId=@GoogleId, Title=@Title, Color=@Color,
                SortOrder=@SortOrder, IsDefault=@IsDefault, IsVisible=@IsVisible,
                Updated=@Updated, SyncMode=@SyncMode, SyncToken=@SyncToken
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

    public async Task<bool> DeleteAsync(int no)
    {
        try
        {
            using var cmd = CreateCommand("DELETE FROM KCalendarList WHERE No = @No AND IsDefault = 0");
            cmd.Parameters.AddWithValue("@No", no);
            return await cmd.ExecuteNonQueryAsync() > 0;
        }
        catch (Exception ex)
        {
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
        // SyncToken 컬럼이 존재하면 읽기 (기존 DB 호환)
        try { cal.SyncToken = r.GetString(r.GetOrdinal("SyncToken")); }
        catch { cal.SyncToken = string.Empty; }
        return cal;
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

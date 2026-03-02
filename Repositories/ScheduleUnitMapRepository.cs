using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Models;

namespace NewSchool.Repositories;

/// <summary>
/// ScheduleUnitMap Repository
/// 수업-단원 매핑 관리 (1:N 병합 지원)
/// </summary>
public class ScheduleUnitMapRepository : BaseRepository
{
    public ScheduleUnitMapRepository(string dbPath) : base(dbPath)
    {
        EnsureTableExists();
    }

    #region Table Management

    private void EnsureTableExists()
    {
        const string sql = @"
            CREATE TABLE IF NOT EXISTS ScheduleUnitMap (
                No INTEGER PRIMARY KEY AUTOINCREMENT,
                ScheduleId INTEGER NOT NULL,
                CourseSectionId INTEGER NOT NULL,
                AllocatedHours INTEGER DEFAULT 1,
                OrderInSlot INTEGER DEFAULT 1,
                FOREIGN KEY (ScheduleId) REFERENCES Schedule(No) ON DELETE CASCADE,
                FOREIGN KEY (CourseSectionId) REFERENCES CourseSection(No) ON DELETE CASCADE
            );
            
            CREATE INDEX IF NOT EXISTS idx_schedule_unit_map_schedule ON ScheduleUnitMap(ScheduleId);
            CREATE INDEX IF NOT EXISTS idx_schedule_unit_map_section ON ScheduleUnitMap(CourseSectionId);
            CREATE UNIQUE INDEX IF NOT EXISTS idx_schedule_unit_map_unique ON ScheduleUnitMap(ScheduleId, CourseSectionId);
        ";

        try
        {
            using var cmd = CreateCommand(sql);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            LogError("ScheduleUnitMap 테이블 생성 실패", ex);
        }
    }

    #endregion

    #region Create

    /// <summary>
    /// 매핑 생성
    /// </summary>
    public async Task<int> CreateAsync(ScheduleUnitMap map)
    {
        const string query = @"
            INSERT INTO ScheduleUnitMap (
                ScheduleId, CourseSectionId, AllocatedHours, OrderInSlot
            ) VALUES (
                @ScheduleId, @CourseSectionId, @AllocatedHours, @OrderInSlot
            );
            SELECT last_insert_rowid();";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@ScheduleId", map.ScheduleId);
            cmd.Parameters.AddWithValue("@CourseSectionId", map.CourseSectionId);
            cmd.Parameters.AddWithValue("@AllocatedHours", map.AllocatedHours);
            cmd.Parameters.AddWithValue("@OrderInSlot", map.OrderInSlot);

            var result = await cmd.ExecuteScalarAsync();
            map.No = Convert.ToInt32(result);

            LogInfo($"매핑 생성 완료: Schedule={map.ScheduleId}, Section={map.CourseSectionId}");
            return map.No;
        }
        catch (Exception ex)
        {
            LogError($"매핑 생성 실패: Schedule={map.ScheduleId}, Section={map.CourseSectionId}", ex);
            throw;
        }
    }

    /// <summary>
    /// 스케줄에 단원 추가 (간편 메서드)
    /// </summary>
    public async Task<int> AddUnitToScheduleAsync(int scheduleId, int courseSectionId, int order = 0)
    {
        // 순서 자동 계산
        if (order <= 0)
        {
            order = await GetNextOrderAsync(scheduleId);
        }

        var map = new ScheduleUnitMap
        {
            ScheduleId = scheduleId,
            CourseSectionId = courseSectionId,
            AllocatedHours = 1,
            OrderInSlot = order
        };

        return await CreateAsync(map);
    }

    /// <summary>
    /// 일괄 매핑 생성
    /// </summary>
    public async Task<int> BulkCreateAsync(List<ScheduleUnitMap> maps)
    {
        int count = 0;
        foreach (var map in maps)
        {
            await CreateAsync(map);
            count++;
        }
        LogInfo($"매핑 일괄 생성 완료: {count}개");
        return count;
    }

    #endregion

    #region Read

    /// <summary>
    /// No로 매핑 조회
    /// </summary>
    public async Task<ScheduleUnitMap?> GetByIdAsync(int no)
    {
        const string query = "SELECT * FROM ScheduleUnitMap WHERE No = @No";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@No", no);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return MapScheduleUnitMap(reader);
            }
            return null;
        }
        catch (Exception ex)
        {
            LogError($"매핑 조회 실패: No={no}", ex);
            throw;
        }
    }

    /// <summary>
    /// 스케줄의 모든 매핑 조회
    /// </summary>
    public async Task<List<ScheduleUnitMap>> GetByScheduleAsync(int scheduleId)
    {
        const string query = @"
            SELECT * FROM ScheduleUnitMap 
            WHERE ScheduleId = @ScheduleId
            ORDER BY OrderInSlot";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@ScheduleId", scheduleId);

            return await ExecuteQueryAsync(cmd);
        }
        catch (Exception ex)
        {
            LogError($"스케줄별 매핑 조회 실패: ScheduleId={scheduleId}", ex);
            throw;
        }
    }

    /// <summary>
    /// 스케줄의 모든 매핑 조회 (CourseSection 포함)
    /// </summary>
    public async Task<List<ScheduleUnitMap>> GetByScheduleWithSectionAsync(int scheduleId)
    {
        const string query = @"
            SELECT m.*, 
                   cs.SectionName, cs.UnitNo, cs.ChapterNo, cs.SectionNo,
                   cs.SectionType
            FROM ScheduleUnitMap m
            INNER JOIN CourseSection cs ON m.CourseSectionId = cs.No
            WHERE m.ScheduleId = @ScheduleId
            ORDER BY m.OrderInSlot";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@ScheduleId", scheduleId);

            var maps = new List<ScheduleUnitMap>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var map = MapScheduleUnitMap(reader);
                
                // CourseSection 부분 정보 설정
                map.CourseSection = new CourseSection
                {
                    No = map.CourseSectionId,
                    SectionName = reader.GetString(reader.GetOrdinal("SectionName")),
                    UnitNo = reader.GetInt32(reader.GetOrdinal("UnitNo")),
                    ChapterNo = reader.GetInt32(reader.GetOrdinal("ChapterNo")),
                    SectionNo = reader.GetInt32(reader.GetOrdinal("SectionNo")),
                    SectionType = GetStringOrDefault(reader, "SectionType", "Normal")
                };
                
                maps.Add(map);
            }
            return maps;
        }
        catch (Exception ex)
        {
            LogError($"스케줄별 매핑(+Section) 조회 실패: ScheduleId={scheduleId}", ex);
            throw;
        }
    }

    /// <summary>
    /// 단원이 매핑된 모든 스케줄 조회
    /// </summary>
    public async Task<List<ScheduleUnitMap>> GetBySectionAsync(int courseSectionId)
    {
        const string query = @"
            SELECT * FROM ScheduleUnitMap 
            WHERE CourseSectionId = @CourseSectionId
            ORDER BY No";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@CourseSectionId", courseSectionId);

            return await ExecuteQueryAsync(cmd);
        }
        catch (Exception ex)
        {
            LogError($"단원별 매핑 조회 실패: CourseSectionId={courseSectionId}", ex);
            throw;
        }
    }

    /// <summary>
    /// 단원이 매핑된 스케줄 수 조회
    /// </summary>
    public async Task<int> GetCountBySectionAsync(int courseSectionId)
    {
        const string query = @"
            SELECT COUNT(*) FROM ScheduleUnitMap 
            WHERE CourseSectionId = @CourseSectionId";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@CourseSectionId", courseSectionId);

            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }
        catch (Exception ex)
        {
            LogError($"단원별 매핑 수 조회 실패: CourseSectionId={courseSectionId}", ex);
            throw;
        }
    }

    /// <summary>
    /// 특정 매핑 존재 여부 확인
    /// </summary>
    public async Task<bool> ExistsAsync(int scheduleId, int courseSectionId)
    {
        const string query = @"
            SELECT COUNT(*) FROM ScheduleUnitMap 
            WHERE ScheduleId = @ScheduleId AND CourseSectionId = @CourseSectionId";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@ScheduleId", scheduleId);
            cmd.Parameters.AddWithValue("@CourseSectionId", courseSectionId);

            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result) > 0;
        }
        catch (Exception ex)
        {
            LogError($"매핑 존재 확인 실패", ex);
            throw;
        }
    }

    /// <summary>
    /// 스케줄의 다음 순서 번호 조회
    /// </summary>
    public async Task<int> GetNextOrderAsync(int scheduleId)
    {
        const string query = @"
            SELECT COALESCE(MAX(OrderInSlot), 0) + 1 
            FROM ScheduleUnitMap 
            WHERE ScheduleId = @ScheduleId";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@ScheduleId", scheduleId);

            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }
        catch (Exception ex)
        {
            LogError($"다음 순서 조회 실패: ScheduleId={scheduleId}", ex);
            throw;
        }
    }

    /// <summary>
    /// 스케줄의 총 할당 시수 조회
    /// </summary>
    public async Task<int> GetTotalAllocatedHoursAsync(int scheduleId)
    {
        const string query = @"
            SELECT COALESCE(SUM(AllocatedHours), 0) 
            FROM ScheduleUnitMap 
            WHERE ScheduleId = @ScheduleId";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@ScheduleId", scheduleId);

            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }
        catch (Exception ex)
        {
            LogError($"총 할당 시수 조회 실패: ScheduleId={scheduleId}", ex);
            throw;
        }
    }

    #endregion

    #region Update

    /// <summary>
    /// 매핑 수정
    /// </summary>
    public async Task<bool> UpdateAsync(ScheduleUnitMap map)
    {
        const string query = @"
            UPDATE ScheduleUnitMap SET
                ScheduleId = @ScheduleId,
                CourseSectionId = @CourseSectionId,
                AllocatedHours = @AllocatedHours,
                OrderInSlot = @OrderInSlot
            WHERE No = @No";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@No", map.No);
            cmd.Parameters.AddWithValue("@ScheduleId", map.ScheduleId);
            cmd.Parameters.AddWithValue("@CourseSectionId", map.CourseSectionId);
            cmd.Parameters.AddWithValue("@AllocatedHours", map.AllocatedHours);
            cmd.Parameters.AddWithValue("@OrderInSlot", map.OrderInSlot);

            int affected = await cmd.ExecuteNonQueryAsync();
            bool success = affected > 0;

            if (success)
                LogInfo($"매핑 수정 완료: No={map.No}");

            return success;
        }
        catch (Exception ex)
        {
            LogError($"매핑 수정 실패: No={map.No}", ex);
            throw;
        }
    }

    /// <summary>
    /// 할당 시수 변경
    /// </summary>
    public async Task<bool> UpdateAllocatedHoursAsync(int no, int hours)
    {
        const string query = "UPDATE ScheduleUnitMap SET AllocatedHours = @Hours WHERE No = @No";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@No", no);
            cmd.Parameters.AddWithValue("@Hours", hours);

            return await cmd.ExecuteNonQueryAsync() > 0;
        }
        catch (Exception ex)
        {
            LogError($"할당 시수 변경 실패: No={no}", ex);
            throw;
        }
    }

    /// <summary>
    /// 순서 변경
    /// </summary>
    public async Task<bool> UpdateOrderAsync(int no, int order)
    {
        const string query = "UPDATE ScheduleUnitMap SET OrderInSlot = @Order WHERE No = @No";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@No", no);
            cmd.Parameters.AddWithValue("@Order", order);

            return await cmd.ExecuteNonQueryAsync() > 0;
        }
        catch (Exception ex)
        {
            LogError($"순서 변경 실패: No={no}", ex);
            throw;
        }
    }

    #endregion

    #region Delete

    /// <summary>
    /// 매핑 삭제
    /// </summary>
    public async Task<bool> DeleteAsync(int no)
    {
        const string query = "DELETE FROM ScheduleUnitMap WHERE No = @No";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@No", no);

            int affected = await cmd.ExecuteNonQueryAsync();
            bool success = affected > 0;

            if (success)
                LogInfo($"매핑 삭제 완료: No={no}");

            return success;
        }
        catch (Exception ex)
        {
            LogError($"매핑 삭제 실패: No={no}", ex);
            throw;
        }
    }

    /// <summary>
    /// 스케줄의 모든 매핑 삭제
    /// </summary>
    public async Task<int> DeleteByScheduleAsync(int scheduleId)
    {
        const string query = "DELETE FROM ScheduleUnitMap WHERE ScheduleId = @ScheduleId";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@ScheduleId", scheduleId);

            int affected = await cmd.ExecuteNonQueryAsync();
            LogInfo($"스케줄별 매핑 삭제: ScheduleId={scheduleId}, 삭제={affected}개");

            return affected;
        }
        catch (Exception ex)
        {
            LogError($"스케줄별 매핑 삭제 실패: ScheduleId={scheduleId}", ex);
            throw;
        }
    }

    /// <summary>
    /// 단원의 모든 매핑 삭제
    /// </summary>
    public async Task<int> DeleteBySectionAsync(int courseSectionId)
    {
        const string query = "DELETE FROM ScheduleUnitMap WHERE CourseSectionId = @CourseSectionId";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@CourseSectionId", courseSectionId);

            int affected = await cmd.ExecuteNonQueryAsync();
            LogInfo($"단원별 매핑 삭제: CourseSectionId={courseSectionId}, 삭제={affected}개");

            return affected;
        }
        catch (Exception ex)
        {
            LogError($"단원별 매핑 삭제 실패: CourseSectionId={courseSectionId}", ex);
            throw;
        }
    }

    /// <summary>
    /// 특정 스케줄에서 특정 단원 제거
    /// </summary>
    public async Task<bool> RemoveUnitFromScheduleAsync(int scheduleId, int courseSectionId)
    {
        const string query = @"
            DELETE FROM ScheduleUnitMap 
            WHERE ScheduleId = @ScheduleId AND CourseSectionId = @CourseSectionId";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@ScheduleId", scheduleId);
            cmd.Parameters.AddWithValue("@CourseSectionId", courseSectionId);

            int affected = await cmd.ExecuteNonQueryAsync();
            return affected > 0;
        }
        catch (Exception ex)
        {
            LogError($"스케줄에서 단원 제거 실패", ex);
            throw;
        }
    }

    #endregion

    #region Helper Methods

    private async Task<List<ScheduleUnitMap>> ExecuteQueryAsync(SqliteCommand cmd)
    {
        var maps = new List<ScheduleUnitMap>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            maps.Add(MapScheduleUnitMap(reader));
        }
        return maps;
    }

    private ScheduleUnitMap MapScheduleUnitMap(SqliteDataReader reader)
    {
        return new ScheduleUnitMap
        {
            No = reader.GetInt32(reader.GetOrdinal("No")),
            ScheduleId = reader.GetInt32(reader.GetOrdinal("ScheduleId")),
            CourseSectionId = reader.GetInt32(reader.GetOrdinal("CourseSectionId")),
            AllocatedHours = GetIntOrDefault(reader, "AllocatedHours", 1),
            OrderInSlot = GetIntOrDefault(reader, "OrderInSlot", 1)
        };
    }

    private int GetIntOrDefault(SqliteDataReader reader, string columnName, int defaultValue = 0)
    {
        try
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? defaultValue : reader.GetInt32(ordinal);
        }
        catch
        {
            return defaultValue;
        }
    }

    private string GetStringOrDefault(SqliteDataReader reader, string columnName, string defaultValue = "")
    {
        try
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? defaultValue : reader.GetString(ordinal);
        }
        catch
        {
            return defaultValue;
        }
    }

    #endregion
}

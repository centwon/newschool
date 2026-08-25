using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Models;

namespace NewSchool.Repositories;

/// <summary>
/// 교과 단원 Repository
/// 대단원 > 중단원 > 소단원 계층 구조 관리
/// </summary>
public class CourseSectionRepository : BaseRepository
{
    public CourseSectionRepository(string dbPath) : base(dbPath)
    {
        EnsureTableExists();
    }

    #region Table Management

    /// <summary>
    /// CourseSection 스키마 정본(기본 컬럼만 — 기존 DB 호환).
    /// <c>DatabaseInitializer</c> 가 이 상수를 함께 실행하고, 인덱스는
    /// <see cref="CreateIndexesIfNeeded"/> 가 붙인다.
    ///
    /// ⚠ 컬럼을 나중에 추가하는 장치는 <b>두지 않기로 한 것</b>이다(2026-08-25 결정, 재검토 금지).
    /// 예전 주석이 말하던 <c>AddNewColumnsIfNeeded</c> 는 실제로 존재하지 않는다 —
    /// <c>CREATE TABLE IF NOT EXISTS</c> 는 이미 만들어진 파일에 아무 일도 하지 않는다.
    /// 앱 전체가 같은 방침이다(대가와 배경은 <c>Board.cs</c> 의 같은 취지 주석에 적어 두었다).
    /// </summary>
    internal const string SchemaSql = @"
            CREATE TABLE IF NOT EXISTS CourseSection (
                No INTEGER PRIMARY KEY AUTOINCREMENT,
                Course INTEGER NOT NULL,
                UnitNo INTEGER NOT NULL,
                UnitName TEXT NOT NULL,
                ChapterNo INTEGER NOT NULL,
                ChapterName TEXT NOT NULL,
                SectionNo INTEGER NOT NULL,
                SectionName TEXT NOT NULL,
                StartPage INTEGER DEFAULT 0,
                EndPage INTEGER DEFAULT 0,
                EstimatedHours INTEGER DEFAULT 1,
                SortOrder INTEGER DEFAULT 0,
                FOREIGN KEY (Course) REFERENCES Course(No) ON DELETE CASCADE
            );
        ";

    private void EnsureTableExists()
    {
        try
        {
            using var cmd = CreateCommand(SchemaSql);
            cmd.ExecuteNonQuery();

            CreateIndexesIfNeeded();
        }
        catch (Exception ex)
        {
            LogError("CourseSection 테이블 생성 실패", ex);
        }
    }

    /// <summary>
    /// 인덱스 생성 (컬럼 추가 후 실행)
    /// </summary>
    private void CreateIndexesIfNeeded()
    {
        var indexes = new[]
        {
            "CREATE INDEX IF NOT EXISTS idx_coursesection_course ON CourseSection(Course)",
            "CREATE INDEX IF NOT EXISTS idx_coursesection_sort ON CourseSection(Course, SortOrder)"
        };

        foreach (var indexSql in indexes)
        {
            try
            {
                using var cmd = CreateCommand(indexSql);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                LogInfo($"인덱스 생성 스킵: {ex.Message}");
            }
        }
    }

    #endregion

    #region Create

    /// <summary>
    /// 단원 생성
    /// </summary>
    public async Task<int> CreateAsync(CourseSection section)
    {
        const string query = @"
            INSERT INTO CourseSection (
                Course, UnitNo, UnitName, ChapterNo, ChapterName,
                SectionNo, SectionName, StartPage, EndPage, EstimatedHours,
                SortOrder
            ) VALUES (
                @Course, @UnitNo, @UnitName, @ChapterNo, @ChapterName,
                @SectionNo, @SectionName, @StartPage, @EndPage, @EstimatedHours,
                @SortOrder
            );
            SELECT last_insert_rowid();";

        try
        {
            using var cmd = CreateCommand(query);
            AddParameters(cmd, section);

            var result = await cmd.ExecuteScalarAsync();
            section.No = Convert.ToInt32(result);

            LogInfo($"단원 생성 완료: No={section.No}, {section.FullPath}");
            return section.No;
        }
        catch (Exception ex)
        {
            LogError($"단원 생성 실패: {section.SectionName}", ex);
            throw;
        }
    }

    /// <summary>
    /// 단원 일괄 생성 (기존 데이터 삭제 후)
    /// </summary>
    public async Task<int> BulkCreateAsync(int courseNo, List<CourseSection> sections)
    {
        try
        {
            // 전체를 하나의 트랜잭션으로 처리 — 기존 단원(+진도·매핑 연쇄삭제)을 지운 뒤
            // 새 단원을 삽입하므로, 중간 실패 시 롤백되지 않으면 기존 데이터가 통째로 유실됨.
            // (CSV 가져오기 경로에서 호출됨) DeleteByCourseAsync·CreateAsync 는 CreateCommand 로
            // 활성 트랜잭션을 이어받으므로 여기서 감싸면 모두 원자적으로 커밋/롤백된다.
            return await ExecuteInTransactionAsync(async () =>
            {
                // 1. 기존 단원 삭제
                await DeleteByCourseAsync(courseNo);

                // 2. 새 단원 일괄 생성
                int count = 0;
                int sortOrder = 1;

                foreach (var section in sections)
                {
                    section.Course = courseNo;
                    section.SortOrder = sortOrder++;
                    await CreateAsync(section);
                    count++;
                }

                LogInfo($"단원 일괄 생성 완료: Course={courseNo}, {count}개");
                return count;
            });
        }
        catch (Exception ex)
        {
            LogError($"단원 일괄 생성 실패: Course={courseNo}", ex);
            throw;
        }
    }

    #endregion

    #region Read

    /// <summary>
    /// No로 단원 조회
    /// </summary>
    public async Task<CourseSection?> GetByIdAsync(int no)
    {
        const string query = "SELECT * FROM CourseSection WHERE No = @No";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@No", no);

            using var reader = await cmd.ExecuteReaderAsync();
            var cache = new ReaderColumnCache();
            cache.Initialize(reader);   // 컬럼 인덱스를 행마다 다시 찾지 않도록 한 번만
            if (await reader.ReadAsync())
            {
                return MapSection(reader, cache);
            }
            return null;
        }
        catch (Exception ex)
        {
            LogError($"단원 조회 실패: No={no}", ex);
            throw;
        }
    }

    /// <summary>
    /// 과목별 단원 목록 조회 (정렬순)
    /// </summary>
    public async Task<List<CourseSection>> GetByCourseAsync(int courseNo)
    {
        const string query = @"
            SELECT * FROM CourseSection
            WHERE Course = @Course
            ORDER BY SortOrder, UnitNo, ChapterNo, SectionNo";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@Course", courseNo);

            return await ExecuteQueryAsync(cmd);
        }
        catch (Exception ex)
        {
            LogError($"과목별 단원 조회 실패: Course={courseNo}", ex);
            throw;
        }
    }

    /// <summary>
    /// 과목의 총 예상 차시 조회
    /// </summary>
    public async Task<int> GetTotalEstimatedHoursAsync(int courseNo)
    {
        const string query = @"
            SELECT COALESCE(SUM(EstimatedHours), 0)
            FROM CourseSection
            WHERE Course = @Course";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@Course", courseNo);

            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }
        catch (Exception ex)
        {
            LogError($"총 예상 차시 조회 실패: Course={courseNo}", ex);
            throw;
        }
    }

    // 대단원 목록만 뽑는 GetUnitsAsync 는 호출부가 없어 지웠다(39차).
    // 화면은 단원 전체(GetByCourseAsync)를 받아 대단원으로 묶는다.

    #endregion

    #region Update

    /// <summary>
    /// 단원 수정
    /// </summary>
    public async Task<bool> UpdateAsync(CourseSection section)
    {
        const string query = @"
            UPDATE CourseSection SET
                UnitNo = @UnitNo,
                UnitName = @UnitName,
                ChapterNo = @ChapterNo,
                ChapterName = @ChapterName,
                SectionNo = @SectionNo,
                SectionName = @SectionName,
                StartPage = @StartPage,
                EndPage = @EndPage,
                EstimatedHours = @EstimatedHours,
                SortOrder = @SortOrder
            WHERE No = @No";

        try
        {
            using var cmd = CreateCommand(query);
            AddParameters(cmd, section);

            int affected = await cmd.ExecuteNonQueryAsync();
            bool success = affected > 0;

            if (success)
                LogInfo($"단원 수정 완료: No={section.No}");

            return success;
        }
        catch (Exception ex)
        {
            LogError($"단원 수정 실패: No={section.No}", ex);
            throw;
        }
    }

    /// <summary>
    /// SortOrder 일괄 업데이트 (드래그 앤 드롭 시 사용)
    /// </summary>
    public async Task<int> BulkUpdateSortOrderAsync(List<CourseSection> sections)
    {
        if (sections == null || sections.Count == 0)
        {
            LogWarning("BulkUpdateSortOrderAsync: sections가 null이거나 비어있음");
            return 0;
        }

        try
        {
            LogInfo($"BulkUpdateSortOrderAsync 시작: {sections.Count}개 단원");
            
            return await ExecuteInTransactionAsync(async () =>
            {
                int count = 0;
                const string query = "UPDATE CourseSection SET SortOrder = @SortOrder WHERE No = @No";
                
                foreach (var section in sections)
                {
                    LogDebug($"UPDATE 실행 중: No={section.No}, SortOrder={section.SortOrder}");
                    
                    using var cmd = CreateCommand(query);
                    cmd.Parameters.AddWithValue("@No", section.No);
                    cmd.Parameters.AddWithValue("@SortOrder", section.SortOrder);
                    
                    int affected = await cmd.ExecuteNonQueryAsync();
                    LogDebug($"  영향받은 행: {affected}");
                    
                    if (affected == 0)
                    {
                        LogWarning($"  경고: No={section.No}에 해당하는 행이 없음");
                    }
                    
                    count++;
                }
                
                LogInfo($"SortOrder 일괄 업데이트 완료: {count}개");
                return count;
            });
        }
        catch (Exception ex)
        {
            LogError("SortOrder 일괄 업데이트 실패", ex);
            throw;
        }
    }

    #endregion

    #region Delete

    /// <summary>
    /// 단원 삭제
    /// </summary>
    public async Task<bool> DeleteAsync(int no)
    {
        const string query = "DELETE FROM CourseSection WHERE No = @No";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@No", no);

            int affected = await cmd.ExecuteNonQueryAsync();
            bool success = affected > 0;

            if (success)
                LogInfo($"단원 삭제 완료: No={no}");

            return success;
        }
        catch (Exception ex)
        {
            LogError($"단원 삭제 실패: No={no}", ex);
            throw;
        }
    }

    /// <summary>
    /// 과목의 모든 단원 삭제 (관련 데이터 함께 삭제)
    /// </summary>
    public async Task<int> DeleteByCourseAsync(int courseNo)
    {
        try
        {
            // 1. 먼저 해당 과목의 단원 ID 목록 조회
            var sectionIds = new List<int>();
            const string selectQuery = "SELECT No FROM CourseSection WHERE Course = @Course";
            using (var selectCmd = CreateCommand(selectQuery))
            {
                selectCmd.Parameters.AddWithValue("@Course", courseNo);
                using var reader = await selectCmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    sectionIds.Add(reader.GetInt32(0));
                }
            }

            if (sectionIds.Count == 0)
            {
                LogInfo($"과목별 단원 삭제: Course={courseNo}, 삭제할 단원 없음");
                return 0;
            }

            // 2. CourseSection 삭제
            const string query = "DELETE FROM CourseSection WHERE Course = @Course";
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@Course", courseNo);

            int affected = await cmd.ExecuteNonQueryAsync();
            LogInfo($"과목별 단원 삭제: Course={courseNo}, 삭제={affected}개");

            return affected;
        }
        catch (Exception ex)
        {
            LogError($"과목별 단원 삭제 실패: Course={courseNo}", ex);
            throw;
        }
    }

    #endregion

    #region Helper Methods

    private void AddParameters(SqliteCommand cmd, CourseSection section)
    {
        cmd.Parameters.AddWithValue("@No", section.No);
        cmd.Parameters.AddWithValue("@Course", section.Course);
        cmd.Parameters.AddWithValue("@UnitNo", section.UnitNo);
        cmd.Parameters.AddWithValue("@UnitName", section.UnitName);
        cmd.Parameters.AddWithValue("@ChapterNo", section.ChapterNo);
        cmd.Parameters.AddWithValue("@ChapterName", section.ChapterName);
        cmd.Parameters.AddWithValue("@SectionNo", section.SectionNo);
        cmd.Parameters.AddWithValue("@SectionName", section.SectionName);
        cmd.Parameters.AddWithValue("@StartPage", section.StartPage);
        cmd.Parameters.AddWithValue("@EndPage", section.EndPage);
        cmd.Parameters.AddWithValue("@EstimatedHours", section.EstimatedHours);
        cmd.Parameters.AddWithValue("@SortOrder", section.SortOrder);
    }

    private async Task<List<CourseSection>> ExecuteQueryAsync(SqliteCommand cmd)
    {
        var sections = new List<CourseSection>();
        using var reader = await cmd.ExecuteReaderAsync();
        var cache = new ReaderColumnCache();
        cache.Initialize(reader);   // 컬럼 인덱스를 행마다 다시 찾지 않도록 한 번만
        while (await reader.ReadAsync())
        {
            sections.Add(MapSection(reader, cache));
        }
        return sections;
    }

    private CourseSection MapSection(SqliteDataReader reader, ReaderColumnCache cache)
    {
        var section = new CourseSection
        {
            No = reader.GetInt32(cache.GetOrdinal("No")),
            Course = reader.GetInt32(cache.GetOrdinal("Course")),
            UnitNo = reader.GetInt32(cache.GetOrdinal("UnitNo")),
            UnitName = reader.GetString(cache.GetOrdinal("UnitName")),
            ChapterNo = reader.GetInt32(cache.GetOrdinal("ChapterNo")),
            ChapterName = reader.GetString(cache.GetOrdinal("ChapterName")),
            SectionNo = reader.GetInt32(cache.GetOrdinal("SectionNo")),
            SectionName = reader.GetString(cache.GetOrdinal("SectionName")),
            StartPage = GetIntOrDefault(reader, cache, "StartPage", 0),
            EndPage = GetIntOrDefault(reader, cache, "EndPage", 0),
            EstimatedHours = GetIntOrDefault(reader, cache, "EstimatedHours", 1),
            SortOrder = GetIntOrDefault(reader, cache, "SortOrder", 0)
        };

        return section;
    }

    private int GetIntOrDefault(SqliteDataReader reader, ReaderColumnCache cache, string columnName, int defaultValue = 0)
    {
        try
        {
            var ordinal = cache.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? defaultValue : reader.GetInt32(ordinal);
        }
        catch
        {
            return defaultValue;
        }
    }

    // 미사용 메서드 제거 (2026-08-19): GetStringOrDefault — 호출처 0건

    #endregion
}

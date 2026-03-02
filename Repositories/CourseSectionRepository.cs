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

    private void EnsureTableExists()
    {
        // 1. 테이블 생성 (기본 컬럼만 - 기존 DB 호환)
        const string createTableSql = @"
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

        try
        {
            using var cmd = CreateCommand(createTableSql);
            cmd.ExecuteNonQuery();

            // 2. 기존 테이블에 새 컬럼 추가 (v2 마이그레이션)
            AddNewColumnsIfNeeded();

            // 3. 인덱스 생성 (컬럼 추가 후)
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
            "CREATE INDEX IF NOT EXISTS idx_coursesection_sort ON CourseSection(Course, SortOrder)",
            "CREATE INDEX IF NOT EXISTS idx_coursesection_type ON CourseSection(Course, SectionType)",
            "CREATE INDEX IF NOT EXISTS idx_coursesection_pinned ON CourseSection(Course, IsPinned)"
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

    /// <summary>
    /// v2 신규 컬럼 추가 (기존 DB 마이그레이션)
    /// </summary>
    private void AddNewColumnsIfNeeded()
    {
        var columns = new Dictionary<string, string>
        {
            { "LessonPlan", "TEXT DEFAULT ''" },
            { "EndPage", "INTEGER DEFAULT 0" },
            { "SectionType", "TEXT DEFAULT 'Normal'" },
            { "IsPinned", "INTEGER DEFAULT 0" },
            { "PinnedDate", "TEXT" },
            { "LearningObjective", "TEXT DEFAULT ''" },
            { "MaterialPath", "TEXT DEFAULT ''" },
            { "MaterialUrl", "TEXT DEFAULT ''" },
            { "Memo", "TEXT DEFAULT ''" }
        };

        foreach (var (columnName, columnDef) in columns)
        {
            try
            {
                string checkSql = $"SELECT COUNT(*) FROM pragma_table_info('CourseSection') WHERE name='{columnName}'";
                using var checkCmd = CreateCommand(checkSql);
                var exists = Convert.ToInt32(checkCmd.ExecuteScalar()) > 0;

                if (!exists)
                {
                    string alterSql = $"ALTER TABLE CourseSection ADD COLUMN {columnName} {columnDef}";
                    using var alterCmd = CreateCommand(alterSql);
                    alterCmd.ExecuteNonQuery();
                    LogInfo($"CourseSection.{columnName} 컬럼 추가 완료");
                }
            }
            catch (Exception ex)
            {
                LogError($"{columnName} 컬럼 추가 실패", ex);
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
                SortOrder, LessonPlan, SectionType, IsPinned, PinnedDate,
                LearningObjective, MaterialPath, MaterialUrl, Memo
            ) VALUES (
                @Course, @UnitNo, @UnitName, @ChapterNo, @ChapterName,
                @SectionNo, @SectionName, @StartPage, @EndPage, @EstimatedHours,
                @SortOrder, @LessonPlan, @SectionType, @IsPinned, @PinnedDate,
                @LearningObjective, @MaterialPath, @MaterialUrl, @Memo
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
            if (await reader.ReadAsync())
            {
                return MapSection(reader);
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
    /// 고정된 단원 조회 (Anchor 배치용)
    /// </summary>
    public async Task<List<CourseSection>> GetPinnedSectionsAsync(int courseNo)
    {
        const string query = @"
            SELECT * FROM CourseSection
            WHERE Course = @Course AND (IsPinned = 1 OR SectionType IN ('Exam', 'Assessment'))
            ORDER BY PinnedDate, SortOrder";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@Course", courseNo);

            return await ExecuteQueryAsync(cmd);
        }
        catch (Exception ex)
        {
            LogError($"고정 단원 조회 실패: Course={courseNo}", ex);
            throw;
        }
    }

    /// <summary>
    /// 일반 단원 조회 (Fill 배치용 - 고정되지 않은 것만)
    /// </summary>
    public async Task<List<CourseSection>> GetNormalSectionsAsync(int courseNo)
    {
        const string query = @"
            SELECT * FROM CourseSection
            WHERE Course = @Course 
              AND IsPinned = 0 
              AND SectionType NOT IN ('Exam', 'Assessment')
            ORDER BY SortOrder, UnitNo, ChapterNo, SectionNo";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@Course", courseNo);

            return await ExecuteQueryAsync(cmd);
        }
        catch (Exception ex)
        {
            LogError($"일반 단원 조회 실패: Course={courseNo}", ex);
            throw;
        }
    }

    /// <summary>
    /// 유형별 단원 조회
    /// </summary>
    public async Task<List<CourseSection>> GetByTypeAsync(int courseNo, string sectionType)
    {
        const string query = @"
            SELECT * FROM CourseSection
            WHERE Course = @Course AND SectionType = @SectionType
            ORDER BY SortOrder, UnitNo, ChapterNo, SectionNo";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@Course", courseNo);
            cmd.Parameters.AddWithValue("@SectionType", sectionType);

            return await ExecuteQueryAsync(cmd);
        }
        catch (Exception ex)
        {
            LogError($"유형별 단원 조회 실패: Course={courseNo}, Type={sectionType}", ex);
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

    /// <summary>
    /// 유형별 차시 통계
    /// </summary>
    public async Task<Dictionary<string, int>> GetHoursByTypeAsync(int courseNo)
    {
        const string query = @"
            SELECT SectionType, COALESCE(SUM(EstimatedHours), 0) as TotalHours
            FROM CourseSection
            WHERE Course = @Course
            GROUP BY SectionType";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@Course", courseNo);

            var result = new Dictionary<string, int>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var type = reader.GetString(0);
                var hours = reader.GetInt32(1);
                result[type] = hours;
            }
            return result;
        }
        catch (Exception ex)
        {
            LogError($"유형별 차시 통계 조회 실패: Course={courseNo}", ex);
            throw;
        }
    }

    /// <summary>
    /// 대단원 목록 조회 (중복 제거)
    /// </summary>
    public async Task<List<(int UnitNo, string UnitName)>> GetUnitsAsync(int courseNo)
    {
        const string query = @"
            SELECT DISTINCT UnitNo, UnitName
            FROM CourseSection
            WHERE Course = @Course
            ORDER BY UnitNo";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@Course", courseNo);

            var units = new List<(int, string)>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                units.Add((
                    reader.GetInt32(reader.GetOrdinal("UnitNo")),
                    reader.GetString(reader.GetOrdinal("UnitName"))
                ));
            }
            return units;
        }
        catch (Exception ex)
        {
            LogError($"대단원 목록 조회 실패: Course={courseNo}", ex);
            throw;
        }
    }

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
                SortOrder = @SortOrder,
                LessonPlan = @LessonPlan,
                SectionType = @SectionType,
                IsPinned = @IsPinned,
                PinnedDate = @PinnedDate,
                LearningObjective = @LearningObjective,
                MaterialPath = @MaterialPath,
                MaterialUrl = @MaterialUrl,
                Memo = @Memo
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
    /// 고정 날짜 설정
    /// </summary>
    public async Task<bool> SetPinnedDateAsync(int no, DateTime? pinnedDate)
    {
        const string query = @"
            UPDATE CourseSection SET
                IsPinned = @IsPinned,
                PinnedDate = @PinnedDate
            WHERE No = @No";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@No", no);
            cmd.Parameters.AddWithValue("@IsPinned", pinnedDate.HasValue ? 1 : 0);
            cmd.Parameters.AddWithValue("@PinnedDate", pinnedDate?.ToString("yyyy-MM-dd") ?? (object)DBNull.Value);

            int affected = await cmd.ExecuteNonQueryAsync();
            return affected > 0;
        }
        catch (Exception ex)
        {
            LogError($"고정 날짜 설정 실패: No={no}", ex);
            throw;
        }
    }

    /// <summary>
    /// 유형 변경
    /// </summary>
    public async Task<bool> SetSectionTypeAsync(int no, string sectionType)
    {
        const string query = "UPDATE CourseSection SET SectionType = @SectionType WHERE No = @No";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@No", no);
            cmd.Parameters.AddWithValue("@SectionType", sectionType);

            int affected = await cmd.ExecuteNonQueryAsync();
            return affected > 0;
        }
        catch (Exception ex)
        {
            LogError($"유형 변경 실패: No={no}", ex);
            throw;
        }
    }

    /// <summary>
    /// 메모 업데이트
    /// </summary>
    public async Task<bool> UpdateMemoAsync(int no, string memo)
    {
        const string query = "UPDATE CourseSection SET Memo = @Memo WHERE No = @No";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@No", no);
            cmd.Parameters.AddWithValue("@Memo", memo);

            int affected = await cmd.ExecuteNonQueryAsync();
            return affected > 0;
        }
        catch (Exception ex)
        {
            LogError($"메모 업데이트 실패: No={no}", ex);
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

            // 2. LessonProgress 삭제 (외래 키 제약)
            string progressDeleteQuery = $"DELETE FROM LessonProgress WHERE CourseSectionId IN ({string.Join(",", sectionIds)})";
            try
            {
                using var progressCmd = CreateCommand(progressDeleteQuery);
                await progressCmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                LogInfo($"LessonProgress 삭제 스킵 (테이블 없음): {ex.Message}");
            }

            // 3. ScheduleUnitMap 삭제 (외래 키 제약)
            string mapDeleteQuery = $"DELETE FROM ScheduleUnitMap WHERE CourseSectionId IN ({string.Join(",", sectionIds)})";
            try
            {
                using var mapCmd = CreateCommand(mapDeleteQuery);
                await mapCmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                LogInfo($"ScheduleUnitMap 삭제 스킵 (테이블 없음): {ex.Message}");
            }

            // 4. CourseSection 삭제
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
        cmd.Parameters.AddWithValue("@LessonPlan", section.LessonPlan ?? string.Empty);
        cmd.Parameters.AddWithValue("@SectionType", section.SectionType ?? "Normal");
        cmd.Parameters.AddWithValue("@IsPinned", section.IsPinned ? 1 : 0);
        cmd.Parameters.AddWithValue("@PinnedDate", section.PinnedDate?.ToString("yyyy-MM-dd") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@LearningObjective", section.LearningObjective ?? string.Empty);
        cmd.Parameters.AddWithValue("@MaterialPath", section.MaterialPath ?? string.Empty);
        cmd.Parameters.AddWithValue("@MaterialUrl", section.MaterialUrl ?? string.Empty);
        cmd.Parameters.AddWithValue("@Memo", section.Memo ?? string.Empty);
    }

    private async Task<List<CourseSection>> ExecuteQueryAsync(SqliteCommand cmd)
    {
        var sections = new List<CourseSection>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            sections.Add(MapSection(reader));
        }
        return sections;
    }

    private CourseSection MapSection(SqliteDataReader reader)
    {
        var section = new CourseSection
        {
            No = reader.GetInt32(reader.GetOrdinal("No")),
            Course = reader.GetInt32(reader.GetOrdinal("Course")),
            UnitNo = reader.GetInt32(reader.GetOrdinal("UnitNo")),
            UnitName = reader.GetString(reader.GetOrdinal("UnitName")),
            ChapterNo = reader.GetInt32(reader.GetOrdinal("ChapterNo")),
            ChapterName = reader.GetString(reader.GetOrdinal("ChapterName")),
            SectionNo = reader.GetInt32(reader.GetOrdinal("SectionNo")),
            SectionName = reader.GetString(reader.GetOrdinal("SectionName")),
            StartPage = GetIntOrDefault(reader, "StartPage", 0),
            EndPage = GetIntOrDefault(reader, "EndPage", 0),
            EstimatedHours = GetIntOrDefault(reader, "EstimatedHours", 1),
            SortOrder = GetIntOrDefault(reader, "SortOrder", 0),
            LessonPlan = GetStringOrDefault(reader, "LessonPlan"),
            SectionType = GetStringOrDefault(reader, "SectionType", "Normal"),
            IsPinned = GetIntOrDefault(reader, "IsPinned", 0) == 1,
            LearningObjective = GetStringOrDefault(reader, "LearningObjective"),
            MaterialPath = GetStringOrDefault(reader, "MaterialPath"),
            MaterialUrl = GetStringOrDefault(reader, "MaterialUrl"),
            Memo = GetStringOrDefault(reader, "Memo")
        };

        // PinnedDate 파싱
        var pinnedDateStr = GetStringOrDefault(reader, "PinnedDate");
        if (!string.IsNullOrEmpty(pinnedDateStr) && DateTime.TryParse(pinnedDateStr, out var pinnedDate))
        {
            section.PinnedDate = pinnedDate;
        }

        return section;
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

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Models;

namespace NewSchool.Repositories;

/// <summary>
/// TeacherSchoolHistory Repository
/// 교사 근무 이력 (학교별) 관리
/// </summary>
public class TeacherSchoolHistoryRepository : BaseRepository
{
    public TeacherSchoolHistoryRepository(string dbPath) : base(dbPath) { }

    /// <summary>
    /// 다른 Repository 와 <b>한 연결·한 트랜잭션</b>을 공유한다.
    /// 교사와 근무 이력은 함께 만들어져야 하는데, 각자 연결을 열면 트랜잭션이 공유되지 않는다.
    /// </summary>
    public TeacherSchoolHistoryRepository(SqliteConnection connection) : base(connection) { }

    #region Create

    /// <summary>
    /// 교사 근무 이력 생성
    /// </summary>
    public async Task<int> CreateAsync(TeacherSchoolHistory history)
    {
        const string query = @"
                INSERT INTO TeacherSchoolHistory (
                    TeacherID, SchoolCode, StartDate, EndDate, IsCurrent,
                    Position, Role, Memo, CreatedAt, UpdatedAt
                ) VALUES (
                    @TeacherID, @SchoolCode, @StartDate, @EndDate, @IsCurrent,
                    @Position, @Role, @Memo, @CreatedAt, @UpdatedAt
                );
                SELECT last_insert_rowid();";

        try
        {
            using var cmd = CreateCommand(query);
            AddHistoryParameters(cmd, history);

            var result = await cmd.ExecuteScalarAsync();
            history.No = Convert.ToInt32(result);

            LogInfo($"교사 근무이력 생성 완료: No={history.No}, TeacherID={history.TeacherID}");
            return history.No;
        }
        catch (Exception ex)
        {
            LogError($"교사 근무이력 생성 실패: TeacherID={history.TeacherID}", ex);
            throw;
        }
    }

    #endregion

    #region Read

    /// <summary>
    /// 교사의 모든 근무 이력 조회 (최신순)
    /// </summary>
    public async Task<List<TeacherSchoolHistory>> GetByTeacherIdAsync(string teacherId)
    {
        const string query = @"
                SELECT * FROM TeacherSchoolHistory 
                WHERE TeacherID = @TeacherID 
                ORDER BY StartDate DESC";

        var histories = new List<TeacherSchoolHistory>();

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@TeacherID", teacherId);

            using var reader = await cmd.ExecuteReaderAsync();
            var cache = new ReaderColumnCache();
            cache.Initialize(reader);   // 컬럼 인덱스를 행마다 다시 찾지 않도록 한 번만
            while (await reader.ReadAsync())
            {
                histories.Add(MapHistory(reader, cache));
            }

            LogInfo($"교사 근무이력 조회 완료: TeacherID={teacherId}, Count={histories.Count}");
            return histories;
        }
        catch (Exception ex)
        {
            LogError($"교사 근무이력 조회 실패: TeacherID={teacherId}", ex);
            throw;
        }
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// TeacherSchoolHistory 파라미터 추가
    /// </summary>
    private void AddHistoryParameters(SqliteCommand cmd, TeacherSchoolHistory history)
    {
        cmd.Parameters.AddWithValue("@TeacherID", history.TeacherID);
        cmd.Parameters.AddWithValue("@SchoolCode", history.SchoolCode);
        cmd.Parameters.AddWithValue("@StartDate", history.StartDate);
        cmd.Parameters.AddWithValue("@EndDate", history.EndDate ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@IsCurrent", history.IsCurrent ? 1 : 0);
        cmd.Parameters.AddWithValue("@Position", history.Position ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@Role", history.Role ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@Memo", history.Memo ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@CreatedAt", history.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("@UpdatedAt", history.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
    }

    /// <summary>
    /// SqliteDataReader를 TeacherSchoolHistory로 매핑
    /// </summary>
    private TeacherSchoolHistory MapHistory(SqliteDataReader reader, ReaderColumnCache cache)
    {
        return new TeacherSchoolHistory
        {
            No = reader.GetInt32(cache.GetOrdinal("No")),
            TeacherID = reader.GetString(cache.GetOrdinal("TeacherID")),
            SchoolCode = reader.GetString(cache.GetOrdinal("SchoolCode")),
            StartDate = reader.GetString(cache.GetOrdinal("StartDate")),
            EndDate = GetStringOrEmpty(reader, cache, "EndDate"),
            IsCurrent = reader.GetInt32(cache.GetOrdinal("IsCurrent")) == 1,
            Position = GetStringOrEmpty(reader, cache, "Position"),
            Role = GetStringOrEmpty(reader, cache, "Role"),
            Memo = GetStringOrEmpty(reader, cache, "Memo"),
            CreatedAt = DateTime.Parse(reader.GetString(cache.GetOrdinal("CreatedAt"))),
            UpdatedAt = DateTime.Parse(reader.GetString(cache.GetOrdinal("UpdatedAt")))
        };
    }

    private string GetStringOrEmpty(SqliteDataReader reader, ReaderColumnCache cache, string columnName)
    {
        int ordinal = cache.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
    }

    #endregion
}
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Models;

namespace NewSchool.Repositories;

/// <summary>
/// Club Repository
/// 동아리 정보 관리
/// </summary>
public class ClubRepository : BaseRepository
{
    public ClubRepository(string dbPath) : base(dbPath) { }

    #region Create

    /// <summary>
    /// 동아리 생성
    /// </summary>
    public async Task<int> CreateAsync(Club club)
    {
        const string query = @"
                INSERT INTO Club (
                    SchoolCode, TeacherID, Year, ClubName, ActivityRoom, Remark,
                    CreatedAt, UpdatedAt, IsDeleted
                ) VALUES (
                    @SchoolCode, @TeacherID, @Year, @ClubName, @ActivityRoom, @Remark,
                    @CreatedAt, @UpdatedAt, @IsDeleted
                );
                SELECT last_insert_rowid();";

        try
        {
            using var cmd = CreateCommand(query);
            AddClubParameters(cmd, club);

            var result = await cmd.ExecuteScalarAsync();
            club.No = Convert.ToInt32(result);

            LogInfo($"동아리 생성 완료: No={club.No}, ClubName={club.ClubName}");
            return club.No;
        }
        catch (Exception ex)
        {
            LogError($"동아리 생성 실패: ClubName={club.ClubName}", ex);
            throw;
        }
    }

    #endregion

    #region Read

    /// <summary>
    /// No로 동아리 조회
    /// </summary>
    public async Task<Club?> GetByIdAsync(int no)
    {
        const string query = "SELECT * FROM Club WHERE No = @No AND IsDeleted = 0";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@No", no);

            using var reader = await cmd.ExecuteReaderAsync();
            var cache = new ReaderColumnCache();
            cache.Initialize(reader);   // 컬럼 인덱스를 행마다 다시 찾지 않도록 한 번만
            if (await reader.ReadAsync())
            {
                return MapClub(reader, cache);
            }

            return null;
        }
        catch (Exception ex)
        {
            LogError($"동아리 조회 실패: No={no}", ex);
            throw;
        }
    }

    /// <summary>
    /// 학교/학년도별 동아리 목록 조회
    /// </summary>
    public async Task<List<Club>> GetBySchoolAsync(string schoolCode, int year)
    {
        const string query = @"
                SELECT * FROM Club 
                WHERE SchoolCode = @SchoolCode 
                  AND Year = @Year
                  AND IsDeleted = 0
                ORDER BY ClubName";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@SchoolCode", schoolCode);
            cmd.Parameters.AddWithValue("@Year", year);

            var clubs = new List<Club>();
            using var reader = await cmd.ExecuteReaderAsync();
            var cache = new ReaderColumnCache();
            cache.Initialize(reader);   // 컬럼 인덱스를 행마다 다시 찾지 않도록 한 번만
            while (await reader.ReadAsync())
            {
                clubs.Add(MapClub(reader, cache));
            }

            return clubs;
        }
        catch (Exception ex)
        {
            LogError($"동아리 목록 조회 실패: SchoolCode={schoolCode}, Year={year}", ex);
            throw;
        }
    }

    /// <summary>
    /// 교사별 동아리 목록 조회
    /// </summary>
    public async Task<List<Club>> GetByTeacherAsync(string teacherId, int year)
    {
        const string query = @"
                SELECT * FROM Club 
                WHERE TeacherID = @TeacherID 
                  AND Year = @Year
                  AND IsDeleted = 0
                ORDER BY ClubName";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@TeacherID", teacherId);
            cmd.Parameters.AddWithValue("@Year", year);

            var clubs = new List<Club>();
            using var reader = await cmd.ExecuteReaderAsync();
            var cache = new ReaderColumnCache();
            cache.Initialize(reader);   // 컬럼 인덱스를 행마다 다시 찾지 않도록 한 번만
            while (await reader.ReadAsync())
            {
                clubs.Add(MapClub(reader, cache));
            }

            return clubs;
        }
        catch (Exception ex)
        {
            LogError($"교사별 동아리 목록 조회 실패: TeacherID={teacherId}", ex);
            throw;
        }
    }

    /// <summary>
    /// 모든 동아리 목록 조회 (삭제되지 않은 것만)
    /// </summary>
    public async Task<List<Club>> GetAllAsync()
    {
        const string query = @"
                SELECT * FROM Club 
                WHERE IsDeleted = 0
                ORDER BY Year DESC, ClubName";

        try
        {
            var clubs = new List<Club>();
            using var cmd = CreateCommand(query);
            using var reader = await cmd.ExecuteReaderAsync();
            var cache = new ReaderColumnCache();
            cache.Initialize(reader);   // 컬럼 인덱스를 행마다 다시 찾지 않도록 한 번만
            while (await reader.ReadAsync())
            {
                clubs.Add(MapClub(reader, cache));
            }

            return clubs;
        }
        catch (Exception ex)
        {
            LogError("모든 동아리 조회 실패", ex);
            throw;
        }
    }

    #endregion

    #region Update

    /// <summary>
    /// 동아리 정보 수정
    /// </summary>
    public async Task<bool> UpdateAsync(Club club)
    {
        const string query = @"
                UPDATE Club SET
                    SchoolCode = @SchoolCode,
                    TeacherID = @TeacherID,
                    Year = @Year,
                    ClubName = @ClubName,
                    ActivityRoom = @ActivityRoom,
                    Remark = @Remark,
                    UpdatedAt = @UpdatedAt
                WHERE No = @No";

        try
        {
            using var cmd = CreateCommand(query);
            AddClubParameters(cmd, club);

            int affected = await cmd.ExecuteNonQueryAsync();
            bool success = affected > 0;

            if (success)
                LogInfo($"동아리 수정 완료: No={club.No}");
            else
                LogWarning($"동아리 수정 실패: No={club.No}");

            return success;
        }
        catch (Exception ex)
        {
            LogError($"동아리 수정 실패: No={club.No}", ex);
            throw;
        }
    }

    #endregion

    #region Hide

    /// <summary>
    /// 동아리를 <b>목록에서 감춘다</b>(행은 남는다).
    ///
    /// <para>⚠ 이 메서드의 이름이 <c>DeleteAsync</c> 였을 때 실제로 혼란이 생겼다.
    /// <c>ClubEnrollment.ClubNo</c> 에는 <c>ON DELETE CASCADE</c> 가 걸려 있어 스키마만 보면
    /// "동아리를 지우면 부원 배정도 지워진다"로 읽힌다. 그런데 화면은 "부원 배정 기록은
    /// 그대로 보관됩니다" 라고 말한다. 둘 다 맞다 — <b>여기서 행을 지우지 않으므로 CASCADE 가
    /// 애초에 깨어나지 않기</b> 때문이다. 이름이 <c>Delete</c> 인 동안에는 그 사실을 코드를
    /// 좇아 들어가야만 알 수 있었다(46차 감사에서 두 번 오판할 뻔했다).</para>
    ///
    /// <para>그래서 이름을 <c>Hide</c> 로 바꾼다 — <b>이것은 삭제가 아니다.</b> 호출부는
    /// 이름만 보고 자식이 살아남는다는 것을 안다.</para>
    ///
    /// <para>⚠ 누군가 이것을 진짜 삭제로 바꾸면 <b>부원 배정이 함께 사라지고 화면 문구가
    /// 거짓이 된다.</b> <c>DeleteCascadeTests.동아리_삭제는_부원_배정을_남긴다</c> 가 먼저 깨진다.</para>
    /// </summary>
    public async Task<bool> HideAsync(int no)
    {
        const string query = @"
                UPDATE Club
                SET IsDeleted = 1, UpdatedAt = @UpdatedAt
                WHERE No = @No";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.AddWithValue("@No", no);
            cmd.Parameters.AddWithValue("@UpdatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            int affected = await cmd.ExecuteNonQueryAsync();
            bool success = affected > 0;

            if (success)
                LogInfo($"동아리 감춤 완료: No={no}");
            else
                LogWarning($"동아리 감춤 실패: No={no}");

            return success;
        }
        catch (Exception ex)
        {
            LogError($"동아리 감춤 실패: No={no}", ex);
            throw;
        }
    }

    // 물리 삭제(HardDeleteAsync)는 호출부가 없어 지웠다(39차) — 감추기 하나로 통일.

    #endregion

    #region Helper Methods

    private void AddClubParameters(SqliteCommand cmd, Club club)
    {
        cmd.Parameters.AddWithValue("@No", club.No);
        cmd.Parameters.AddWithValue("@SchoolCode", club.SchoolCode ?? string.Empty);
        cmd.Parameters.AddWithValue("@TeacherID", club.TeacherID ?? string.Empty);
        cmd.Parameters.AddWithValue("@Year", club.Year);
        cmd.Parameters.AddWithValue("@ClubName", club.ClubName ?? string.Empty);
        cmd.Parameters.AddWithValue("@ActivityRoom", club.ActivityRoom ?? string.Empty);
        cmd.Parameters.AddWithValue("@Remark", club.Remark ?? string.Empty);
        cmd.Parameters.AddWithValue("@CreatedAt", club.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("@UpdatedAt", club.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("@IsDeleted", club.IsDeleted ? 1 : 0);
    }

    private Club MapClub(SqliteDataReader reader, ReaderColumnCache cache)
    {
        return new Club
        {
            No = reader.GetInt32(cache.GetOrdinal("No")),
            SchoolCode = reader.GetString(cache.GetOrdinal("SchoolCode")),
            TeacherID = reader.GetString(cache.GetOrdinal("TeacherID")),
            Year = reader.GetInt32(cache.GetOrdinal("Year")),
            ClubName = reader.GetString(cache.GetOrdinal("ClubName")),
            ActivityRoom = reader.IsDBNull(cache.GetOrdinal("ActivityRoom")) 
                ? string.Empty 
                : reader.GetString(cache.GetOrdinal("ActivityRoom")),
            Remark = reader.IsDBNull(cache.GetOrdinal("Remark")) 
                ? string.Empty 
                : reader.GetString(cache.GetOrdinal("Remark")),
            CreatedAt = DateTime.Parse(reader.GetString(cache.GetOrdinal("CreatedAt"))),
            UpdatedAt = DateTime.Parse(reader.GetString(cache.GetOrdinal("UpdatedAt"))),
            IsDeleted = reader.GetInt32(cache.GetOrdinal("IsDeleted")) == 1
        };
    }

    #endregion
}

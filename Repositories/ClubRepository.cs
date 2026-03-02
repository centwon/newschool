using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Models;

namespace NewSchool.Repositories
{
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
                if (await reader.ReadAsync())
                {
                    return MapClub(reader);
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
                while (await reader.ReadAsync())
                {
                    clubs.Add(MapClub(reader));
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
                while (await reader.ReadAsync())
                {
                    clubs.Add(MapClub(reader));
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
                while (await reader.ReadAsync())
                {
                    clubs.Add(MapClub(reader));
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

        #region Delete

        /// <summary>
        /// 동아리 논리 삭제
        /// </summary>
        public async Task<bool> DeleteAsync(int no)
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
                    LogInfo($"동아리 논리 삭제 완료: No={no}");
                else
                    LogWarning($"동아리 삭제 실패: No={no}");

                return success;
            }
            catch (Exception ex)
            {
                LogError($"동아리 삭제 실패: No={no}", ex);
                throw;
            }
        }

        /// <summary>
        /// 동아리 물리 삭제 (주의!)
        /// </summary>
        public async Task<bool> HardDeleteAsync(int no)
        {
            const string query = "DELETE FROM Club WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@No", no);

                int affected = await cmd.ExecuteNonQueryAsync();
                bool success = affected > 0;

                if (success)
                    LogInfo($"동아리 물리 삭제 완료: No={no}");

                return success;
            }
            catch (Exception ex)
            {
                LogError($"동아리 물리 삭제 실패: No={no}", ex);
                throw;
            }
        }

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

        private Club MapClub(SqliteDataReader reader)
        {
            return new Club
            {
                No = reader.GetInt32(reader.GetOrdinal("No")),
                SchoolCode = reader.GetString(reader.GetOrdinal("SchoolCode")),
                TeacherID = reader.GetString(reader.GetOrdinal("TeacherID")),
                Year = reader.GetInt32(reader.GetOrdinal("Year")),
                ClubName = reader.GetString(reader.GetOrdinal("ClubName")),
                ActivityRoom = reader.IsDBNull(reader.GetOrdinal("ActivityRoom")) 
                    ? string.Empty 
                    : reader.GetString(reader.GetOrdinal("ActivityRoom")),
                Remark = reader.IsDBNull(reader.GetOrdinal("Remark")) 
                    ? string.Empty 
                    : reader.GetString(reader.GetOrdinal("Remark")),
                CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
                UpdatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("UpdatedAt"))),
                IsDeleted = reader.GetInt32(reader.GetOrdinal("IsDeleted")) == 1
            };
        }

        #endregion
    }
}

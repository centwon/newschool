using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Models;

namespace NewSchool.Repositories
{
    /// <summary>
    /// ClassDiary Repository
    /// 학급 일지 관리 (출결, 메모, 알림장, 생활 기록)
    /// </summary>
    public class ClassDiaryRepository : BaseRepository
    {
        public ClassDiaryRepository(string dbPath) : base(dbPath)
        {
            // 기존 DB 마이그레이션: CreatedAt/UpdatedAt 컬럼 추가
            TryAddColumn("ClassDiary", "CreatedAt", "TEXT");
            TryAddColumn("ClassDiary", "UpdatedAt", "TEXT");
        }

        /// <summary>
        /// 컬럼 추가 시도 (이미 존재하면 무시)
        /// </summary>
        private void TryAddColumn(string table, string columnName, string columnDef)
        {
            try
            {
                using var cmd = CreateCommand($"ALTER TABLE {table} ADD COLUMN {columnName} {columnDef}");
                cmd.ExecuteNonQuery();
            }
            catch (SqliteException)
            {
                // 이미 존재하는 경우 무시
            }
        }

        #region Create

        /// <summary>
        /// 학급 일지 생성
        /// </summary>
        public async Task<int> CreateAsync(ClassDiary diary)
        {
            const string query = @"
                INSERT INTO ClassDiary (
                    SchoolCode, TeacherID, Year, Semester, Date, Grade, Class,
                    Absent, Late, LeaveEarly, Memo, Notice, Life,
                    CreatedAt, UpdatedAt
                )
                VALUES (
                    @SchoolCode, @TeacherID, @Year, @Semester, @Date, @Grade, @Class,
                    @Absent, @Late, @LeaveEarly, @Memo, @Notice, @Life,
                    @CreatedAt, @UpdatedAt
                );
                SELECT last_insert_rowid();";

            try
            {
                using var cmd = CreateCommand(query);
                AddDiaryParameters(cmd, diary);

                var result = await cmd.ExecuteScalarAsync();
                diary.No = Convert.ToInt32(result);

                LogInfo($"학급 일지 생성: No={diary.No}, Date={diary.Date:yyyy-MM-dd}, {diary.Grade}학년 {diary.Class}반");
                return diary.No;
            }
            catch (Exception ex)
            {
                LogError($"학급 일지 생성 실패: Date={diary.Date:yyyy-MM-dd}", ex);
                throw;
            }
        }

        #endregion

        #region Read

        /// <summary>
        /// No로 학급 일지 조회
        /// </summary>
        public async Task<ClassDiary?> GetByNoAsync(int no)
        {
            const string query = "SELECT * FROM ClassDiary WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@No", no);

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return MapDiary(reader);
                }

                LogWarning($"학급 일지를 찾을 수 없음: No={no}");
                return null;
            }
            catch (Exception ex)
            {
                LogError($"학급 일지 조회 실패: No={no}", ex);
                throw;
            }
        }

        /// <summary>
        /// 특정 날짜의 특정 학급 일지 조회
        /// </summary>
        public async Task<ClassDiary?> GetByDateAsync(string schoolCode, int year, int grade, int classNum, DateTime date)
        {
            const string query = @"
                SELECT * FROM ClassDiary 
                WHERE SchoolCode = @SchoolCode 
                  AND Year = @Year 
                  AND Grade = @Grade 
                  AND Class = @Class 
                  AND Date = @Date";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@SchoolCode", schoolCode);
                cmd.Parameters.AddWithValue("@Year", year);
                cmd.Parameters.AddWithValue("@Grade", grade);
                cmd.Parameters.AddWithValue("@Class", classNum);
                cmd.Parameters.AddWithValue("@Date", date.ToString("yyyy-MM-dd"));

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return MapDiary(reader);
                }

                LogDebug($"학급 일지 없음: {date:yyyy-MM-dd}, {grade}학년 {classNum}반");
                return null;
            }
            catch (Exception ex)
            {
                LogError($"학급 일지 조회 실패: Date={date:yyyy-MM-dd}", ex);
                throw;
            }
        }

        /// <summary>
        /// 특정 학급의 모든 일지 조회
        /// </summary>
        public async Task<List<ClassDiary>> GetByClassAsync(string schoolCode, int year,  int grade, int classNum)
        {
            const string query = @"
                SELECT * FROM ClassDiary 
                WHERE SchoolCode = @SchoolCode 
                  AND Year = @Year 
                  AND Grade = @Grade 
                  AND Class = @Class
                ORDER BY Date DESC";

            var diaries = new List<ClassDiary>();

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@SchoolCode", schoolCode);
                cmd.Parameters.AddWithValue("@Year", year);
                cmd.Parameters.AddWithValue("@Grade", grade);
                cmd.Parameters.AddWithValue("@Class", classNum);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    diaries.Add(MapDiary(reader));
                }

                LogInfo($"학급 일지 조회: {grade}학년 {classNum}반 - {diaries.Count}건");
                return diaries;
            }
            catch (Exception ex)
            {
                LogError($"학급 일지 조회 실패: {grade}학년 {classNum}반", ex);
                throw;
            }
        }

        /// <summary>
        /// 특정 월의 학급 일지 조회
        /// </summary>
        public async Task<List<ClassDiary>> GetByMonthAsync(string schoolCode, int year, int semester, int grade, int classNum, int month)
        {
            const string query = @"
                SELECT * FROM ClassDiary 
                WHERE SchoolCode = @SchoolCode 
                  AND Year = @Year 
                  AND Semester = @Semester 
                  AND Grade = @Grade 
                  AND Class = @Class
                  AND strftime('%Y-%m', Date) = @YearMonth
                ORDER BY Date";

            var diaries = new List<ClassDiary>();

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@SchoolCode", schoolCode);
                cmd.Parameters.AddWithValue("@Year", year);
                cmd.Parameters.AddWithValue("@Semester", semester);
                cmd.Parameters.AddWithValue("@Grade", grade);
                cmd.Parameters.AddWithValue("@Class", classNum);
                cmd.Parameters.AddWithValue("@YearMonth", $"{year:D4}-{month:D2}");

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    diaries.Add(MapDiary(reader));
                }

                LogInfo($"월별 일지 조회: {year}년 {month}월 {grade}학년 {classNum}반 - {diaries.Count}건");
                return diaries;
            }
            catch (Exception ex)
            {
                LogError($"월별 일지 조회 실패: {year}년 {month}월", ex);
                throw;
            }
        }

        /// <summary>
        /// 기간별 학급 일지 조회
        /// </summary>
        public async Task<List<ClassDiary>> GetByDateRangeAsync(
            string schoolCode, int year, int semester, int grade, int classNum, 
            DateTime startDate, DateTime endDate)
        {
            const string query = @"
                SELECT * FROM ClassDiary 
                WHERE SchoolCode = @SchoolCode 
                  AND Year = @Year 
                  AND Semester = @Semester 
                  AND Grade = @Grade 
                  AND Class = @Class
                  AND Date BETWEEN @StartDate AND @EndDate
                ORDER BY Date";

            var diaries = new List<ClassDiary>();

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@SchoolCode", schoolCode);
                cmd.Parameters.AddWithValue("@Year", year);
                cmd.Parameters.AddWithValue("@Semester", semester);
                cmd.Parameters.AddWithValue("@Grade", grade);
                cmd.Parameters.AddWithValue("@Class", classNum);
                cmd.Parameters.AddWithValue("@StartDate", startDate.ToString("yyyy-MM-dd"));
                cmd.Parameters.AddWithValue("@EndDate", endDate.ToString("yyyy-MM-dd"));

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    diaries.Add(MapDiary(reader));
                }

                LogInfo($"기간별 일지 조회: {startDate:yyyy-MM-dd} ~ {endDate:yyyy-MM-dd} - {diaries.Count}건");
                return diaries;
            }
            catch (Exception ex)
            {
                LogError($"기간별 일지 조회 실패: {startDate:yyyy-MM-dd} ~ {endDate:yyyy-MM-dd}", ex);
                throw;
            }
        }

        /// <summary>
        /// 최근 일지 조회
        /// </summary>
        public async Task<ClassDiary?> GetLatestAsync(string schoolCode, int year, int semester, int grade, int classNum)
        {
            const string query = @"
                SELECT * FROM ClassDiary 
                WHERE SchoolCode = @SchoolCode 
                  AND Year = @Year 
                  AND Semester = @Semester 
                  AND Grade = @Grade 
                  AND Class = @Class
                ORDER BY Date DESC
                LIMIT 1";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@SchoolCode", schoolCode);
                cmd.Parameters.AddWithValue("@Year", year);
                cmd.Parameters.AddWithValue("@Semester", semester);
                cmd.Parameters.AddWithValue("@Grade", grade);
                cmd.Parameters.AddWithValue("@Class", classNum);

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return MapDiary(reader);
                }

                LogDebug($"최근 일지 없음: {grade}학년 {classNum}반");
                return null;
            }
            catch (Exception ex)
            {
                LogError($"최근 일지 조회 실패: {grade}학년 {classNum}반", ex);
                throw;
            }
        }

        /// <summary>
        /// 검색 (메모, 알림장, 생활 기록에서 키워드 검색)
        /// </summary>
        public async Task<List<ClassDiary>> SearchAsync(string schoolCode, int year, int semester, int grade, int classNum, string keyword)
        {
            const string query = @"
                SELECT * FROM ClassDiary 
                WHERE SchoolCode = @SchoolCode 
                  AND Year = @Year 
                  AND Semester = @Semester 
                  AND Grade = @Grade 
                  AND Class = @Class
                  AND (Memo LIKE @Keyword OR Notice LIKE @Keyword OR Life LIKE @Keyword)
                ORDER BY Date DESC";

            var diaries = new List<ClassDiary>();

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@SchoolCode", schoolCode);
                cmd.Parameters.AddWithValue("@Year", year);
                cmd.Parameters.AddWithValue("@Semester", semester);
                cmd.Parameters.AddWithValue("@Grade", grade);
                cmd.Parameters.AddWithValue("@Class", classNum);
                cmd.Parameters.AddWithValue("@Keyword", $"%{keyword}%");

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    diaries.Add(MapDiary(reader));
                }

                LogInfo($"일지 검색: '{keyword}' - {diaries.Count}건");
                return diaries;
            }
            catch (Exception ex)
            {
                LogError($"일지 검색 실패: '{keyword}'", ex);
                throw;
            }
        }

        #endregion

        #region Update

        /// <summary>
        /// 학급 일지 수정
        /// </summary>
        public async Task<bool> UpdateAsync(ClassDiary diary)
        {
            const string query = @"
                UPDATE ClassDiary SET
                    SchoolCode = @SchoolCode,
                    TeacherID = @TeacherID,
                    Year = @Year,
                    Semester = @Semester,
                    Date = @Date,
                    Grade = @Grade,
                    Class = @Class,
                    Absent = @Absent,
                    Late = @Late,
                    LeaveEarly = @LeaveEarly,
                    Memo = @Memo,
                    Notice = @Notice,
                    Life = @Life,
                    UpdatedAt = @UpdatedAt
                WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@No", diary.No);
                AddDiaryParameters(cmd, diary);

                int rowsAffected = await cmd.ExecuteNonQueryAsync();
                bool success = rowsAffected > 0;

                if (success)
                {
                    LogInfo($"학급 일지 수정 완료: No={diary.No}");
                }
                else
                {
                    LogWarning($"학급 일지 수정 실패 (존재하지 않음): No={diary.No}");
                }

                return success;
            }
            catch (Exception ex)
            {
                LogError($"학급 일지 수정 실패: No={diary.No}", ex);
                throw;
            }
        }

        #endregion

        #region Delete

        /// <summary>
        /// 학급 일지 삭제 (물리 삭제)
        /// </summary>
        public async Task<bool> DeleteAsync(int no)
        {
            const string query = "DELETE FROM ClassDiary WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@No", no);

                int rowsAffected = await cmd.ExecuteNonQueryAsync();
                bool success = rowsAffected > 0;

                if (success)
                {
                    LogInfo($"학급 일지 삭제 완료: No={no}");
                }
                else
                {
                    LogWarning($"학급 일지 삭제 실패 (존재하지 않음): No={no}");
                }

                return success;
            }
            catch (Exception ex)
            {
                LogError($"학급 일지 삭제 실패: No={no}", ex);
                throw;
            }
        }

        #endregion

        #region Statistics & Utilities

        /// <summary>
        /// 일지 개수 조회
        /// </summary>
        public async Task<int> GetCountAsync(string schoolCode, int year, int semester, int grade, int classNum)
        {
            const string query = @"
                SELECT COUNT(*) FROM ClassDiary 
                WHERE SchoolCode = @SchoolCode 
                  AND Year = @Year 
                  AND Semester = @Semester 
                  AND Grade = @Grade 
                  AND Class = @Class";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@SchoolCode", schoolCode);
                cmd.Parameters.AddWithValue("@Year", year);
                cmd.Parameters.AddWithValue("@Semester", semester);
                cmd.Parameters.AddWithValue("@Grade", grade);
                cmd.Parameters.AddWithValue("@Class", classNum);

                var result = await cmd.ExecuteScalarAsync();
                return Convert.ToInt32(result);
            }
            catch (Exception ex)
            {
                LogError("일지 개수 조회 실패", ex);
                throw;
            }
        }

        /// <summary>
        /// 일지 존재 여부 확인
        /// </summary>
        public async Task<bool> ExistsAsync(string schoolCode, int year, int semester, int grade, int classNum, DateTime date)
        {
            const string query = @"
                SELECT COUNT(*) FROM ClassDiary 
                WHERE SchoolCode = @SchoolCode 
                  AND Year = @Year 
                  AND Semester = @Semester 
                  AND Grade = @Grade 
                  AND Class = @Class 
                  AND Date = @Date";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@SchoolCode", schoolCode);
                cmd.Parameters.AddWithValue("@Year", year);
                cmd.Parameters.AddWithValue("@Semester", semester);
                cmd.Parameters.AddWithValue("@Grade", grade);
                cmd.Parameters.AddWithValue("@Class", classNum);
                cmd.Parameters.AddWithValue("@Date", date.ToString("yyyy-MM-dd"));

                var result = await cmd.ExecuteScalarAsync();
                return Convert.ToInt32(result) > 0;
            }
            catch (Exception ex)
            {
                LogError("일지 존재 확인 실패", ex);
                throw;
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// ClassDiary 파라미터 추가
        /// </summary>
        private void AddDiaryParameters(SqliteCommand cmd, ClassDiary diary)
        {
            cmd.Parameters.AddWithValue("@SchoolCode", diary.SchoolCode);
            cmd.Parameters.AddWithValue("@TeacherID", string.IsNullOrWhiteSpace(diary.TeacherID) ? DBNull.Value : diary.TeacherID);
            cmd.Parameters.AddWithValue("@Year", diary.Year);
            cmd.Parameters.AddWithValue("@Semester", diary.Semester);
            cmd.Parameters.AddWithValue("@Date", diary.Date.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@Grade", diary.Grade);
            cmd.Parameters.AddWithValue("@Class", diary.Class);
            cmd.Parameters.AddWithValue("@Absent", diary.Absent);
            cmd.Parameters.AddWithValue("@Late", diary.Late);
            cmd.Parameters.AddWithValue("@LeaveEarly", diary.LeaveEarly);
            cmd.Parameters.AddWithValue("@Memo", diary.Memo);
            cmd.Parameters.AddWithValue("@Notice", diary.Notice);
            cmd.Parameters.AddWithValue("@Life", diary.Life);
            cmd.Parameters.AddWithValue("@CreatedAt", diary.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@UpdatedAt", diary.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        }

        /// <summary>
        /// SqliteDataReader를 ClassDiary로 매핑
        /// </summary>
        private ClassDiary MapDiary(SqliteDataReader reader)
        {
            return new ClassDiary
            {
                No = reader.GetInt32(reader.GetOrdinal("No")),
                SchoolCode = reader.GetString(reader.GetOrdinal("SchoolCode")),
                TeacherID = reader.IsDBNull(reader.GetOrdinal("TeacherID")) ? string.Empty : reader.GetString(reader.GetOrdinal("TeacherID")),
                Year = reader.GetInt32(reader.GetOrdinal("Year")),
                Semester = reader.GetInt32(reader.GetOrdinal("Semester")),
                Date = DateTime.Parse(reader.GetString(reader.GetOrdinal("Date"))),
                Grade = reader.GetInt32(reader.GetOrdinal("Grade")),
                Class = reader.GetInt32(reader.GetOrdinal("Class")),
                Absent = reader.GetString(reader.GetOrdinal("Absent")),
                Late = reader.GetString(reader.GetOrdinal("Late")),
                LeaveEarly = reader.GetString(reader.GetOrdinal("LeaveEarly")),
                Memo = reader.GetString(reader.GetOrdinal("Memo")),
                Notice = reader.GetString(reader.GetOrdinal("Notice")),
                Life = reader.GetString(reader.GetOrdinal("Life")),
                CreatedAt = GetDateTimeSafe(reader, "CreatedAt"),
                UpdatedAt = GetDateTimeSafe(reader, "UpdatedAt")
            };
        }

        private static DateTime GetDateTimeSafe(SqliteDataReader reader, string column)
        {
            try
            {
                var ordinal = reader.GetOrdinal(column);
                if (reader.IsDBNull(ordinal)) return DateTime.Now;
                var str = reader.GetString(ordinal);
                return DateTime.TryParse(str, out var dt) ? dt : DateTime.Now;
            }
            catch
            {
                return DateTime.Now;
            }
        }

        #endregion
    }
}

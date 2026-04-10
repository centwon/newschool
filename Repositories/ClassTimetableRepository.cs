using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Models;

namespace NewSchool.Repositories
{
    /// <summary>
    /// ClassTimetable Repository
    /// 학급 시간표 정보 관리 (학생/학급용)
    /// </summary>
    public class ClassTimetableRepository : BaseRepository
    {
        public ClassTimetableRepository(string dbPath) : base(dbPath) { }

        #region Create

        /// <summary>
        /// 학급 시간표 생성
        /// </summary>
        public async Task<int> CreateAsync(ClassTimetable timetable)
        {
            const string query = @"
                INSERT INTO ClassTimetable (
                    SchoolCode, Year, Semester, Grade, Class,
                    DayOfWeek, Period, SubjectName, TeacherName, Room
                ) VALUES (
                    @SchoolCode, @Year, @Semester, @Grade, @Class,
                    @DayOfWeek, @Period, @SubjectName, @TeacherName, @Room
                );
                SELECT last_insert_rowid();";

            try
            {
                using var cmd = CreateCommand(query);
                AddTimetableParameters(cmd, timetable);

                var result = await cmd.ExecuteScalarAsync();
                timetable.No = Convert.ToInt32(result);

                LogInfo($"학급 시간표 생성 완료: No={timetable.No}, {timetable.Grade}학년 {timetable.Class}반");
                return timetable.No;
            }
            catch (Exception ex)
            {
                LogError($"학급 시간표 생성 실패: {timetable.Grade}학년 {timetable.Class}반", ex);
                throw;
            }
        }

        /// <summary>
        /// 학급 시간표 일괄 생성
        /// </summary>
        public async Task<int> CreateBatchAsync(List<ClassTimetable> timetables)
        {
            if (timetables == null || timetables.Count == 0)
                return 0;

            int count = 0;

            try
            {
                foreach (var timetable in timetables)
                {
                    await CreateAsync(timetable);
                    count++;
                }

                LogInfo($"학급 시간표 일괄 생성 완료: {count}개");
                return count;
            }
            catch (Exception ex)
            {
                LogError($"학급 시간표 일괄 생성 실패", ex);
                throw;
            }
        }

        #endregion

        #region Read

        /// <summary>
        /// No로 학급 시간표 조회
        /// </summary>
        public async Task<ClassTimetable?> GetByIdAsync(int no)
        {
            const string query = "SELECT * FROM ClassTimetable WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@No", no);

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return MapTimetable(reader);
                }

                return null;
            }
            catch (Exception ex)
            {
                LogError($"학급 시간표 조회 실패: No={no}", ex);
                throw;
            }
        }

        /// <summary>
        /// 학급별 시간표 조회
        /// </summary>
        public async Task<List<ClassTimetable>> GetByClassAsync(
            string schoolCode, int year, int semester, int grade, int classNo)
        {
            const string query = @"
                SELECT * FROM ClassTimetable 
                WHERE SchoolCode = @SchoolCode
                  AND Year = @Year
                  AND Semester = @Semester
                  AND Grade = @Grade
                  AND Class = @Class
                ORDER BY DayOfWeek, Period";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@SchoolCode", schoolCode);
                cmd.Parameters.AddWithValue("@Year", year);
                cmd.Parameters.AddWithValue("@Semester", semester);
                cmd.Parameters.AddWithValue("@Grade", grade);
                cmd.Parameters.AddWithValue("@Class", classNo);

                var timetables = new List<ClassTimetable>();
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    timetables.Add(MapTimetable(reader));
                }

                return timetables;
            }
            catch (Exception ex)
            {
                LogError($"학급 시간표 조회 실패: {grade}학년 {classNo}반", ex);
                throw;
            }
        }

        /// <summary>
        /// 학년별 시간표 조회
        /// </summary>
        public async Task<List<ClassTimetable>> GetByGradeAsync(
            string schoolCode, int year, int semester, int grade)
        {
            const string query = @"
                SELECT * FROM ClassTimetable 
                WHERE SchoolCode = @SchoolCode
                  AND Year = @Year
                  AND Semester = @Semester
                  AND Grade = @Grade
                ORDER BY Class, DayOfWeek, Period";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@SchoolCode", schoolCode);
                cmd.Parameters.AddWithValue("@Year", year);
                cmd.Parameters.AddWithValue("@Semester", semester);
                cmd.Parameters.AddWithValue("@Grade", grade);

                var timetables = new List<ClassTimetable>();
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    timetables.Add(MapTimetable(reader));
                }

                return timetables;
            }
            catch (Exception ex)
            {
                LogError($"학년별 시간표 조회 실패: {grade}학년", ex);
                throw;
            }
        }

        #endregion

        #region Update

        /// <summary>
        /// 학급 시간표 수정
        /// </summary>
        public async Task<bool> UpdateAsync(ClassTimetable timetable)
        {
            const string query = @"
                UPDATE ClassTimetable SET
                    SchoolCode = @SchoolCode,
                    Year = @Year,
                    Semester = @Semester,
                    Grade = @Grade,
                    Class = @Class,
                    DayOfWeek = @DayOfWeek,
                    Period = @Period,
                    SubjectName = @SubjectName,
                    TeacherName = @TeacherName,
                    Room = @Room
                WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                AddTimetableParameters(cmd, timetable);

                int affected = await cmd.ExecuteNonQueryAsync();
                bool success = affected > 0;

                if (success)
                    LogInfo($"학급 시간표 수정 완료: No={timetable.No}");
                else
                    LogWarning($"학급 시간표 수정 실패: No={timetable.No}");

                return success;
            }
            catch (Exception ex)
            {
                LogError($"학급 시간표 수정 실패: No={timetable.No}", ex);
                throw;
            }
        }

        #endregion

        #region Delete

        /// <summary>
        /// 학급 시간표 삭제
        /// </summary>
        public async Task<bool> DeleteAsync(int no)
        {
            const string query = "DELETE FROM ClassTimetable WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@No", no);

                int affected = await cmd.ExecuteNonQueryAsync();
                bool success = affected > 0;

                if (success)
                    LogInfo($"학급 시간표 삭제 완료: No={no}");
                else
                    LogWarning($"학급 시간표 삭제 실패: No={no}");

                return success;
            }
            catch (Exception ex)
            {
                LogError($"학급 시간표 삭제 실패: No={no}", ex);
                throw;
            }
        }

        /// <summary>
        /// 학급별 시간표 전체 삭제
        /// </summary>
        public async Task<int> DeleteByClassAsync(
            string schoolCode, int year, int semester, int grade, int classNo)
        {
            const string query = @"
                DELETE FROM ClassTimetable 
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
                cmd.Parameters.AddWithValue("@Class", classNo);

                int affected = await cmd.ExecuteNonQueryAsync();

                LogInfo($"학급 시간표 전체 삭제 완료: {grade}학년 {classNo}반, 삭제 수={affected}");
                return affected;
            }
            catch (Exception ex)
            {
                LogError($"학급 시간표 전체 삭제 실패: {grade}학년 {classNo}반", ex);
                throw;
            }
        }

        #endregion

        #region Helper

        /// <summary>
        /// 중복 체크
        /// </summary>
        public async Task<bool> IsDuplicateAsync(
            string schoolCode, int year, int semester,
            int grade, int classNo, int dayOfWeek, int period)
        {
            const string query = @"
                SELECT EXISTS(SELECT 1
                FROM ClassTimetable
                WHERE SchoolCode = @SchoolCode
                  AND Year = @Year
                  AND Semester = @Semester
                  AND Grade = @Grade
                  AND Class = @Class
                  AND DayOfWeek = @DayOfWeek
                  AND Period = @Period)";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@SchoolCode", schoolCode);
                cmd.Parameters.AddWithValue("@Year", year);
                cmd.Parameters.AddWithValue("@Semester", semester);
                cmd.Parameters.AddWithValue("@Grade", grade);
                cmd.Parameters.AddWithValue("@Class", classNo);
                cmd.Parameters.AddWithValue("@DayOfWeek", dayOfWeek);
                cmd.Parameters.AddWithValue("@Period", period);

                var result = await cmd.ExecuteScalarAsync();
                return Convert.ToInt32(result) == 1;
            }
            catch (Exception ex)
            {
                LogError($"시간표 중복 체크 실패", ex);
                throw;
            }
        }

        private void AddTimetableParameters(SqliteCommand cmd, ClassTimetable timetable)
        {
            cmd.Parameters.AddWithValue("@No", timetable.No);
            cmd.Parameters.AddWithValue("@SchoolCode", timetable.SchoolCode ?? string.Empty);
            cmd.Parameters.AddWithValue("@Year", timetable.Year);
            cmd.Parameters.AddWithValue("@Semester", timetable.Semester);
            cmd.Parameters.AddWithValue("@Grade", timetable.Grade);
            cmd.Parameters.AddWithValue("@Class", timetable.Class);
            cmd.Parameters.AddWithValue("@DayOfWeek", timetable.DayOfWeek);
            cmd.Parameters.AddWithValue("@Period", timetable.Period);
            cmd.Parameters.AddWithValue("@SubjectName", timetable.SubjectName ?? string.Empty);
            cmd.Parameters.AddWithValue("@TeacherName", timetable.TeacherName ?? string.Empty);
            cmd.Parameters.AddWithValue("@Room", timetable.Room ?? string.Empty);
        }

        private ClassTimetable MapTimetable(SqliteDataReader reader)
        {
            return new ClassTimetable
            {
                No = reader.GetInt32(reader.GetOrdinal("No")),
                SchoolCode = reader.GetString(reader.GetOrdinal("SchoolCode")),
                Year = reader.GetInt32(reader.GetOrdinal("Year")),
                Semester = reader.GetInt32(reader.GetOrdinal("Semester")),
                Grade = reader.GetInt32(reader.GetOrdinal("Grade")),
                Class = reader.GetInt32(reader.GetOrdinal("Class")),
                DayOfWeek = reader.GetInt32(reader.GetOrdinal("DayOfWeek")),
                Period = reader.GetInt32(reader.GetOrdinal("Period")),
                SubjectName = reader.GetString(reader.GetOrdinal("SubjectName")),
                TeacherName = reader.GetString(reader.GetOrdinal("TeacherName")),
                Room = reader.GetString(reader.GetOrdinal("Room"))
            };
        }

        #endregion
    }
}

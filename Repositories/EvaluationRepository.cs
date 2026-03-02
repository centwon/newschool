using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Models;

namespace NewSchool.Repositories
{
    /// <summary>
    /// Evaluation Repository
    /// 평가(성적) 정보 관리
    /// </summary>
    public class EvaluationRepository : BaseRepository
    {
        public EvaluationRepository(string dbPath) : base(dbPath) { }

        #region Create

        /// <summary>
        /// 평가 기록 생성
        /// </summary>
        public async Task<int> CreateAsync(Evaluation evaluation)
        {
            const string query = @"
                INSERT INTO Evaluation (
                    StudentID, SchoolCode, Year, Semester, CourseNo, Subject,
                    EvaluationType, Round, Score, MaxScore, Grade, Rank, TotalStudents,
                    Achievement, TeacherID, Memo, CreatedAt, UpdatedAt, IsDeleted
                ) VALUES (
                    @StudentID, @SchoolCode, @Year, @Semester, @CourseNo, @Subject,
                    @EvaluationType, @Round, @Score, @MaxScore, @Grade, @Rank, @TotalStudents,
                    @Achievement, @TeacherID, @Memo, @CreatedAt, @UpdatedAt, @IsDeleted
                );
                SELECT last_insert_rowid();";

            try
            {
                using var cmd = CreateCommand(query);
                AddEvaluationParameters(cmd, evaluation);

                var result = await cmd.ExecuteScalarAsync();
                evaluation.No = Convert.ToInt32(result);

                LogInfo($"평가 기록 생성 완료: No={evaluation.No}, StudentID={evaluation.StudentID}");
                return evaluation.No;
            }
            catch (Exception ex)
            {
                LogError($"평가 기록 생성 실패: StudentID={evaluation.StudentID}", ex);
                throw;
            }
        }

        /// <summary>
        /// 학급 전체 평가 일괄 등록
        /// </summary>
        public async Task<int> BulkCreateAsync(List<Evaluation> evaluations)
        {
            if (evaluations == null || evaluations.Count == 0)
                return 0;

            try
            {
                BeginTransaction();

                int count = 0;
                foreach (var evaluation in evaluations)
                {
                    await CreateAsync(evaluation);
                    count++;
                }

                Commit();
                return count;
            }
            catch
            {
                Rollback();
                throw;
            }
        }

        #endregion

        #region Read

        /// <summary>
        /// No로 평가 기록 조회
        /// </summary>
        public async Task<Evaluation?> GetByIdAsync(int no)
        {
            const string query = "SELECT * FROM Evaluation WHERE No = @No AND IsDeleted = 0";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@No", no);

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return MapEvaluation(reader);
                }

                return null;
            }
            catch (Exception ex)
            {
                LogError($"평가 기록 조회 실패: No={no}", ex);
                throw;
            }
        }

        /// <summary>
        /// 학생의 학년도/학기별 평가 기록 조회
        /// </summary>
        public async Task<List<Evaluation>> GetByStudentAsync(
            string studentId, int year, int semester)
        {
            const string query = @"
                SELECT * FROM Evaluation 
                WHERE StudentID = @StudentID 
                  AND Year = @Year 
                  AND Semester = @Semester
                  AND IsDeleted = 0
                ORDER BY CourseNo, EvaluationType, Round";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@StudentID", studentId);
                cmd.Parameters.AddWithValue("@Year", year);
                cmd.Parameters.AddWithValue("@Semester", semester);

                var evaluations = new List<Evaluation>();
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    evaluations.Add(MapEvaluation(reader));
                }

                return evaluations;
            }
            catch (Exception ex)
            {
                LogError($"학생 평가 기록 조회 실패: StudentID={studentId}", ex);
                throw;
            }
        }

        /// <summary>
        /// 과목별 평가 기록 조회
        /// </summary>
        public async Task<List<Evaluation>> GetByCourseAsync(
            int courseNo, string evaluationType, int round)
        {
            const string query = @"
                SELECT * FROM Evaluation 
                WHERE CourseNo = @CourseNo 
                  AND EvaluationType = @EvaluationType
                  AND Round = @Round
                  AND IsDeleted = 0
                ORDER BY Rank";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@CourseNo", courseNo);
                cmd.Parameters.AddWithValue("@EvaluationType", evaluationType);
                cmd.Parameters.AddWithValue("@Round", round);

                var evaluations = new List<Evaluation>();
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    evaluations.Add(MapEvaluation(reader));
                }

                return evaluations;
            }
            catch (Exception ex)
            {
                LogError($"과목 평가 기록 조회 실패: CourseNo={courseNo}", ex);
                throw;
            }
        }

        /// <summary>
        /// 학급별 평가 기록 조회
        /// </summary>
        public async Task<List<Evaluation>> GetByClassAsync(
            string schoolCode, int year, int semester, int grade, int classNo,
            string evaluationType, int round)
        {
            const string query = @"
                SELECT e.* FROM Evaluation e
                INNER JOIN Enrollment en ON e.StudentID = en.StudentID
                WHERE en.SchoolCode = @SchoolCode
                  AND en.Year = @Year
                  AND en.Semester = @Semester
                  AND en.Grade = @Grade
                  AND en.Class = @Class
                  AND e.EvaluationType = @EvaluationType
                  AND e.Round = @Round
                  AND e.IsDeleted = 0
                ORDER BY en.Number, e.Subject";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@SchoolCode", schoolCode);
                cmd.Parameters.AddWithValue("@Year", year);
                cmd.Parameters.AddWithValue("@Semester", semester);
                cmd.Parameters.AddWithValue("@Grade", grade);
                cmd.Parameters.AddWithValue("@Class", classNo);
                cmd.Parameters.AddWithValue("@EvaluationType", evaluationType);
                cmd.Parameters.AddWithValue("@Round", round);

                var evaluations = new List<Evaluation>();
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    evaluations.Add(MapEvaluation(reader));
                }

                return evaluations;
            }
            catch (Exception ex)
            {
                LogError($"학급 평가 기록 조회 실패: {grade}학년 {classNo}반", ex);
                throw;
            }
        }

        /// <summary>
        /// 학생의 특정 과목 평가 기록 조회
        /// </summary>
        public async Task<List<Evaluation>> GetByStudentAndCourseAsync(
            string studentId, int courseNo)
        {
            const string query = @"
                SELECT * FROM Evaluation 
                WHERE StudentID = @StudentID 
                  AND CourseNo = @CourseNo
                  AND IsDeleted = 0
                ORDER BY EvaluationType, Round";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@StudentID", studentId);
                cmd.Parameters.AddWithValue("@CourseNo", courseNo);

                var evaluations = new List<Evaluation>();
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    evaluations.Add(MapEvaluation(reader));
                }

                return evaluations;
            }
            catch (Exception ex)
            {
                LogError($"학생 과목별 평가 조회 실패: StudentID={studentId}, CourseNo={courseNo}", ex);
                throw;
            }
        }

        /// <summary>
        /// 학생의 학기 평균 성적 계산
        /// </summary>
        public async Task<decimal> CalculateAverageAsync(
            string studentId, int year, int semester)
        {
            const string query = @"
                SELECT AVG(Score * 100.0 / MaxScore) as Average
                FROM Evaluation 
                WHERE StudentID = @StudentID 
                  AND Year = @Year 
                  AND Semester = @Semester
                  AND IsDeleted = 0";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@StudentID", studentId);
                cmd.Parameters.AddWithValue("@Year", year);
                cmd.Parameters.AddWithValue("@Semester", semester);

                var result = await cmd.ExecuteScalarAsync();
                if (result == null || result == DBNull.Value)
                    return 0;

                return Convert.ToDecimal(result);
            }
            catch (Exception ex)
            {
                LogError($"평균 성적 계산 실패: StudentID={studentId}", ex);
                throw;
            }
        }

        /// <summary>
        /// 과목별 통계 (평균, 최고점, 최저점)
        /// </summary>
        public async Task<(decimal Average, decimal Max, decimal Min)> GetCourseStatisticsAsync(
            int courseNo, string evaluationType, int round)
        {
            const string query = @"
                SELECT 
                    AVG(Score * 100.0 / MaxScore) as Average,
                    MAX(Score * 100.0 / MaxScore) as Max,
                    MIN(Score * 100.0 / MaxScore) as Min
                FROM Evaluation 
                WHERE CourseNo = @CourseNo 
                  AND EvaluationType = @EvaluationType
                  AND Round = @Round
                  AND IsDeleted = 0";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@CourseNo", courseNo);
                cmd.Parameters.AddWithValue("@EvaluationType", evaluationType);
                cmd.Parameters.AddWithValue("@Round", round);

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    var avg = reader.IsDBNull(0) ? 0 : Convert.ToDecimal(reader.GetDouble(0));
                    var max = reader.IsDBNull(1) ? 0 : Convert.ToDecimal(reader.GetDouble(1));
                    var min = reader.IsDBNull(2) ? 0 : Convert.ToDecimal(reader.GetDouble(2));

                    return (avg, max, min);
                }

                return (0, 0, 0);
            }
            catch (Exception ex)
            {
                LogError($"과목 통계 조회 실패: CourseNo={courseNo}", ex);
                throw;
            }
        }

        #endregion

        #region Update

        /// <summary>
        /// 평가 기록 수정
        /// </summary>
        public async Task<bool> UpdateAsync(Evaluation evaluation)
        {
            const string query = @"
                UPDATE Evaluation SET
                    StudentID = @StudentID,
                    SchoolCode = @SchoolCode,
                    Year = @Year,
                    Semester = @Semester,
                    CourseNo = @CourseNo,
                    Subject = @Subject,
                    EvaluationType = @EvaluationType,
                    Round = @Round,
                    Score = @Score,
                    MaxScore = @MaxScore,
                    Grade = @Grade,
                    Rank = @Rank,
                    TotalStudents = @TotalStudents,
                    Achievement = @Achievement,
                    TeacherID = @TeacherID,
                    Memo = @Memo,
                    UpdatedAt = @UpdatedAt,
                    IsDeleted = @IsDeleted
                WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                AddEvaluationParameters(cmd, evaluation);

                int affected = await cmd.ExecuteNonQueryAsync();
                bool success = affected > 0;

                if (success)
                    LogInfo($"평가 기록 수정 완료: No={evaluation.No}");
                else
                    LogWarning($"평가 기록 수정 실패: No={evaluation.No}");

                return success;
            }
            catch (Exception ex)
            {
                LogError($"평가 기록 수정 실패: No={evaluation.No}", ex);
                throw;
            }
        }

        #endregion

        #region Delete

        /// <summary>
        /// 평가 기록 삭제 (논리 삭제)
        /// </summary>
        public async Task<bool> DeleteAsync(int no)
        {
            const string query = @"
                UPDATE Evaluation SET 
                    IsDeleted = 1,
                    UpdatedAt = @UpdatedAt
                WHERE No = @No";

            try
            {
                using var cmd = CreateCommand(query);
                cmd.Parameters.AddWithValue("@No", no);
                cmd.Parameters.AddWithValue("@UpdatedAt", DateTime.Now);

                int affected = await cmd.ExecuteNonQueryAsync();
                bool success = affected > 0;

                if (success)
                    LogInfo($"평가 기록 삭제 완료: No={no}");
                else
                    LogWarning($"평가 기록 삭제 실패: No={no}");

                return success;
            }
            catch (Exception ex)
            {
                LogError($"평가 기록 삭제 실패: No={no}", ex);
                throw;
            }
        }

        #endregion

        #region Helper Methods

        private void AddEvaluationParameters(SqliteCommand cmd, Evaluation evaluation)
        {
            cmd.Parameters.AddWithValue("@No", evaluation.No);
            cmd.Parameters.AddWithValue("@StudentID", evaluation.StudentID ?? string.Empty);
            cmd.Parameters.AddWithValue("@SchoolCode", evaluation.SchoolCode ?? string.Empty);
            cmd.Parameters.AddWithValue("@Year", evaluation.Year);
            cmd.Parameters.AddWithValue("@Semester", evaluation.Semester);
            cmd.Parameters.AddWithValue("@CourseNo", evaluation.CourseNo);
            cmd.Parameters.AddWithValue("@Subject", evaluation.Subject ?? string.Empty);
            cmd.Parameters.AddWithValue("@EvaluationType", evaluation.EvaluationType ?? "지필");
            cmd.Parameters.AddWithValue("@Round", evaluation.Round);
            cmd.Parameters.AddWithValue("@Score", evaluation.Score);
            cmd.Parameters.AddWithValue("@MaxScore", evaluation.MaxScore);
            cmd.Parameters.AddWithValue("@Grade", evaluation.Grade ?? string.Empty);
            cmd.Parameters.AddWithValue("@Rank", evaluation.Rank);
            cmd.Parameters.AddWithValue("@TotalStudents", evaluation.TotalStudents);
            cmd.Parameters.AddWithValue("@Achievement", evaluation.Achievement ?? string.Empty);
            cmd.Parameters.AddWithValue("@TeacherID", evaluation.TeacherID ?? string.Empty);
            cmd.Parameters.AddWithValue("@Memo", evaluation.Memo ?? string.Empty);
            cmd.Parameters.AddWithValue("@CreatedAt", evaluation.CreatedAt);
            cmd.Parameters.AddWithValue("@UpdatedAt", evaluation.UpdatedAt);
            cmd.Parameters.AddWithValue("@IsDeleted", evaluation.IsDeleted ? 1 : 0);
        }

        private Evaluation MapEvaluation(SqliteDataReader reader)
        {
            return new Evaluation
            {
                No = reader.GetInt32(reader.GetOrdinal("No")),
                StudentID = reader.GetString(reader.GetOrdinal("StudentID")),
                SchoolCode = reader.GetString(reader.GetOrdinal("SchoolCode")),
                Year = reader.GetInt32(reader.GetOrdinal("Year")),
                Semester = reader.GetInt32(reader.GetOrdinal("Semester")),
                CourseNo = reader.GetInt32(reader.GetOrdinal("CourseNo")),
                Subject = reader.GetString(reader.GetOrdinal("Subject")),
                EvaluationType = reader.GetString(reader.GetOrdinal("EvaluationType")),
                Round = reader.GetInt32(reader.GetOrdinal("Round")),
                Score = reader.GetDecimal(reader.GetOrdinal("Score")),
                MaxScore = reader.GetDecimal(reader.GetOrdinal("MaxScore")),
                Grade = reader.GetString(reader.GetOrdinal("Grade")),
                Rank = reader.GetInt32(reader.GetOrdinal("Rank")),
                TotalStudents = reader.GetInt32(reader.GetOrdinal("TotalStudents")),
                Achievement = reader.GetString(reader.GetOrdinal("Achievement")),
                TeacherID = reader.GetString(reader.GetOrdinal("TeacherID")),
                Memo = reader.GetString(reader.GetOrdinal("Memo")),
                CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
                UpdatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("UpdatedAt"))),
                IsDeleted = reader.GetInt32(reader.GetOrdinal("IsDeleted")) == 1
            };
        }

        #endregion
    }
}

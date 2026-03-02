using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Repositories;

namespace NewSchool.Services
{
    /// <summary>
    /// Teacher Service
    /// 교사 정보 + 근무 이력 통합 관리
    /// ⭐ CreateAsync, AddHistoryAsync 추가 (InitialSetupDialog 지원)
    /// </summary>
    public class TeacherService : IDisposable
    {
        private readonly string _dbPath;
        private bool _disposed;

        public TeacherService(string dbPath)
        {
            _dbPath = dbPath;
        }

        #region 교사 등록 (신규 추가)

        /// <summary>
        /// Teacher만 단독으로 생성 (InitialSetupDialog용)
        /// </summary>
        public async Task<(bool Success, string Message)> CreateAsync(Teacher teacher)
        {
            using var teacherRepo = new TeacherRepository(_dbPath);

            try
            {
                // 1. TeacherID 중복 확인
                var existing = await teacherRepo.GetByTeacherIdAsync(teacher.TeacherID);
                if (existing != null)
                {
                    return (false, "이미 등록된 교사 ID입니다.");
                }

                // 2. LoginID 중복 확인
                existing = await teacherRepo.GetByLoginIdAsync(teacher.LoginID);
                if (existing != null)
                {
                    return (false, "이미 사용 중인 로그인 ID입니다.");
                }

                // 3. Teacher 생성
                await teacherRepo.CreateAsync(teacher);
                Debug.WriteLine($"[TeacherService] Teacher 생성: {teacher.TeacherID}");

                return (true, "교사 등록이 완료되었습니다.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TeacherService] 교사 등록 실패: {ex.Message}");
                return (false, $"교사 등록 중 오류가 발생했습니다: {ex.Message}");
            }
        }

        /// <summary>
        /// TeacherSchoolHistory만 단독으로 추가 (InitialSetupDialog용)
        /// </summary>
        public async Task<(bool Success, string Message)> AddHistoryAsync(TeacherSchoolHistory history)
        {
            using var historyRepo = new TeacherSchoolHistoryRepository(_dbPath);

            try
            {
                await historyRepo.CreateAsync(history);
                Debug.WriteLine($"[TeacherService] TeacherSchoolHistory 생성: {history.TeacherID}");

                return (true, "근무 이력이 추가되었습니다.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TeacherService] 근무 이력 추가 실패: {ex.Message}");
                return (false, $"근무 이력 추가 중 오류가 발생했습니다: {ex.Message}");
            }
        }

        /// <summary>
        /// 신규 교사 등록 (Teacher + TeacherSchoolHistory 동시 생성)
        /// 트랜잭션으로 원자성 보장
        /// </summary>
        public async Task<(bool Success, string Message, string TeacherID)> RegisterTeacherAsync(
            Teacher teacher,
            TeacherSchoolHistory history)
        {
            using var teacherRepo = new TeacherRepository(_dbPath);
            using var historyRepo = new TeacherSchoolHistoryRepository(_dbPath);

            try
            {
                // 트랜잭션 시작
                teacherRepo.BeginTransaction();
                historyRepo.SetTransaction(teacherRepo.GetTransaction());

                // 1. TeacherID 중복 확인
                var existing = await teacherRepo.GetByTeacherIdAsync(teacher.TeacherID);
                if (existing != null)
                {
                    teacherRepo.Rollback();
                    return (false, "이미 등록된 교사 ID입니다.", string.Empty);
                }

                // 2. LoginID 중복 확인
                existing = await teacherRepo.GetByLoginIdAsync(teacher.LoginID);
                if (existing != null)
                {
                    teacherRepo.Rollback();
                    return (false, "이미 사용 중인 로그인 ID입니다.", string.Empty);
                }

                // 3. Teacher 생성
                await teacherRepo.CreateAsync(teacher);
                Debug.WriteLine($"[TeacherService] Teacher 생성: {teacher.TeacherID}");

                // 4. TeacherSchoolHistory 생성
                history.TeacherID = teacher.TeacherID;
                history.IsCurrent = true; // 신규 등록은 항상 현재 근무
                await historyRepo.CreateAsync(history);
                Debug.WriteLine($"[TeacherService] TeacherSchoolHistory 생성: {history.No}");

                // 트랜잭션 커밋
                teacherRepo.Commit();

                return (true, "교사 등록이 완료되었습니다.", teacher.TeacherID);
            }
            catch (Exception ex)
            {
                teacherRepo.Rollback();
                Debug.WriteLine($"[TeacherService] 교사 등록 실패: {ex.Message}");
                return (false, $"교사 등록 중 오류가 발생했습니다: {ex.Message}", string.Empty);
            }
        }

        #endregion

        #region 교사 전보 처리

        /// <summary>
        /// 교사 전보 처리
        /// 기존 근무 이력 종료 + 새 근무 이력 생성
        /// </summary>
        public async Task<(bool Success, string Message)> TransferTeacherAsync(
            string teacherId,
            string endDate,
            TeacherSchoolHistory newHistory)
        {
            using var teacherRepo = new TeacherRepository(_dbPath);
            using var historyRepo = new TeacherSchoolHistoryRepository(_dbPath);

            try
            {
                // 트랜잭션 시작
                historyRepo.BeginTransaction();

                // 1. 현재 근무 이력 종료
                await historyRepo.EndCurrentAsync(teacherId, endDate);
                Debug.WriteLine($"[TeacherService] 기존 근무 종료: {teacherId}");

                // 2. 새 근무 이력 생성
                newHistory.TeacherID = teacherId;
                newHistory.IsCurrent = true;
                await historyRepo.CreateAsync(newHistory);
                Debug.WriteLine($"[TeacherService] 새 근무 이력 생성: {newHistory.No}");

                // 3. Teacher 정보 업데이트 (필요시)
                var teacher = await teacherRepo.GetByTeacherIdAsync(teacherId);
                if (teacher != null)
                {
                    teacher.UpdatedAt = DateTime.Now;
                    await teacherRepo.UpdateAsync(teacher);
                }

                historyRepo.Commit();

                return (true, "교사 전보 처리가 완료되었습니다.");
            }
            catch (Exception ex)
            {
                historyRepo.Rollback();
                Debug.WriteLine($"[TeacherService] 교사 전보 처리 실패: {ex.Message}");
                return (false, $"교사 전보 처리 중 오류가 발생했습니다: {ex.Message}");
            }
        }

        #endregion

        #region 교사 상태 관리

        /// <summary>
        /// 교사 퇴직 처리
        /// </summary>
        public async Task<(bool Success, string Message)> RetireTeacherAsync(
            string teacherId,
            string retireDate)
        {
            using var teacherRepo = new TeacherRepository(_dbPath);
            using var historyRepo = new TeacherSchoolHistoryRepository(_dbPath);

            try
            {
                teacherRepo.BeginTransaction();
                historyRepo.SetTransaction(teacherRepo.GetTransaction());

                // 1. 교사 상태 변경
                var teacher = await teacherRepo.GetByTeacherIdAsync(teacherId);
                if (teacher == null)
                {
                    teacherRepo.Rollback();
                    return (false, "교사를 찾을 수 없습니다.");
                }

                await teacherRepo.UpdateStatusAsync(teacher.No, "퇴직");

                // 2. 현재 근무 이력 종료
                await historyRepo.EndCurrentAsync(teacherId, retireDate);

                teacherRepo.Commit();

                return (true, "교사 퇴직 처리가 완료되었습니다.");
            }
            catch (Exception ex)
            {
                teacherRepo.Rollback();
                Debug.WriteLine($"[TeacherService] 교사 퇴직 처리 실패: {ex.Message}");
                return (false, $"교사 퇴직 처리 중 오류가 발생했습니다: {ex.Message}");
            }
        }

        /// <summary>
        /// 교사 상태 변경 (휴직, 복직 등)
        /// </summary>
        public async Task<(bool Success, string Message)> UpdateTeacherStatusAsync(
            string teacherId,
            string status)
        {
            using var teacherRepo = new TeacherRepository(_dbPath);

            try
            {
                var teacher = await teacherRepo.GetByTeacherIdAsync(teacherId);
                if (teacher == null)
                {
                    return (false, "교사를 찾을 수 없습니다.");
                }

                await teacherRepo.UpdateStatusAsync(teacher.No, status);

                return (true, $"{status} 처리가 완료되었습니다.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TeacherService] 교사 상태 변경 실패: {ex.Message}");
                return (false, $"교사 상태 변경 중 오류가 발생했습니다: {ex.Message}");
            }
        }

        #endregion

        #region 교사 정보 조회

        /// <summary>
        /// 교사 전체 정보 조회 (Teacher + 근무 이력)
        /// </summary>
        public async Task<TeacherFullInfo?> GetTeacherFullInfoAsync(string teacherId)
        {
            using var teacherRepo = new TeacherRepository(_dbPath);
            using var historyRepo = new TeacherSchoolHistoryRepository(_dbPath);

            try
            {
                var teacher = await teacherRepo.GetByTeacherIdAsync(teacherId);
                if (teacher == null) return null;

                var currentHistory = await historyRepo.GetCurrentByTeacherIdAsync(teacherId);
                var allHistory = await historyRepo.GetByTeacherIdAsync(teacherId);

                return new TeacherFullInfo
                {
                    Teacher = teacher,
                    CurrentHistory = currentHistory,
                    AllHistory = allHistory
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TeacherService] 교사 정보 조회 실패: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 특정 학교의 현재 재직 중인 교사 목록
        /// </summary>
        public async Task<List<TeacherWithHistory>> GetSchoolTeachersAsync(string schoolCode)
        {
            using var teacherRepo = new TeacherRepository(_dbPath);
            using var historyRepo = new TeacherSchoolHistoryRepository(_dbPath);

            try
            {
                var histories = await historyRepo.GetCurrentBySchoolCodeAsync(schoolCode);
                var teachers = new List<TeacherWithHistory>();

                foreach (var history in histories)
                {
                    var teacher = await teacherRepo.GetByTeacherIdAsync(history.TeacherID);
                    if (teacher != null)
                    {
                        teachers.Add(new TeacherWithHistory
                        {
                            Teacher = teacher,
                            History = history
                        });
                    }
                }

                return teachers.OrderBy(t => t.Teacher.Name).ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TeacherService] 학교 교사 목록 조회 실패: {ex.Message}");
                return new List<TeacherWithHistory>();
            }
        }

        /// <summary>
        /// 교사 검색
        /// </summary>
        public async Task<List<Teacher>> SearchTeachersAsync(string keyword)
        {
            using var teacherRepo = new TeacherRepository(_dbPath);

            try
            {
                return await teacherRepo.SearchAsync(keyword);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TeacherService] 교사 검색 실패: {ex.Message}");
                return new List<Teacher>();
            }
        }

        /// <summary>
        /// 과목별 교사 조회
        /// </summary>
        public async Task<List<Teacher>> GetTeachersBySubjectAsync(string subject)
        {
            using var teacherRepo = new TeacherRepository(_dbPath);

            try
            {
                return await teacherRepo.GetBySubjectAsync(subject);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TeacherService] 과목별 교사 조회 실패: {ex.Message}");
                return new List<Teacher>();
            }
        }

        #endregion

        #region 교사 정보 수정

        /// <summary>
        /// 교사 정보 수정
        /// </summary>
        public async Task<(bool Success, string Message)> UpdateTeacherAsync(Teacher teacher)
        {
            using var teacherRepo = new TeacherRepository(_dbPath);

            try
            {
                var success = await teacherRepo.UpdateAsync(teacher);
                return success
                    ? (true, "교사 정보가 수정되었습니다.")
                    : (false, "교사 정보 수정에 실패했습니다.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TeacherService] 교사 정보 수정 실패: {ex.Message}");
                return (false, $"교사 정보 수정 중 오류가 발생했습니다: {ex.Message}");
            }
        }

        /// <summary>
        /// 근무 이력 수정
        /// </summary>
        public async Task<(bool Success, string Message)> UpdateHistoryAsync(TeacherSchoolHistory history)
        {
            using var historyRepo = new TeacherSchoolHistoryRepository(_dbPath);

            try
            {
                var success = await historyRepo.UpdateAsync(history);
                return success
                    ? (true, "근무 이력이 수정되었습니다.")
                    : (false, "근무 이력 수정에 실패했습니다.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TeacherService] 근무 이력 수정 실패: {ex.Message}");
                return (false, $"근무 이력 수정 중 오류가 발생했습니다: {ex.Message}");
            }
        }

        #endregion

        #region 로그인 관리

        /// <summary>
        /// 교사 로그인
        /// </summary>
        public async Task<(bool Success, string Message, Teacher? Teacher)> LoginAsync(
            string loginId,
            string password)
        {
            using var teacherRepo = new TeacherRepository(_dbPath);

            try
            {
                var teacher = await teacherRepo.GetByLoginIdAsync(loginId);
                if (teacher == null)
                {
                    return (false, "존재하지 않는 사용자입니다.", null);
                }

                // TODO: 비밀번호 검증 (암호화된 비밀번호와 비교)
                // 현재는 임시로 생략

                // 마지막 로그인 시간 업데이트
                await teacherRepo.UpdateLastLoginAsync(teacher.No);

                return (true, "로그인에 성공했습니다.", teacher);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TeacherService] 로그인 실패: {ex.Message}");
                return (false, $"로그인 중 오류가 발생했습니다: {ex.Message}", null);
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
            }
        }

        #endregion
    }

    #region Helper Classes

    /// <summary>
    /// 교사 전체 정보 (조회용)
    /// </summary>
    public class TeacherFullInfo
    {
        public Teacher Teacher { get; set; } = new();
        public TeacherSchoolHistory? CurrentHistory { get; set; }
        public List<TeacherSchoolHistory> AllHistory { get; set; } = new();
    }

    /// <summary>
    /// 교사 + 근무 이력 (목록 조회용)
    /// </summary>
    public class TeacherWithHistory
    {
        public Teacher Teacher { get; set; } = new();
        public TeacherSchoolHistory History { get; set; } = new();
    }

    #endregion
}

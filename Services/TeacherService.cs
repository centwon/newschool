using System;
using System.Diagnostics;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Repositories;

namespace NewSchool.Services;

/// <summary>
/// 교사 등록 — 교사 행과 근무 이력을 <b>함께</b> 만든다.
///
/// 이 앱에는 교사를 관리하는 화면이 없다. 교사가 생기는 자리는 첫 실행 초기 설정 한 곳뿐이라
/// 서비스도 그 한 가지만 한다. (조회·수정·전보·퇴직 등 12개 중 10개는 만들어만 두고
/// 한 번도 부르지 않은 채였고, 그중 둘은 연결을 공유하지 않아 실제로는 동작하지도 않았다 —
/// 2026-08-23 정리.)
/// </summary>
public sealed class TeacherService : IDisposable
{
    private readonly string _dbPath;
    private bool _disposed;

    public TeacherService(string dbPath)
    {
        _dbPath = dbPath;
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

        // ⚠ 이력 Repository 는 반드시 교사 Repository 의 **연결을 공유**해야 한다.
        // 각자 연결을 열면 `SetTransaction` 을 해도 `CreateCommand` 의
        // "이 연결에서 시작된 트랜잭션인가" 검사에 걸려 조용히 무시되고, 이력 INSERT 가
        // 트랜잭션 밖 다른 연결에서 돌아 아직 커밋 안 된 교사 행을 못 본다
        // (외래키 위반이거나 쓰기 락 대기 → "database is locked").
        using var historyRepo = new TeacherSchoolHistoryRepository(teacherRepo.GetConnection());

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
            NewSchool.Logging.Log.Error("TeacherService", "교사 등록 실패(롤백함)", ex);
            return (false, $"교사 등록 중 오류가 발생했습니다: {ex.Message}", string.Empty);
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }
}

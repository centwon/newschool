using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using NewSchool.Controls;

namespace NewSchool.Scheduler;

/// <summary>
/// Scheduler 클래스 - 완전 리팩토링 버전
/// ✅ Ktask → KEvent 통합 완료
/// </summary>
public static class Scheduler
{
    // ✅ 매번 새로 생성하는 헬퍼
    public static SchedulerService CreateService() => new(DbPath);

    // ✅ 배치 트랜잭션용 UnitOfWork 생성
    public static UnitOfWork CreateUnitOfWork() => new(DbPath);

    // ✅ 전체 DB 경로
    private static string DbPath => Path.Combine(Settings.UserDataPath, Settings.SchedulerDB);

    #region Initialization

    public static async Task InitAsync()
    {
        try
        {
            // 데이터 디렉토리 생성
            string dataDir = Settings.UserDataPath;
            if (!Directory.Exists(dataDir))
            {
                Directory.CreateDirectory(dataDir);
                Debug.WriteLine($"[SchedulerDB] 데이터 디렉토리 생성: {dataDir}");
            }

            Debug.WriteLine($"[SchedulerDB] DB 경로: {DbPath}");
            Debug.WriteLine($"[SchedulerDB] DB 존재: {File.Exists(DbPath)}");
            Debug.WriteLine($"[SchedulerDB] 초기화 상태: {Settings.Scheduler_Inited.Value}");

            // 데이터베이스 초기화 — CREATE TABLE IF NOT EXISTS는 멱등적이므로 항상 실행
            Debug.WriteLine("[SchedulerDB] 데이터베이스 테이블 확인/초기화 시작");
            bool success = await InitDatabaseAsync();

            if (success)
            {
                Settings.Scheduler_Inited.Set(true);
                Debug.WriteLine("[SchedulerDB] 초기화/테이블 확인 완료");
            }
            else
            {
                await MessageBox.ShowAsync("스케줄러 데이터베이스 초기화에 실패하였습니다.", "오류");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SchedulerDB] 초기화 실패: {ex.Message}");
            Debug.WriteLine($"[SchedulerDB] StackTrace: {ex.StackTrace}");
            await MessageBox.ShowAsync($"초기화 오류: {ex.Message}", "오류");
        }
    }

    private static async Task<bool> InitDatabaseAsync()
    {
        try
        {
            using var dbInit = new DatabaseInitializer(DbPath);
            return await dbInit.InitializeAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SchedulerDB] DB 초기화 실패: {ex.Message}");
            return false;
        }
    }

    #endregion

    // 정적 헬퍼(GetTasksAsync/InsertTaskEventAsync/UpdateTaskEventAsync/DeleteTaskEventAsync)는
    // 호출부가 한 곳도 없었다(전수 조사 34차). 화면들은 CreateService() 로 SchedulerService 를
    // 직접 쓰므로 이 얇은 래퍼는 불필요하다.
    //
    // DB 유지보수 묶음(ValidateDatabaseAsync·OptimizeDatabaseAsync·BackupDatabaseAsync·
    // RestoreDatabaseAsync·ResetDatabaseAsync 와 전용 헬퍼 CheckpointWal·DeleteWalSidecars)도
    // 같은 이유로 지웠다(39차). school.db 쪽 같은 메서드들은 2026-08-16 에 이미 제거했고,
    // 사용자에게 보이는 백업·복원은 Settings 의 백업 ZIP(VACUUM INTO 스냅샷) 한 경로뿐이다.
    // 되살릴 일이 생기면 git 에서 꺼내되, 한 번도 돌아간 적 없는 코드라는 점에 유의할 것.
}

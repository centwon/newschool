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

    /// <summary>
    /// 일정 DB 초기화. 실패를 여기서 알리지 않는 이유는 <see cref="Board.Board.InitAsync"/> 와 같다 —
    /// 시작 경로에는 아직 창이 없어 대화상자가 뜨지 않는다. <c>App</c> 이 모아서 알린다.
    /// </summary>
    /// <returns>테이블까지 준비되면 true.</returns>
    public static async Task<bool> InitAsync()
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

            // 데이터베이스 초기화 — CREATE TABLE IF NOT EXISTS는 멱등적이므로 항상 실행
            Debug.WriteLine("[SchedulerDB] 데이터베이스 테이블 확인/초기화 시작");

            // 초기화 완료 플래그(Settings.Scheduler_Inited)는 세우기만 하고 읽는 곳이 없어
            // 설정째로 지웠다(2026-08-31). 게다가 성공할 때마다 조건 없이 Set 을 불러
            // 앱을 켤 때마다 설정 DB 에 쓰기가 한 번씩 났다. 초기화는 어차피 매번 도는 자리다.
            if (!await InitDatabaseAsync())
            {
                Logging.Log.Error("SchedulerDB", $"일정 DB 테이블을 준비하지 못했다: {DbPath}");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Logging.Log.Error("SchedulerDB", "일정 DB 초기화 실패", ex);
            return false;
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
            Logging.Log.Error("SchedulerDB", "일정 DB 테이블 생성 실패", ex);
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

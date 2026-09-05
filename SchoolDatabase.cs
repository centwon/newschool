using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace NewSchool;

/// <summary>
/// SchoolDatabase - School 데이터베이스 관리
/// 데이터베이스 초기화, 백업, 복원 등
/// Board.cs와 동일한 패턴
/// </summary>
public static class SchoolDatabase
{
    /// <summary>
    /// ⭐ 전체 DB 경로 (Public - 모든 곳에서 사용). <b>경로만 만든다 — 파일을 건드리지 않는다.</b>
    ///
    /// <para>⚠ 예전에는 이 getter 안에서 데이터 폴더를 만들었다. 읽는 곳이 130군데인데 거의
    /// 전부 화면 이벤트 한복판(<c>using var repo = new X(SchoolDatabase.DbPath)</c>)이라,
    /// 폴더를 만들지 못하면 <see cref="UnauthorizedAccessException"/> 이 <b>속성을 읽는 그 자리</b>
    /// 에서 튀었다 — 49차 "막지 않은 <c>Directory.CreateDirectory</c>" 의 친척이고, 130군데를
    /// 전부 감쌀 수는 없다. 남는 기록도 <c>Debug.WriteLine</c> 뿐이라 배포본에는 흔적조차
    /// 없었다(50차).</para>
    ///
    /// <para>형제인 <see cref="Board.Board"/>·<see cref="Scheduler.Scheduler"/> 는 처음부터
    /// 경로만 만들고 폴더는 각자 <c>InitAsync</c> 에서만 만든다 — 그쪽에 맞췄다. 폴더는
    /// <see cref="SettingsDb"/> 정적 생성자(실패해도 이유를 로그에 남기고 살아남는다)와
    /// <see cref="InitAsync"/> 가 만들며, 이 속성은 <see cref="Settings.SchoolDB"/> 를 거치므로
    /// <b>그 둘보다 먼저 도는 길이 없다</b>.</para>
    /// </summary>
    public static string DbPath => Path.Combine(Settings.UserDataPath, Settings.SchoolDB.Value);

    /// <summary>
    /// 데이터 폴더 경로
    /// </summary>
    public static string DataDirectory => Settings.UserDataPath;

    #region Initialization

    /// <summary>
    /// School 데이터베이스 초기화. App.xaml.cs 에서 호출한다.
    ///
    /// <para>⚠ 예전에는 <c>Task</c> 를 돌려주며 <b>모든 실패를 삼켰다</b>. 폴더를 못 만들거나
    /// 테이블을 못 만들어도 앱은 그대로 떴고, 화면마다 "자료가 하나도 없음" 으로 보였다.
    /// 기록도 <c>Debug.WriteLine</c> 뿐이라 배포본에서는 흔적조차 남지 않았다.
    /// 게시판·일정 DB 는 실패하면 적어도 안내를 띄웠는데(<see cref="Board.Board.InitAsync"/>),
    /// 정작 본 DB 만 조용했다 — 형제끼리 어긋난 자리다.</para>
    /// </summary>
    /// <returns>테이블까지 준비되면 true. 실패하면 false — 호출부가 사용자에게 알려야 한다.</returns>
    public static async Task<bool> InitAsync()
    {
        try
        {
            // 데이터 디렉토리 생성
            string dataDir = Settings.DataDirectory;
            if (!Directory.Exists(dataDir))
            {
                Directory.CreateDirectory(dataDir);
                Debug.WriteLine($"[SchoolDatabase] 데이터 디렉토리 생성: {dataDir}");
            }

            Debug.WriteLine($"[SchoolDatabase] DB 경로: {DbPath}");
            Debug.WriteLine($"[SchoolDatabase] DB 존재: {File.Exists(DbPath)}");

            // 데이터베이스 초기화 (CREATE TABLE IF NOT EXISTS → 항상 안전)
            Debug.WriteLine("[SchoolDatabase] 데이터베이스 초기화 시작");
            bool success = await InitDatabaseAsync();

            if (success)
                Debug.WriteLine("[SchoolDatabase] 초기화 완료");
            else
                Logging.Log.Error("SchoolDatabase", $"학교 DB 테이블을 준비하지 못했다: {DbPath}");

            return success;
        }
        catch (Exception ex)
        {
            Logging.Log.Error("SchoolDatabase", "학교 DB 초기화 실패", ex);
            return false;
        }
    }

    /// <summary>
    /// 데이터베이스 테이블 생성
    /// </summary>
    private static async Task<bool> InitDatabaseAsync()
    {
        try
        {
            using var initializer = new Database.DatabaseInitializer(DbPath);
            return await initializer.InitializeAsync();
        }
        catch (Exception ex)
        {
            Logging.Log.Error("SchoolDatabase", "학교 DB 테이블 생성 실패", ex);
            return false;
        }
    }

    #endregion

    #region Database Management

    // 미사용 메서드 제거 (2026-08-16): BackupDatabaseAsync·RestoreDatabaseAsync — 호출처 0건.
    // 전자는 Settings.Backup 이 쓰는 Backups\ 와 별개로 Backup\ 폴더에 School_*.db 를 쌓아
    // 정리 정책도 복원 경로도 없는 사본을 만들었다. 백업은 Settings.Backup/Restore 하나로 간다.

    /// <summary>
    /// 데이터베이스 완전 초기화 (모든 데이터 삭제)
    /// </summary>
    public static async Task<bool> ResetDatabaseAsync()
    {
        try
        {
            Debug.WriteLine("[SchoolDatabase] 데이터베이스 완전 초기화 시작");

            // DB 파일 삭제
            if (File.Exists(DbPath))
            {
                File.Delete(DbPath);
                Debug.WriteLine("[SchoolDatabase] 기존 DB 파일 삭제 완료");
            }

            // 재초기화. ⚠ 결과를 버리면 "초기화했습니다" 라고 말해 놓고 테이블이 없는 채로
            // 남는다 — 지우는 것만 성공하고 다시 만들지 못한 상태가 가장 나쁘다.
            if (!await InitAsync())
            {
                Logging.Log.Error("SchoolDatabase", "DB 를 지운 뒤 다시 만들지 못했다");
                return false;
            }

            Debug.WriteLine("[SchoolDatabase] 데이터베이스 완전 초기화 완료");
            return true;
        }
        catch (Exception ex)
        {
            Logging.Log.Error("SchoolDatabase", "DB 완전 초기화 실패", ex);
            return false;
        }
    }

    #endregion

    #region Utility Methods

    /// <summary>
    /// DB 파일 존재 여부 확인
    /// </summary>
    public static bool DatabaseExists()
    {
        return File.Exists(DbPath);
    }

    // DB 파일 크기 조회 둘(GetDatabaseSize·GetDatabaseSizeFormatted)은 호출부가 없어
    // 지웠다(39차) — 크기를 보여 주는 화면이 없다.

    #endregion
}

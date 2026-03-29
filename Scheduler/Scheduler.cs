using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Controls;
using NewSchool.Models;
using NewSchool.Scheduler.Repositories;
using NewSchool.Services;

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

    #region Database Management

    /// <summary>
    /// 데이터베이스 검증 (비동기)
    /// </summary>
    public static async Task<bool> ValidateDatabaseAsync()
    {
        try
        {
            if (!File.Exists(DbPath))
            {
                Debug.WriteLine("[SchedulerDB] DB 파일이 존재하지 않습니다.");
                return false;
            }

            // KEventRepository로 검증
            using var repo = new KEventRepository(DbPath);
            await repo.GetTaskCountAsync();

            Debug.WriteLine("[SchedulerDB] 데이터베이스 검증 완료");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SchedulerDB] DB 검증 실패: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 데이터베이스 최적화 (비동기)
    /// </summary>
    public static async Task<bool> OptimizeDatabaseAsync()
    {
        try
        {
            return await Task.Run(() =>
            {
                using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={DbPath}");
                connection.Open();
                using var cmd = connection.CreateCommand();

                cmd.CommandText = "VACUUM";
                cmd.ExecuteNonQuery();

                cmd.CommandText = "ANALYZE";
                cmd.ExecuteNonQuery();

                Debug.WriteLine("[SchedulerDB] 데이터베이스 최적화 완료");
                return true;
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SchedulerDB] DB 최적화 실패: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 데이터베이스 백업 (비동기)
    /// </summary>
    public static async Task<string?> BackupDatabaseAsync()
    {
        try
        {
            if (!File.Exists(DbPath))
            {
                Debug.WriteLine("[SchedulerDB] 백업할 DB 파일이 없습니다.");
                return null;
            }

            string timestamp = DateTime.Now.ToString("yyyy_MM_dd_HH_mm_ss",
                System.Globalization.CultureInfo.InvariantCulture);
            string backupFileName = $"scheduler_backup_{timestamp}.db";
            string backupPath = Path.Combine(
                Path.GetDirectoryName(DbPath) ?? AppDomain.CurrentDomain.BaseDirectory,
                backupFileName
            );

            await Task.Run(() => File.Copy(DbPath, backupPath, true));
            Debug.WriteLine($"[SchedulerDB] DB 백업 완료: {backupPath}");
            return backupPath;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SchedulerDB] DB 백업 실패: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 데이터베이스 복원 (비동기)
    /// </summary>
    public static async Task<bool> RestoreDatabaseAsync(string backupPath)
    {
        try
        {
            if (!File.Exists(backupPath))
            {
                Debug.WriteLine("[SchedulerDB] 백업 파일이 존재하지 않습니다.");
                return false;
            }

            await Task.Run(() => File.Copy(backupPath, DbPath, true));
            Debug.WriteLine($"[SchedulerDB] DB 복원 완료: {backupPath}");

            return await ValidateDatabaseAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SchedulerDB] DB 복원 실패: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 데이터베이스 완전 초기화 (비동기)
    /// </summary>
    public static async Task<bool> ResetDatabaseAsync()
    {
        try
        {
            var window = App.MainWindow;
            if (window == null)
            {
                await MessageBox.ShowAsync("메인 윈도우를 찾을 수 없습니다. 데이터베이스 초기화를 취소합니다.", "오류");
                return false;
            }
            var confirmed = await MessageBox.ShowConfirmAsync(
                "모든 작업과 학사일정이 삭제됩니다.\n정말 초기화하시겠습니까?",
                "데이터베이스 초기화", "초기화", "취소");
            if (!confirmed)
                return false;

            // DB 파일 삭제
            if (File.Exists(DbPath))
            {
                File.Delete(DbPath);
                Debug.WriteLine("[SchedulerDB] DB 파일 삭제됨");
            }

            // 재초기화
            Settings.Scheduler_Inited.Set(false);
            await InitAsync();

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SchedulerDB] DB 초기화 실패: {ex.Message}");
            return false;
        }
    }

    #endregion

    #region Static Helper Methods (비동기, KEvent 통합)

    /// <summary>
    /// task 조회 (비동기, KEvent ItemType="task")
    /// </summary>
    public static async Task<List<KEvent>> GetTasksAsync(string dbPath, DateTime startDay, uint period = 0, bool showDone = true)
    {
        using var service = new SchedulerService(dbPath);
        int days = period == 0 ? 1 : (int)period;
        return await service.GetTasksByDateAsync(startDay, days, showDone);
    }

    public static async Task<List<KEvent>> GetTasksAsync(DateTime startDay, uint period = 0, bool showDone = true)
    {
        return await GetTasksAsync(DbPath, startDay, period, showDone);
    }

    /// <summary>
    /// task 생성 (비동기, KEvent ItemType="task")
    /// </summary>
    public static async Task<int> InsertTaskEventAsync(string dbPath, KEvent taskEvent)
    {
        using var service = new SchedulerService(dbPath);
        return await service.CreateTaskAsync(taskEvent);
    }

    public static async Task<int> InsertTaskEventAsync(KEvent taskEvent)
    {
        return await InsertTaskEventAsync(DbPath, taskEvent);
    }

    /// <summary>
    /// task 수정 (비동기)
    /// </summary>
    public static async Task UpdateTaskEventAsync(string dbPath, KEvent taskEvent)
    {
        using var service = new SchedulerService(dbPath);
        await service.UpdateTaskAsync(taskEvent);
    }

    public static async Task UpdateTaskEventAsync(KEvent taskEvent)
    {
        await UpdateTaskEventAsync(DbPath, taskEvent);
    }

    /// <summary>
    /// task 삭제 (비동기)
    /// </summary>
    public static async Task<bool> DeleteTaskEventAsync(int taskNo)
    {
        using var service = new SchedulerService(DbPath);
        return await service.DeleteTaskAsync(taskNo);
    }

    #endregion
}

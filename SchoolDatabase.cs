using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace NewSchool
{
    /// <summary>
    /// SchoolDatabase - School 데이터베이스 관리
    /// 데이터베이스 초기화, 백업, 복원 등
    /// Board.cs와 동일한 패턴
    /// </summary>
    public static class SchoolDatabase
    {
        /// <summary>
        /// ⭐ 전체 DB 경로 (Public - 모든 곳에서 사용)
        /// Data 폴더 자동 생성 및 전체 경로 반환
        /// </summary>
        public static string DbPath
        {
            get
            {
                string dataDir = Settings.UserDataPath;

                if (!Directory.Exists(dataDir))
                {
                    Directory.CreateDirectory(dataDir);
                    Debug.WriteLine($"[SchoolDatabase] 데이터 폴더 생성: {dataDir}");
                }

                return Path.Combine(dataDir, Settings.SchoolDB.Value);
            }
        }

        /// <summary>
        /// 데이터 폴더 경로
        /// </summary>
        public static string DataDirectory => Settings.UserDataPath;

        #region Initialization

        /// <summary>
        /// School 데이터베이스 초기화
        /// App.xaml.cs에서 호출
        /// </summary>
        public static async Task InitAsync()
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
                Debug.WriteLine($"[SchoolDatabase] 초기화 상태: {Settings.School_Inited.Value}");

                // 데이터베이스 초기화 (CREATE TABLE IF NOT EXISTS → 항상 안전)
                Debug.WriteLine("[SchoolDatabase] 데이터베이스 초기화 시작");
                bool success = await InitDatabaseAsync();

                if (success)
                {
                    Settings.School_Inited.Set(true);
                    Debug.WriteLine("[SchoolDatabase] 초기화 완료");
                }
                else
                {
                    Debug.WriteLine("[SchoolDatabase] 데이터베이스 초기화 실패");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SchoolDatabase] 초기화 중 예외 발생: {ex.Message}");
                Debug.WriteLine($"[SchoolDatabase] StackTrace: {ex.StackTrace}");
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
                Debug.WriteLine($"[SchoolDatabase] DB 초기화 실패: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Database Management

        /// <summary>
        /// 데이터베이스 백업
        /// </summary>
        public static async Task<bool> BackupDatabaseAsync()
        {
            try
            {
                if (!File.Exists(DbPath))
                {
                    Debug.WriteLine("[SchoolDatabase] 백업할 DB 파일이 없습니다.");
                    return false;
                }

                string backupDir = Path.Combine(Settings.UserDataPath, "Backup");

                if (!Directory.Exists(backupDir))
                {
                    Directory.CreateDirectory(backupDir);
                }

                string backupFileName = $"School_{DateTime.Now:yyyyMMdd_HHmmss}.db";
                string backupPath = Path.Combine(backupDir, backupFileName);

                await Task.Run(() => File.Copy(DbPath, backupPath, true));

                Debug.WriteLine($"[SchoolDatabase] DB 백업 완료: {backupPath}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SchoolDatabase] DB 백업 실패: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 데이터베이스 복원
        /// </summary>
        public static async Task<bool> RestoreDatabaseAsync(string backupPath)
        {
            try
            {
                if (!File.Exists(backupPath))
                {
                    Debug.WriteLine($"[SchoolDatabase] 백업 파일이 존재하지 않습니다: {backupPath}");
                    return false;
                }

                await Task.Run(() => File.Copy(backupPath, DbPath, true));

                Debug.WriteLine($"[SchoolDatabase] DB 복원 완료: {backupPath}");
                Settings.School_Inited.Set(false); // 재초기화 필요
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SchoolDatabase] DB 복원 실패: {ex.Message}");
                return false;
            }
        }

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

                // 재초기화
                Settings.School_Inited.Set(false);
                await InitAsync();

                Debug.WriteLine("[SchoolDatabase] 데이터베이스 완전 초기화 완료");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SchoolDatabase] DB 초기화 실패: {ex.Message}");
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

        /// <summary>
        /// DB 파일 크기 가져오기 (bytes)
        /// </summary>
        public static long GetDatabaseSize()
        {
            if (!File.Exists(DbPath))
                return 0;

            var fileInfo = new FileInfo(DbPath);
            return fileInfo.Length;
        }

        /// <summary>
        /// DB 파일 크기 문자열로 가져오기 (KB, MB)
        /// </summary>
        public static string GetDatabaseSizeFormatted()
        {
            long bytes = GetDatabaseSize();

            if (bytes == 0)
                return "0 KB";

            if (bytes < 1024)
                return $"{bytes} B";

            if (bytes < 1024 * 1024)
                return $"{bytes / 1024.0:F2} KB";

            return $"{bytes / (1024.0 * 1024.0):F2} MB";
        }

        #endregion
    }
}

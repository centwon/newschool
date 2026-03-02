using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using Microsoft.Data.Sqlite;

namespace NewSchool;

/// <summary>
/// 설정 속성 (Fluent API)
/// </summary>
public class SettingProperty<T>
{
    private readonly string _key;
    private T _value;
    private readonly Func<string, T> _parser;
    private readonly Func<T, string> _serializer;

    internal SettingProperty(string key, T defaultValue, Func<string, T> parser, Func<T, string> serializer)
    {
        _key = key;
        _value = defaultValue;
        _parser = parser;
        _serializer = serializer;
    }

    /// <summary>
    /// 값 가져오기 (캐시에서 읽기, 초고속)
    /// </summary>
    public T Value
    {
        get => _value;
        set => _value = value;
    }

    /// <summary>
    /// 값 변경 후 저장
    /// </summary>
    public void Save()
    {
        SettingsDb.Set(_key, _serializer(_value));
    }

    /// <summary>
    /// 값 설정 + 저장 (체이닝 가능)
    /// </summary>
    public SettingProperty<T> Set(T value)
    {
        _value = value;
        Save();
        return this;
    }

    /// <summary>
    /// DB에서 다시 로드
    /// </summary>
    public void Reload()
    {
        string? strValue = SettingsDb.Get(_key);
        if (!string.IsNullOrEmpty(strValue))
        {
            _value = _parser(strValue);
        }
    }

    // 암시적 변환 (자동으로 값 반환)
    public static implicit operator T(SettingProperty<T> prop) => prop.Value;

    public override string ToString() => _value?.ToString() ?? "";
}

/// <summary>
/// 메인 설정 클래스 (Fluent API)
/// </summary>
public static class Settings
{
    // Scheduler 관련 설정
    public static SettingProperty<string> SchedulerDB { get; private set; } = null!;
    public static SettingProperty<bool> Scheduler_Inited { get; private set; } = null!;

    public static SettingProperty<bool> ShowEvents { get; private set; } = null!;
    public static SettingProperty<bool> ShowTasks { get; private set; } = null!;
    public static SettingProperty<double> EventFontSize { get; private set; } = null!;
    public static SettingProperty<double> TaskFontSize { get; private set; } = null!;
    public static SettingProperty<bool> UseGoogle { get; private set; } = null!;
    public static SettingProperty<string> GoogleCalendarName { get; private set; } = null!;
    public static SettingProperty<string> GoogleCalendarID { get; private set; } = null!;

    // Google OAuth 인증
    public static SettingProperty<string> GoogleClientId { get; private set; } = null!;
    public static SettingProperty<string> GoogleClientSecret { get; private set; } = null!;
    public static SettingProperty<string> GoogleAccessToken { get; private set; } = null!;
    public static SettingProperty<string> GoogleRefreshToken { get; private set; } = null!;
    public static SettingProperty<string> GoogleTokenExpiry { get; private set; } = null!;
    public static SettingProperty<bool> GoogleAutoSync { get; private set; } = null!;
    public static SettingProperty<int> GoogleSyncIntervalMinutes { get; private set; } = null!;
    public static SettingProperty<string> GoogleLastSyncTime { get; private set; } = null!;

    // School 관련 설정
    public static SettingProperty<string> SchoolDB { get; private set; } = null!;
    public static SettingProperty<bool> School_Inited { get; private set; } = null!;

    public static SettingProperty<string> User { get; private set; } = null!;
    public static SettingProperty<int> WorkYear { get; private set; } = null!;
    public static SettingProperty<string> ProvinceCode { get; private set; } = null!;
    public static SettingProperty<string> SchoolCode { get; private set; } = null!;
    public static SettingProperty<string> SchoolName { get; private set; } = null!;
    public static SettingProperty<string> SchoolAddress { get; private set; } = null!;
    public static SettingProperty<string> ProvinceName { get; private set; } = null!;
    public static SettingProperty<string> NeisApiKey { get; private set; } = null!;
    public static SettingProperty<int> WorkSemester { get; private set; } = null!;
    public static SettingProperty<bool> TopMost { get; private set; } = null!;
    public static SettingProperty<string> UserName { get; private set; } = null!;
    public static SettingProperty<bool> IsNeisEventDownloaded { get; private set; } = null!;

    // school period 설정
    public static SettingProperty<TimeSpan> AssemblyTime { get; private set; } = null!;
    public static SettingProperty<TimeSpan> DayStarting { get; private set; } = null!;
    public static SettingProperty<TimeSpan> BreakTime { get; private set; } = null!;
    public static SettingProperty<TimeSpan> OnePeriod { get; private set; } = null!;
    public static SettingProperty<TimeSpan> LunchTime { get; private set; } = null!;

    // school homeroom 설정
    public static SettingProperty<int> HomeGrade { get; private set; } = null!;
    public static SettingProperty<int> HomeRoom { get; private set; } = null!;

    //Board 설정
    public static SettingProperty<string> Board_DB { get; private set; } = null!;
    public static SettingProperty<bool> Board_Inited { get; private set; } = null!;
    /// <summary>
    /// 캐시 활성화 여부
    /// </summary>
    public static SettingProperty<bool> EnableCache { get; private set; } = null!;

    /// <summary>
    /// 페이지 크기 (기본값)
    /// </summary>
    public static SettingProperty<int> DefaultPageSize { get; private set; } = null!;
    /// <summary>
    /// 자동 백업 활성화
    /// </summary>
    public static SettingProperty<bool> AutoBackup { get; private set; } = null!;
    /// <summary>
    /// 자동 백업 간격 (일)
    /// </summary>
    public static SettingProperty<int> AutoBackupIntervalDays { get; private set; } = null!;

    /// <summary>
    /// 백업 보관 개수
    /// </summary>
    public static SettingProperty<int> BackupRetentionCount { get; private set; } = null!;
    /// <summary>
    /// 로그 레벨
    /// </summary>
    public static SettingProperty<string> LogLevel { get; private set; } = null!;
    /// <summary>
    /// 테마
    /// </summary>
    public static SettingProperty<string> Theme { get; private set; } = null!;

    /// <summary>
    /// 언어
    /// </summary>
    public static SettingProperty<string> Language { get; private set; } = null!;

    /// <summary>
    /// 창 너비
    /// </summary>
    public static SettingProperty<int> WindowWidth { get; private set; } = null!;

    /// <summary>
    /// 창 높이
    /// </summary>
    public static SettingProperty<int> WindowHeight { get; private set; } = null!;   

    /// <summary>
    /// 설정 초기화 (앱 시작 시 한 번 호출)
    /// </summary>
    public static void Initialize()
    {
        System.Diagnostics.Debug.WriteLine("[Settings] 초기화 시작");

        // DB 초기화
        SettingsDb.Initialize();

        // 속성 초기화 (파서와 직렬화기 지정)
        SchedulerDB = new SettingProperty<string>("SchedulerDB", "scheduler.db", s => s, s => s);
        Scheduler_Inited = new SettingProperty<bool>("Scheduler_Inited", false, bool.Parse, b => b.ToString().ToLower());

        ShowEvents = new SettingProperty<bool>("ShowEvents", true, bool.Parse, b => b.ToString().ToLower());
        ShowTasks = new SettingProperty<bool>("ShowTasks", true, bool.Parse, b => b.ToString().ToLower());
        EventFontSize = new SettingProperty<double>("EventFontSize", 9.0, double.Parse, d => d.ToString());
        TaskFontSize = new SettingProperty<double>("TaskFontSize", 10.0, double.Parse, d => d.ToString());
        UseGoogle = new SettingProperty<bool>("UseGoogle", false, bool.Parse, b => b.ToString().ToLower());
        GoogleCalendarName = new SettingProperty<string>("GoogleCalendarName", "", s => s, s => s);
        GoogleCalendarID = new SettingProperty<string>("GoogleCalendarID", "", s => s, s => s);

        // Google OAuth
        GoogleClientId = new SettingProperty<string>("GoogleClientId", "", s => s, s => s);
        GoogleClientSecret = new SettingProperty<string>("GoogleClientSecret", "", s => s, s => s);
        GoogleAccessToken = new SettingProperty<string>("GoogleAccessToken", "", s => s, s => s);
        GoogleRefreshToken = new SettingProperty<string>("GoogleRefreshToken", "", s => s, s => s);
        GoogleTokenExpiry = new SettingProperty<string>("GoogleTokenExpiry", "", s => s, s => s);
        GoogleAutoSync = new SettingProperty<bool>("GoogleAutoSync", false, bool.Parse, b => b.ToString().ToLower());
        GoogleSyncIntervalMinutes = new SettingProperty<int>("GoogleSyncIntervalMinutes", 15, int.Parse, i => i.ToString());
        GoogleLastSyncTime = new SettingProperty<string>("GoogleLastSyncTime", "", s => s, s => s);

        User = new SettingProperty<string>("User", "user", s => s, s => s);
        WorkYear = new SettingProperty<int>("WorkYear", 0, int.Parse, i => i.ToString());
        ProvinceCode = new SettingProperty<string>("ProvinceCode", "", s => s, s => s);
        SchoolCode = new SettingProperty<string>("SchoolCode", "", s => s, s => s);
        SchoolName = new SettingProperty<string>("SchoolName", "", s => s, s => s);
        SchoolAddress = new SettingProperty<string>("SchoolAddress", "", s => s, s => s);
        ProvinceName = new SettingProperty<string>("ProvinceName", "", s => s, s => s);
        NeisApiKey = new SettingProperty<string>("NeisApiKey", "REMOVED_NEIS_KEY", s => s, s => s);
        WorkSemester = new SettingProperty<int>("WorkSemester", 0, int.Parse, i => i.ToString());
        TopMost = new SettingProperty<bool>("TopMost", true, bool.Parse, b => b.ToString().ToLower());
        UserName = new SettingProperty<string>("UserName", "", s => s, s => s);
        IsNeisEventDownloaded = new SettingProperty<bool>("IsNeisEventDownloaded", false, bool.Parse, b => b.ToString().ToLower());

        AssemblyTime = new SettingProperty<TimeSpan>("AssemblyTime", TimeSpan.FromMinutes(10), TimeSpan.Parse, ts => ts.ToString());
        DayStarting = new SettingProperty<TimeSpan>("DayStarting", new TimeSpan(8, 30, 0), TimeSpan.Parse, ts => ts.ToString());
        BreakTime = new SettingProperty<TimeSpan>("BreakTime", TimeSpan.FromMinutes(10), TimeSpan.Parse, ts => ts.ToString());
        OnePeriod = new SettingProperty<TimeSpan>("OnePeriod", TimeSpan.FromMinutes(45), TimeSpan.Parse, ts => ts.ToString());
        LunchTime = new SettingProperty<TimeSpan>("LunchTime", TimeSpan.FromMinutes(50), TimeSpan.Parse, ts => ts.ToString());

        HomeGrade = new SettingProperty<int>("HomeGrade", 0, int.Parse, d => d.ToString());
        HomeRoom = new SettingProperty<int>("HomeRoom", 0, int.Parse, d => d.ToString());
        SchoolDB = new SettingProperty<string>("SchoolDB", "school.db", s => s, s => s);
        School_Inited = new SettingProperty<bool>("SchoolDB_Inited", false, bool.Parse, b => b.ToString().ToLower());
        Board_DB = new SettingProperty<string>("Board_DB", "board.db", s => s, s => s);
        Board_Inited = new SettingProperty<bool>("Board_Init", false, bool.Parse, b => b.ToString().ToLower());

                /// <summary>
                /// 캐시 활성화 여부
                /// </summary>
        EnableCache = new SettingProperty<bool>("EnableCache", true, bool.Parse, b => b.ToString().ToLower());

    /// <summary>
    /// 페이지 크기 (기본값)
    /// </summary>
    DefaultPageSize = new SettingProperty<int>("DefaultPageSize", 20, int.Parse, i => i.ToString());


        /// <summary>
        /// 자동 백업 활성화
        /// </summary>
        AutoBackup = new SettingProperty<bool>("AutoBackup", false, bool.Parse, b => b.ToString().ToLower());

    /// <summary>
    /// 자동 백업 간격 (일)
    /// </summary>
    AutoBackupIntervalDays = new SettingProperty<int>("AutoBackupIntervalDays", 7, int.Parse, i => i.ToString());

        /// <summary>
        /// 백업 보관 개수
        /// </summary>
        BackupRetentionCount = new SettingProperty<int>("BackupRetentionCount", 20, int.Parse, i => i.ToString());

    /// <summary>
    /// 로그 레벨
    /// </summary>
    LogLevel = new SettingProperty<string>("LogLevel", "Info", s => s, s => s);

    /// <summary>
    /// 테마
    /// </summary>
    Theme = new SettingProperty<string>("Theme", "Light", s => s, s => s);

    /// <summary>
    /// 언어
    /// </summary>
    Language = new SettingProperty<string>("Language", "ko-KR", s => s, s => s);

    /// <summary>
    /// 창 크기 (기본값: 1400x900)
    /// </summary>
    WindowWidth = new SettingProperty<int>("WindowWidth", 1400, int.Parse, i => i.ToString());
    WindowHeight = new SettingProperty<int>("WindowHeight", 900, int.Parse, i => i.ToString());




    // DB에서 모든 값 로드
    LoadAll();

        System.Diagnostics.Debug.WriteLine("[Settings] 초기화 완료");
    }

    /// <summary>
    /// DB에서 모든 설정 로드
    /// </summary>
    private static void LoadAll()
    {
        SchedulerDB.Reload();
        Scheduler_Inited.Reload();
        ShowEvents.Reload();
        ShowTasks.Reload();
        EventFontSize.Reload();
        TaskFontSize.Reload();
        UseGoogle.Reload();
        GoogleCalendarName.Reload();
        GoogleCalendarID.Reload();
        GoogleClientId.Reload();
        GoogleClientSecret.Reload();
        GoogleAccessToken.Reload();
        GoogleRefreshToken.Reload();
        GoogleTokenExpiry.Reload();
        GoogleAutoSync.Reload();
        GoogleSyncIntervalMinutes.Reload();
        GoogleLastSyncTime.Reload();
        SchoolDB.Reload();
        School_Inited.Reload();
        User.Reload();
        WorkYear.Reload();
        ProvinceCode.Reload();
        SchoolCode.Reload();
        SchoolName.Reload();
        SchoolAddress.Reload();
        ProvinceName.Reload();
        NeisApiKey.Reload();
        WorkSemester.Reload();
        TopMost.Reload();
        AutoBackup.Reload();
        UserName.Reload();
        IsNeisEventDownloaded.Reload();

        AssemblyTime.Reload();
        DayStarting.Reload();
        BreakTime.Reload();
        OnePeriod.Reload();
        LunchTime.Reload();

        HomeGrade.Reload();
        HomeRoom.Reload();

        Board_DB.Reload();
        Board_Inited.Reload();
        EnableCache.Reload();
        DefaultPageSize.Reload();
        AutoBackupIntervalDays.Reload();
        BackupRetentionCount.Reload();
        LogLevel.Reload();

        Theme.Reload();
        Language.Reload();
        WindowWidth.Reload();
        WindowHeight.Reload();


    }

    /// <summary>
    /// 기본값으로 리셋
    /// </summary>
    public static void ResetToDefaults()
    {
        SettingsDb.ResetToDefaults();
        LoadAll();
    }

    /// <summary>
    /// 설정 백업
    /// </summary>
    public static string? Backup() => SettingsDb.Backup();

    /// <summary>
    /// 설정 복원
    /// </summary>
    public static bool Restore(string backupPath)
    {
        bool success = SettingsDb.Restore(backupPath);
        if (success) LoadAll();
        return success;
    }

    /// <summary>
    /// 디버그 출력
    /// </summary>
    public static void PrintAll() => SettingsDb.PrintAllSettings();
}

/// <summary>
/// 내부 DB 관리 클래스
/// </summary>
internal static class SettingsDb
{
    private static readonly string DbPath;
    private static readonly string ConnectionString;
    private static readonly Dictionary<string, string> _cache = new();
    private static readonly object _lock = new();
    private static bool _isInitialized = false;

    static SettingsDb()
    {
        DbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Settings.db");
        ConnectionString = $"Data Source={DbPath};Cache=Shared";
    }

    public static void Initialize()
    {
        lock (_lock)
        {
            if (_isInitialized) return;

            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();

            var createSql = @"
                    CREATE TABLE IF NOT EXISTS Settings (
                        Key TEXT PRIMARY KEY,
                        Value TEXT NOT NULL,
                        Type TEXT NOT NULL,
                        Description TEXT,
                        Updated TEXT NOT NULL
                    )";

            using var cmd = new SqliteCommand(createSql, conn);
            cmd.ExecuteNonQuery();

            // 캐시 로드
            LoadCache(conn);

            _isInitialized = true;
        }
    }

    private static void LoadCache(SqliteConnection conn)
    {
        _cache.Clear();
        var sql = "SELECT Key, Value FROM Settings";
        using var cmd = new SqliteCommand(sql, conn);
        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            _cache[reader.GetString(0)] = reader.GetString(1);
        }
    }

    public static string? Get(string key)
    {
        lock (_lock)
        {
            return _cache.TryGetValue(key, out var value) ? value : null;
        }
    }

    public static void Set(string key, string value)
    {
        lock (_lock)
        {
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();

            var sql = @"
                    INSERT INTO Settings (Key, Value, Type, Description, Updated)
                    VALUES (@Key, @Value, '', '', @Updated)
                    ON CONFLICT(Key) DO UPDATE SET 
                        Value = @Value,
                        Updated = @Updated";

            using var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Key", key);
            cmd.Parameters.AddWithValue("@Value", value ?? "");
            cmd.Parameters.AddWithValue("@Updated", DateTime.UtcNow.ToString("o"));
            cmd.ExecuteNonQuery();

            _cache[key] = value ?? "";
        }
    }

    public static void ResetToDefaults()
    {
        lock (_lock)
        {
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();

            var deleteSql = "DELETE FROM Settings";
            using var cmd = new SqliteCommand(deleteSql, conn);
            cmd.ExecuteNonQuery();

            _cache.Clear();
        }
    }

    public static string? Backup()
    {
        lock (_lock)
        {
            try
            {
                string backupFileName = $"appsettings_backup_{DateTime.Now:yyyyMMdd_HHmmss}.db";
                string? dir = Path.GetDirectoryName(DbPath);
                if (string.IsNullOrEmpty(dir))
                    throw new InvalidOperationException("DbPath의 디렉터리 경로를 확인할 수 없습니다.");
                string backupPath = Path.Combine(dir, backupFileName);
                File.Copy(DbPath, backupPath, true);
                return backupPath;
            }
            catch
            {
                return null;
            }
        }
    }

    public static bool Restore(string backupPath)
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(backupPath))
                {
                    File.Copy(backupPath, DbPath, true);
                    _isInitialized = false;
                    Initialize();
                    return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }
    }

    public static void PrintAllSettings()
    {
        lock (_lock)
        {
            System.Diagnostics.Debug.WriteLine("=== Settings ===");
            foreach (var kvp in _cache)
            {
                System.Diagnostics.Debug.WriteLine($"{kvp.Key}: {kvp.Value}");
            }
            System.Diagnostics.Debug.WriteLine("================");
        }
    }

}


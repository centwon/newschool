using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace NewSchool;

/// <summary>
/// 설정 속성 (Fluent API)
/// </summary>
public class SettingProperty<T>
{
    private readonly string _key;
    private T _value;
    private readonly T _defaultValue;
    private readonly Func<string, T> _parser;
    private readonly Func<T, string> _serializer;

    internal SettingProperty(string key, T defaultValue, Func<string, T> parser, Func<T, string> serializer)
    {
        _key = key;
        _value = defaultValue;
        _defaultValue = defaultValue;
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
    /// DB에서 다시 로드. <b>저장된 값이 없으면 기본값으로 되돌린다.</b>
    ///
    /// <para>예전에는 값이 없을 때 현재 값을 그대로 뒀다. 그래서 [설정 초기화]
    /// (<see cref="Settings.ResetToDefaults"/>, DB 를 비우고 <c>LoadAll</c>)를 눌러도 실행 중인 앱의
    /// 설정은 하나도 바뀌지 않았다 — 화면은 옛 값을 그대로 다시 그리면서 "초기화되었습니다" 라고
    /// 알렸고, 그 뒤 설정 하나만 저장돼도(창 크기를 끌기만 해도) 옛 값이 DB 에 되살아났다.
    /// 복원도 같은 함수를 쓰므로, 백업에 없던 키가 현재 값으로 남았다.</para>
    ///
    /// <para>파싱 실패도 기본값으로 떨어뜨린다. 예전에는 <c>int.Parse</c> 가 그대로 터져
    /// <see cref="Settings.Initialize"/> → <c>OnLaunched</c>(async void) 로 예외가 빠져나가
    /// 값 하나가 깨졌을 뿐인데 앱이 안내도 없이 죽었다. 시작 시 무결성 점검은 파일 손상만 본다.</para>
    /// </summary>
    public void Reload() => _value = Interpret(SettingsDb.Get(_key));

    /// <summary>
    /// <see cref="Reload"/> 의 순수 함수 부분 — 저장된 문자열 하나를 값으로 옮긴다.
    /// (저장소와 분리해 두어야 "없을 때·깨졌을 때" 규칙을 DB 없이 테스트할 수 있다.)
    /// </summary>
    internal T Interpret(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return _defaultValue;

        try
        {
            return _parser(stored);
        }
        catch (Exception ex)
        {
            Logging.Log.Warning("Settings",
                $"'{_key}' 값을 읽지 못해 기본값으로 되돌린다: {ex.Message}");
            return _defaultValue;
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
    /// <summary>포터블 표식 파일 — exe 옆에 이 파일이 있으면 포터블로 동작한다.</summary>
    public const string PortableMarkerFileName = "portable.txt";

    private readonly record struct DataLocation(string Root, string DataPath, bool IsPortable);

    /// <summary>
    /// 경로는 처음 요청될 때 1회 결정한다. 정적 생성자를 두지 않는 것은
    /// Settings 의 순수 계산 메서드(ResolveSpecMaxBytes 등)만 쓰는 테스트가
    /// 실제 데이터 폴더를 건드리지 않게 하기 위함이다.
    /// </summary>
    private static readonly Lazy<DataLocation> _location = new(ResolveLocation);

    /// <summary>
    /// 앱 루트 — 포터블은 실행 파일 폴더, 설치본은 %USERPROFILE%\NewSchool.
    /// 산출물(Logs·Exports·Prints·Backups)의 기준 경로다.
    /// </summary>
    public static string RootPath => _location.Value.Root;

    /// <summary>
    /// 사용자 자산(DB·Photos·Files) 폴더 = <see cref="RootPath"/>\Data.
    /// 프로그램 파일과 섞이지 않도록 한 겹 내려 두었다 — 이 폴더만 옮기면 데이터가 통째로 따라온다.
    /// </summary>
    public static string UserDataPath => _location.Value.DataPath;

    /// <summary>
    /// 포터블 모드 여부
    /// </summary>
    public static bool IsPortableMode => _location.Value.IsPortable;

    /// <summary>
    /// 데이터 폴더 경로 (UserDataPath와 동일)
    /// </summary>
    public static string DataDirectory => UserDataPath;

    /// <summary>
    /// 루트·데이터 경로 결정.
    /// </summary>
    private static DataLocation ResolveLocation()
    {
        var exeDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var userRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "NewSchool");

        var portableRoot = FindPortableRoot(exeDir);
        var root = portableRoot ?? userRoot;

        return new DataLocation(root, Path.Combine(root, "Data"), portableRoot != null);
    }

    /// <summary>
    /// 포터블 루트를 찾는다. 포터블이 아니면 null.
    ///
    /// <para>실행 파일 폴더를 먼저 보고, 없으면 <b>그 부모</b>까지 본다. 설치본이 실행 파일을
    /// <c>{app}\bin\</c> 아래에 두기 때문이다 — 부모를 보지 않으면 설치 폴더를 통째로 옮겨도
    /// (<c>{app}\Data\Settings.db</c> 가 있는데) 포터블로 알아보지 못하고, 사용자가 <c>Data\</c> 를
    /// <c>bin\</c> 안으로 밀어 넣어야 해서 <b>데이터가 프로그램 파일 사이에 섞인다</b>
    /// (<see cref="UserDataPath"/> 를 한 겹 내려 둔 이유가 무너진다).</para>
    ///
    /// <para>부모까지 봐도 안전하다 — 표식이 없으면 그냥 지나가고, <c>Program Files</c> 처럼
    /// 쓸 수 없는 자리는 <see cref="IsPortableLayout"/> 의 쓰기 검사에서 걸러진다.
    /// 실행 파일이 루트에 있던 기존 배치는 첫 번째 검사에서 그대로 걸린다.</para>
    /// </summary>
    internal static string? FindPortableRoot(string exeDir)
    {
        if (string.IsNullOrEmpty(exeDir)) return null;

        exeDir = exeDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (IsPortableLayout(exeDir)) return exeDir;

        try
        {
            var parent = Directory.GetParent(exeDir)?.FullName;
            if (parent != null && IsPortableLayout(parent)) return parent;
        }
        catch (Exception ex)
        {
            Logging.Log.Warning("Settings", $"상위 폴더의 포터블 판정에 실패했다: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// 포터블 판정 — 표식 파일이 기준이다.
    /// 데이터 유무로 판정하던 옛 방식은 DB 가 사라지거나 동기화가 이름을 바꾸면
    /// 조용히 사용자 폴더 모드로 넘어가 빈 화면을 띄웠다. 모드와 데이터 상태를 떼어 놓는다.
    /// 표식이 없어도 옛 포터블 배치(exe 옆 Settings.db)면 포터블로 보고 표식을 만들어 준다.
    /// </summary>
    internal static bool IsPortableLayout(string exeDir)
    {
        try
        {
            bool marked = File.Exists(Path.Combine(exeDir, PortableMarkerFileName));

            // 표식 도입(1.0) 이전 배치 — 루트 또는 Data 하위 어느 쪽이든 인정
            bool legacy = !marked
                && (File.Exists(Path.Combine(exeDir, "Settings.db"))
                    || File.Exists(Path.Combine(exeDir, "Data", "Settings.db")));

            if (!marked && !legacy) return false;

            // 표식이 있어도 쓸 수 없는 자리(읽기 전용 매체·Program Files)면 사용자 폴더로 물러선다.
            if (!IsWritable(exeDir)) return false;

            if (legacy) TryCreatePortableMarker(exeDir);
            return true;
        }
        catch
        {
            return false; // 판정 불가 — 항상 쓸 수 있는 사용자 폴더가 안전한 쪽
        }
    }

    /// <summary>해당 폴더에 쓰기 가능한지 테스트</summary>
    private static bool IsWritable(string path)
    {
        try
        {
            var testFile = Path.Combine(path, $".newschool_write_test_{Guid.NewGuid():N}");
            File.WriteAllText(testFile, "");
            File.Delete(testFile);
            return true;
        }
        catch { return false; }
    }

    private static void TryCreatePortableMarker(string exeDir)
    {
        try
        {
            File.WriteAllText(
                Path.Combine(exeDir, PortableMarkerFileName),
                "이 파일이 있으면 NewSchool 은 포터블로 동작합니다." + Environment.NewLine +
                "데이터는 이 폴더의 Data 하위에 저장됩니다. 파일을 지우면 사용자 폴더 모드로 바뀝니다." + Environment.NewLine);
        }
        catch (Exception ex)
        {
            Logging.Log.Warning("Settings", $"포터블 표식을 만들지 못했다: {ex.Message}");
        }
    }

    // Scheduler 관련 설정
    public static SettingProperty<string> SchedulerDB { get; private set; } = null!;
    // 스케줄러 초기화 완료 플래그(Scheduler_Inited)는 세우기만 하고 읽는 곳이 없어
    // 지웠다(2026-08-31, Board_Inited 와 같은 이유). 설정 DB 의 옛 행은 그대로 둔다.

    public static SettingProperty<bool> ShowEvents { get; private set; } = null!;
    public static SettingProperty<bool> ShowTasks { get; private set; } = null!;
    public static SettingProperty<double> EventFontSize { get; private set; } = null!;
    public static SettingProperty<double> TaskFontSize { get; private set; } = null!;
    public static SettingProperty<double> DateFontSize { get; private set; } = null!;
    public static SettingProperty<bool> UseGoogle { get; private set; } = null!;
    // 캘린더 ID(GoogleCalendarID)는 읽는 곳도 쓰는 곳도 없어 지웠다(2026-09-04).
    // 로컬 캘린더와 구글 캘린더의 짝은 Calendar 테이블의 GoogleId 가 들고 있다 — 설정에 둘 자리가 아니다.

    // Google OAuth 인증
    // 클라이언트 ID·시크릿은 여기 두지 않는다 — 실제로 쓰이는 값은 SecretsService(내장 secrets.json)
    // 하나뿐이다(GoogleAuthService.ClientId/ClientSecret). 설정에도 같은 이름이 있으면
    // "DB 에 넣으면 되겠지" 하고 넣어도 아무 일도 일어나지 않는다(2026-09-04 제거).
    public static SettingProperty<string> GoogleAccessToken { get; private set; } = null!;
    public static SettingProperty<string> GoogleRefreshToken { get; private set; } = null!;
    public static SettingProperty<string> GoogleTokenExpiry { get; private set; } = null!;
    public static SettingProperty<bool> GoogleAutoSync { get; private set; } = null!;
    public static SettingProperty<int> GoogleSyncIntervalMinutes { get; private set; } = null!;
    public static SettingProperty<string> GoogleLastSyncTime { get; private set; } = null!;

    // School 관련 설정
    public static SettingProperty<string> SchoolDB { get; private set; } = null!;
    // 학교 DB 초기화 완료 플래그(School_Inited/"SchoolDB_Inited")는 세우고 내리기만 할 뿐
    // 아무것도 가로막지 않아 지웠다(2026-09-04, Board_Inited·Scheduler_Inited 와 같은 이유).
    // SchoolDatabase.InitAsync 는 이 값을 보지 않고 늘 CREATE TABLE IF NOT EXISTS 를 건다.
    // (설정 DB 에 남은 옛 행은 아무도 읽지 않으므로 그대로 둔다.)

    public static SettingProperty<string> User { get; private set; } = null!;
    public static SettingProperty<int> WorkYear { get; private set; } = null!;
    public static SettingProperty<string> ProvinceCode { get; private set; } = null!;
    public static SettingProperty<string> SchoolCode { get; private set; } = null!;
    public static SettingProperty<string> SchoolName { get; private set; } = null!;
    public static SettingProperty<string> SchoolAddress { get; private set; } = null!;
    public static SettingProperty<string> ProvinceName { get; private set; } = null!;
    public static SettingProperty<string> NeisApiKey { get; private set; } = null!;
    /// <summary>학생부 유형별 바이트 제한 오버라이드 ("유형=값;..." 형식, 빈 값=코드 기본값). 정책 변경 시 재빌드 없이 수정.</summary>
    public static SettingProperty<string> SpecByteLimits { get; private set; } = null!;
    public static SettingProperty<int> WorkSemester { get; private set; } = null!;
    public static SettingProperty<bool> TopMost { get; private set; } = null!;
    public static SettingProperty<string> UserName { get; private set; } = null!;

    /// <summary>
    /// 글·댓글·자료의 <b>작성자</b>로 남길 이름. 이름을 정해 두지 않았으면 "사용자".
    ///
    /// <para>⚠ <c>Settings.UserName ?? "사용자"</c> 라고 쓰면 안 된다 — 그 폴백은
    /// <b>절대 발동하지 않는다</b>. <see cref="SettingProperty{T}"/> 에는
    /// <c>implicit operator T</c> 가 있어서 <c>??</c> 의 null 검사가 값이 아니라
    /// <b>래퍼 객체</b>를 보는데, 래퍼는 초기화 뒤 결코 null 이 아니기 때문이다.
    /// 설정 화면은 빈 이름도 그대로 저장하므로, 이름을 지우면 그 뒤로 쓰는 글의 작성자가
    /// 빈 문자열로 남았다. 컴파일러도 잡지 못하는 함정이라 화면마다 적지 말고 여기를 쓴다.</para>
    /// </summary>
    public static string AuthorName
    {
        get
        {
            var name = UserName?.Value;
            return string.IsNullOrWhiteSpace(name) ? "사용자" : name.Trim();
        }
    }

    public static SettingProperty<bool> IsNeisEventDownloaded { get; private set; } = null!;

    // school period 설정
    /// <summary>요일별 교시 수 (월~금, "6,7,6,7,7" 형식). 파싱은 <see cref="Models.PeriodCounts.Parse"/>.</summary>
    public static SettingProperty<string> PeriodsPerDay { get; private set; } = null!;
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
    // 게시판 초기화 완료 플래그(Board_Inited/"Board_Init")는 세우기만 하고 읽는 곳이 없어
    // 지웠다(2026-08-31). 초기화는 CREATE TABLE IF NOT EXISTS 라 매번 돌아도 무해하다.
    // (설정 DB 에 남은 옛 행은 아무도 읽지 않으므로 그대로 둔다.)
    // 캐시 사용(EnableCache)·페이지 크기(DefaultPageSize)·언어(Language) 는 설정 화면에 있었지만
    // 저장만 되고 읽는 곳이 한 군데도 없었다(40차). 컨트롤과 함께 걷어냈다.
    // 게시판 목록의 페이지 크기는 목록 화면 자체의 콤보가 맡는다.
    /// <summary>
    /// Windows 시작 시 자동 실행
    /// </summary>
    public static SettingProperty<bool> StartWithWindows { get; private set; } = null!;

    /// <summary>
    /// 자동 백업 활성화
    /// </summary>
    public static SettingProperty<bool> AutoBackup { get; private set; } = null!;

    /// <summary>
    /// 마지막 백업 시간 (ISO8601)
    /// </summary>
    public static SettingProperty<string> LastBackupTime { get; private set; } = null!;
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

        ShowEvents = new SettingProperty<bool>("ShowEvents", true, bool.Parse, b => b.ToString().ToLower());
        ShowTasks = new SettingProperty<bool>("ShowTasks", true, bool.Parse, b => b.ToString().ToLower());
        EventFontSize = new SettingProperty<double>("EventFontSize", 9.0, double.Parse, d => d.ToString());
        TaskFontSize = new SettingProperty<double>("TaskFontSize", 10.0, double.Parse, d => d.ToString());
        DateFontSize = new SettingProperty<double>("DateFontSize", 12.0, double.Parse, d => d.ToString());
        UseGoogle = new SettingProperty<bool>("UseGoogle", false, bool.Parse, b => b.ToString().ToLower());

        // Google OAuth
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
        NeisApiKey = new SettingProperty<string>("NeisApiKey", NewSchool.Services.SecretsService.NeisApiKey, s => s, s => s);
        SpecByteLimits = new SettingProperty<string>("SpecByteLimits", "", s => s, s => s);
        WorkSemester = new SettingProperty<int>("WorkSemester", 0, int.Parse, i => i.ToString());
        TopMost = new SettingProperty<bool>("TopMost", false, bool.Parse, b => b.ToString().ToLower());
        UserName = new SettingProperty<string>("UserName", "", s => s, s => s);
        IsNeisEventDownloaded = new SettingProperty<bool>("IsNeisEventDownloaded", false, bool.Parse, b => b.ToString().ToLower());

        PeriodsPerDay = new SettingProperty<string>("PeriodsPerDay", Models.PeriodCounts.Default.Serialize(), s => s, s => s);
        AssemblyTime = new SettingProperty<TimeSpan>("AssemblyTime", TimeSpan.FromMinutes(10), TimeSpan.Parse, ts => ts.ToString());
        DayStarting = new SettingProperty<TimeSpan>("DayStarting", new TimeSpan(8, 30, 0), TimeSpan.Parse, ts => ts.ToString());
        BreakTime = new SettingProperty<TimeSpan>("BreakTime", TimeSpan.FromMinutes(10), TimeSpan.Parse, ts => ts.ToString());
        OnePeriod = new SettingProperty<TimeSpan>("OnePeriod", TimeSpan.FromMinutes(45), TimeSpan.Parse, ts => ts.ToString());
        LunchTime = new SettingProperty<TimeSpan>("LunchTime", TimeSpan.FromMinutes(50), TimeSpan.Parse, ts => ts.ToString());

        HomeGrade = new SettingProperty<int>("HomeGrade", 0, int.Parse, d => d.ToString());
        HomeRoom = new SettingProperty<int>("HomeRoom", 0, int.Parse, d => d.ToString());
        SchoolDB = new SettingProperty<string>("SchoolDB", "school.db", s => s, s => s);
        Board_DB = new SettingProperty<string>("Board_DB", "board.db", s => s, s => s);


        /// <summary>
        /// Windows 시작 시 자동 실행
        /// </summary>
        StartWithWindows = new SettingProperty<bool>("StartWithWindows", false, bool.Parse, b => b.ToString().ToLower());

        /// <summary>
        /// 자동 백업 활성화
        /// </summary>
        AutoBackup = new SettingProperty<bool>("AutoBackup", false, bool.Parse, b => b.ToString().ToLower());

        /// <summary>
        /// 마지막 백업 시간
        /// </summary>
        LastBackupTime = new SettingProperty<string>("LastBackupTime", string.Empty, s => s, s => s);

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
        ShowEvents.Reload();
        ShowTasks.Reload();
        EventFontSize.Reload();
        TaskFontSize.Reload();
        DateFontSize.Reload();
        UseGoogle.Reload();
        GoogleAccessToken.Reload();
        GoogleRefreshToken.Reload();
        GoogleTokenExpiry.Reload();
        GoogleAutoSync.Reload();
        GoogleSyncIntervalMinutes.Reload();
        GoogleLastSyncTime.Reload();
        SchoolDB.Reload();
        User.Reload();
        WorkYear.Reload();
        ProvinceCode.Reload();
        SchoolCode.Reload();
        SchoolName.Reload();
        SchoolAddress.Reload();
        ProvinceName.Reload();
        NeisApiKey.Reload();
        SpecByteLimits.Reload();
        WorkSemester.Reload();
        TopMost.Reload();
        StartWithWindows.Reload();
        AutoBackup.Reload();
        LastBackupTime.Reload();
        UserName.Reload();
        IsNeisEventDownloaded.Reload();

        PeriodsPerDay.Reload();
        AssemblyTime.Reload();
        DayStarting.Reload();
        BreakTime.Reload();
        OnePeriod.Reload();
        LunchTime.Reload();

        HomeGrade.Reload();
        HomeRoom.Reload();

        Board_DB.Reload();
        AutoBackupIntervalDays.Reload();
        BackupRetentionCount.Reload();
        LogLevel.Reload();

        Theme.Reload();
        WindowWidth.Reload();
        WindowHeight.Reload();


    }

    /// <summary>
    /// 학생부 영역의 최대 바이트. 지침이 학년도마다 바뀌므로 학년도별 오버라이드를 지원한다.
    ///
    /// 저장 형식(<see cref="SpecByteLimits"/>, 세미콜론 구분):
    ///   <c>진로활동=2100</c>        — 학년도 무관(전체 적용). 구버전 설정이 이 형태다.
    ///   <c>2026:진로활동=2100</c>   — 2026학년도에만 적용
    ///
    /// 우선순위: <b>해당 학년도 지정값 → 학년도 무관 값 → 코드 기본값</b>.
    /// 덕분에 새 지침을 올해 학년도에만 적용해도 지난 학년도 기록이 갑자기 "초과"로 바뀌지 않는다.
    /// </summary>
    /// <param name="year">학년도. 0이면 학년도 무관 값과 코드 기본값만 본다.</param>
    public static int GetSpecMaxBytes(string type, int year = 0)
        => ResolveSpecMaxBytes(SpecByteLimits?.Value ?? "", type, year);

    /// <summary>
    /// <see cref="GetSpecMaxBytes"/> 의 순수 함수 부분 — 설정 문자열만 받아 유효 한도를 계산한다.
    /// (저장소와 분리해 두어야 학년도 우선순위 규칙을 테스트할 수 있다.)
    /// </summary>
    internal static int ResolveSpecMaxBytes(string raw, string type, int year)
    {
        int? global = null;
        foreach (var (y, t, v) in ParseSpecByteLimits(raw))
        {
            if (t != type) continue;
            if (year > 0 && y == year) return v;   // 해당 학년도 지정값이 최우선
            if (y == 0) global = v;                // 학년도 무관 값은 후보로 보관
        }
        return global ?? Helpers.NeisHelper.GetMaxBytes(type);
    }

    /// <summary>저장된 오버라이드를 (학년도, 영역, 바이트) 로 파싱. 학년도 없으면 0.</summary>
    private static IEnumerable<(int Year, string Type, int Value)> ParseSpecByteLimits(string raw)
    {
        foreach (var pair in (raw ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=');
            if (eq <= 0) continue;
            if (!int.TryParse(pair[(eq + 1)..], out int v) || v <= 0) continue;

            var key = pair[..eq];
            int colon = key.IndexOf(':');
            if (colon > 0 && int.TryParse(key[..colon], out int y))
                yield return (y, key[(colon + 1)..], v);
            else
                yield return (0, key, v);
        }
    }

    /// <summary>
    /// 학생부 영역의 바이트 제한 오버라이드 설정.
    /// 코드 기본값과 같으면 항목을 지워 설정을 깔끔하게 유지한다.
    /// </summary>
    /// <param name="year">0이면 학년도 무관으로 저장, 그 외에는 해당 학년도에만 적용.</param>
    public static void SetSpecByteOverride(string type, int value, int year = 0)
        => SpecByteLimits?.Set(ApplySpecByteOverride(SpecByteLimits.Value ?? "", type, value, year));

    /// <summary><see cref="SetSpecByteOverride"/> 의 순수 함수 부분 — 새 설정 문자열을 만들어 반환.</summary>
    internal static string ApplySpecByteOverride(string raw, string type, int value, int year)
    {
        // 원래 순서를 보존하면서 대상 항목만 교체/삭제
        var entries = ParseSpecByteLimits(raw)
            .Where(e => !(e.Year == year && e.Type == type))
            .ToList();

        // 이 항목을 지웠을 때 실제로 적용될 값(= 폴백).
        // 학년도 지정값은 "학년도 무관 오버라이드 → 코드 기본값" 순으로 떨어지므로,
        // 기본값과 같다는 이유만으로 지우면 학년도 무관 값이 되살아나 의도가 뒤집힌다.
        int fallback = Helpers.NeisHelper.GetMaxBytes(type);
        if (year > 0)
        {
            foreach (var e in entries)
                if (e.Year == 0 && e.Type == type) { fallback = e.Value; break; }
        }

        if (value != fallback)
            entries.Add((year, type, value));

        return string.Join(';', entries.Select(
            e => e.Year > 0 ? $"{e.Year}:{e.Type}={e.Value}" : $"{e.Type}={e.Value}"));
    }

    /// <summary>
    /// 기본값으로 리셋.
    ///
    /// <para>설정 DB 밖에 사는 값이 하나 있다 — Windows 자동 시작은 레지스트리
    /// (<c>HKCU\...\Run</c>)에 등록된다. 그것까지 지우지 않으면 "모든 설정을 기본값으로" 라고
    /// 해 놓고 부팅 때마다 계속 뜬다. DB 를 비우기 <b>전에</b> 해제해서, 해제가 쓰는
    /// <c>StartWithWindows=false</c> 행까지 함께 지워지게 한다.</para>
    ///
    /// <para>테마·항상 위·로그 레벨처럼 <b>지금 열려 있는 창에 이미 걸려 있는</b> 설정은
    /// 값만 되돌려서는 화면이 따라오지 않는다. 그 재적용은 부르는 쪽(설정 화면)이 한다.</para>
    /// </summary>
    public static void ResetToDefaults()
    {
        SetStartWithWindows(false);
        SettingsDb.ResetToDefaults();
        LoadAll();
    }

    /// <summary>
    /// 백업·복원 대상 DB 파일명. 앱이 만드는 것만 골라 담아 동기화 충돌본·수동 .bak 을 배제한다.
    /// (사진·첨부는 용량이 커서 백업에 넣지 않는다 — Data 폴더째 복사가 그 역할이다.)
    /// </summary>
    private static string[] BackupDbFileNames() => new[]
    {
        "Settings.db",
        SchoolDB?.Value ?? "school.db",
        SchedulerDB?.Value ?? "scheduler.db",
        Board_DB?.Value ?? "board.db",
    };

    /// <summary>
    /// 전체 데이터 백업 (Settings.db + 모든 DB) → 단일 ZIP 파일.
    /// 각 DB는 VACUUM INTO 로 스냅샷 — 다른 연결이 쓰는 중이어도 원자적이고,
    /// 빈 공간이 제거돼 파일도 작아진다. ZIP 압축(텍스트 위주 DB라 압축률 높음)까지 하면
    /// 기존 폴더 복사 대비 용량이 크게 줄어든다.
    /// </summary>
    public static string? Backup()
    {
        var staging = Path.Combine(Path.GetTempPath(), $"newschool_backup_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(staging);

            // 앱이 관리하는 DB 만 담는다. "*.db 전부" 로 훑던 옛 방식은 동기화 충돌본
            // (school-이름.db 따위)까지 백업에 넣고 복원 때 되살렸다.
            foreach (var name in BackupDbFileNames())
            {
                var dbFile = Path.Combine(UserDataPath, name);
                if (!File.Exists(dbFile)) continue;

                var target = Path.Combine(staging, name);
                if (!TrySnapshotDb(dbFile, target))
                {
                    // VACUUM INTO 실패 시 폴백: 체크포인트 후 파일 복사
                    CheckpointWal(dbFile);
                    File.Copy(dbFile, target, true);
                }
            }

            var backupsRoot = BackupDirectory;
            Directory.CreateDirectory(backupsRoot);
            var zipPath = ReserveBackupPath(backupsRoot);
            System.IO.Compression.ZipFile.CreateFromDirectory(
                staging, zipPath, System.IO.Compression.CompressionLevel.Optimal, includeBaseDirectory: false);

            LastBackupTime.Set(DateTime.Now.ToString("o"));
            CleanupOldBackups();

            System.Diagnostics.Debug.WriteLine($"[Settings] 백업 완료: {zipPath}");
            return zipPath;
        }
        catch (Exception ex)
        {
            // ⚠ Debug.WriteLine 만 남기면 안 된다 — [Conditional("DEBUG")] 라 배포본에서는
            //    통째로 사라진다. 자동 백업은 백그라운드에서 조용히 도는데, 실패가 어디에도
            //    남지 않으면 사용자는 몇 달이고 "백업되고 있다" 고 믿는다.
            Logging.Log.Error("Settings", "백업 실패", ex);
            return null;
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { /* 임시 폴더 정리 실패 무시 */ }
        }
    }

    /// <summary>
    /// 아직 쓰이지 않은 백업 ZIP 경로를 고른다.
    ///
    /// <para>파일명이 초 단위라 같은 초에 백업이 두 번 시작되면 뒤엣것이
    /// <c>ZipFile.CreateFromDirectory</c> 에서 터져 "백업 중 오류" 로 끝난다. 버튼은 잠갔지만
    /// 앱 시작 시 자동 백업은 백그라운드에서 따로 돌기 때문에 사용자의 수동 백업과 겹칠 수 있다.
    /// 겹치면 <c>_2</c>, <c>_3</c> … 을 붙인다 — 접두사가 그대로라 <see cref="CleanupOldBackups"/>
    /// 의 문자열 내림차순 정렬(= 최신순)도 그대로 성립한다.</para>
    /// </summary>
    private static string ReserveBackupPath(string backupsRoot)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var path = Path.Combine(backupsRoot, $"backup_{stamp}.zip");

        for (int suffix = 2; File.Exists(path) && suffix <= 99; suffix++)
            path = Path.Combine(backupsRoot, $"backup_{stamp}_{suffix}.zip");

        return path;
    }

    /// <summary>
    /// VACUUM INTO 로 DB 원자적 스냅샷 생성. WAL 내용 포함 + 빈 공간 제거.
    /// </summary>
    private static bool TrySnapshotDb(string dbPath, string targetPath)
    {
        try
        {
            if (File.Exists(targetPath)) File.Delete(targetPath);  // VACUUM INTO 는 대상이 있으면 실패

            using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;Pooling=False");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "VACUUM INTO @Target;";
            cmd.Parameters.AddWithValue("@Target", targetPath);
            cmd.ExecuteNonQuery();
            return true;
        }
        catch (Exception ex)
        {
            Logging.Log.Error("Settings", $"백업 스냅샷을 뜨지 못했다: {Path.GetFileName(dbPath)}", ex);
            return false;
        }
    }

    /// <summary>
    /// WAL 체크포인트 — -wal 파일의 커밋을 본 DB 파일로 합침 (백업 직전 호출)
    /// </summary>
    internal static void CheckpointWal(string dbPath)
    {
        try
        {
            using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadWrite;Pooling=False");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            // 체크포인트 실패해도 백업 자체는 진행 (본 파일까지의 데이터는 보존됨)
            System.Diagnostics.Debug.WriteLine($"[Settings] WAL 체크포인트 실패({Path.GetFileName(dbPath)}): {ex.Message}");
        }
    }

    /// <summary>
    /// 복원 대상 DB의 연결 풀과 잔여 WAL 파일 정리 — 복원 파일이 이전 -wal/-shm 과 섞여 오염되는 것 방지
    /// </summary>
    /// <summary>
    /// DB 파일을 덮어쓰기 전 준비 — 풀에 남은 연결을 끊고 잔여 WAL/SHM 을 지운다.
    /// (같은 파일의 <c>SettingsDb.Restore</c> 도 쓴다.)
    /// </summary>
    internal static void PrepareForRestore(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            var sidecar = dbPath + suffix;
            try
            {
                if (File.Exists(sidecar)) File.Delete(sidecar);
            }
            catch (Exception ex)
            {
                Logging.Log.Warning("Settings", $"복원 전 WAL 파일을 정리하지 못했다: {Path.GetFileName(sidecar)} — {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 이 폴더에 <b>되돌릴 것이 하나라도</b> 있는가(= 아는 DB 이름이 하나라도 있는가).
    /// 복원이 아무것도 하지 않고 "완료" 로 끝나는 것을 막는 문지기다.
    /// (파일 검사뿐이라 데이터 폴더를 건드리지 않는다 — 그래서 테스트가 직접 부른다.)
    /// </summary>
    internal static bool ContainsRestorableDb(string dir)
        => BackupDbFileNames().Any(name => File.Exists(Path.Combine(dir, name)));

    /// <summary>
    /// 백업에서 전체 복원 — ZIP 파일(신규), 백업 폴더(구버전), 단일 .db(하위호환) 모두 지원
    /// </summary>
    public static bool Restore(string backupDirOrFile)
    {
        try
        {
            // ZIP 백업이면 임시 폴더에 풀어서 폴더 복원 경로 재사용
            if (File.Exists(backupDirOrFile) &&
                backupDirOrFile.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                var extractDir = Path.Combine(Path.GetTempPath(), $"newschool_restore_{Guid.NewGuid():N}");
                try
                {
                    System.IO.Compression.ZipFile.ExtractToDirectory(backupDirOrFile, extractDir);
                    return Restore(extractDir);
                }
                finally
                {
                    try { if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true); }
                    catch { /* 임시 폴더 정리 실패 무시 */ }
                }
            }

            // 단일 파일이면 기존 Settings.db 복원 (하위호환)
            //
            // ⚠ 고른 파일이 정말 설정 DB 인지 먼저 본다. 예전에는 이름이 .db 로 끝나기만 하면
            //   그대로 Settings.db 위에 복사했다. 파일 선택기가 .db 를 열어 주고,
            //   [데이터 폴더 열기] 로 보게 되는 폴더에 school.db·board.db·scheduler.db 가
            //   나란히 있어서, 하나만 잘못 골라도 설정이 통째로 사라졌다 — 되돌릴 사본도 없고,
            //   뒤이은 CREATE TABLE IF NOT EXISTS 가 성공시켜 줘서 "복원 완료" 로 끝났다.
            //   (.db 비교는 .zip 과 마찬가지로 대소문자를 가리지 않는다.)
            if (File.Exists(backupDirOrFile) &&
                backupDirOrFile.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
            {
                if (!SettingsDb.LooksLikeSettingsDb(backupDirOrFile))
                {
                    Logging.Log.Error("Settings",
                        $"설정 DB 가 아닌 파일이라 복원하지 않는다: {Path.GetFileName(backupDirOrFile)}");
                    return false;
                }

                bool success = SettingsDb.Restore(backupDirOrFile);
                if (success) LoadAll();
                return success;
            }

            // 폴더 복원
            if (!Directory.Exists(backupDirOrFile)) return false;

            // 되돌릴 것이 하나라도 있는지 <b>먼저</b> 본다. 예전에는 아는 DB 이름이 하나도
            // 없어도 루프를 통째로 건너뛰고 "복원 완료" 로 끝났다 — 엉뚱한 ZIP, 손으로 다시
            // 압축해 폴더가 한 겹 생긴 백업, 빈 폴더가 전부 성공으로 보고됐다. 손상 복구
            // 흐름은 그 말을 믿고 재시작하는데 DB 는 여전히 손상이라, 같은 대화상자가
            // 끝없이 다시 떴다. 여기서 걸러야 아무것도 건드리지 않은 채로 실패를 알린다.
            if (!ContainsRestorableDb(backupDirOrFile))
            {
                Logging.Log.Error("Settings",
                    $"백업에서 되돌릴 DB 를 찾지 못했다: {backupDirOrFile}");
                return false;
            }

            Directory.CreateDirectory(UserDataPath);

            // 되돌릴 자리를 먼저 마련한다. DB 를 한 개씩 덮어쓰다 중간에 실패하면
            // (파일 잠금·디스크 부족 등) 학생은 백업 시점, 게시판은 현재 시점처럼
            // DB 마다 시점이 어긋난 채 남는다. 그 상태로 "복원 실패" 라고만 알리면
            // 사용자는 아무것도 안 바뀐 줄 안다.
            var rollbackDir = Path.Combine(Path.GetTempPath(), $"newschool_rollback_{Guid.NewGuid():N}");
            var replaced = new List<string>();
            bool committed = false;

            try
            {
                Directory.CreateDirectory(rollbackDir);

                // 백업과 같은 화이트리스트로 되돌린다 — 옛 백업에 섞여 든 충돌본을 되살리지 않는다.
                bool allRestored = true;
                foreach (var fileName in BackupDbFileNames())
                {
                    var dbFile = Path.Combine(backupDirOrFile, fileName);
                    if (!File.Exists(dbFile)) continue;

                    // 네 파일 모두 UserDataPath 아래에 있다(SettingsDb 도 같은 폴더를 쓴다).
                    // 덮어쓰기 전에 현재 것을 옆에 떠 둔다.
                    var targetPath = Path.Combine(UserDataPath, fileName);
                    PrepareForRestore(targetPath);

                    if (File.Exists(targetPath))
                    {
                        File.Copy(targetPath, Path.Combine(rollbackDir, fileName), true);
                        replaced.Add(fileName);
                    }

                    if (fileName.Equals("Settings.db", StringComparison.OrdinalIgnoreCase))
                    {
                        // 설정 DB 는 덮어쓴 뒤 다시 읽어야 하므로 전용 경로를 쓴다.
                        // 결과를 버리면 설정만 옛 상태로 남은 반쪽 복원을 "복원 완료" 로 알리게 된다.
                        if (!SettingsDb.Restore(dbFile))
                        {
                            allRestored = false;
                            System.Diagnostics.Debug.WriteLine("[Settings] Settings.db 복원 실패");
                        }
                    }
                    else
                    {
                        File.Copy(dbFile, targetPath, true);
                    }
                }

                committed = true;
                LoadAll();
                return allRestored;
            }
            finally
            {
                // 되돌리기까지 실패하면 사본을 지우지 않고 남긴다 — 손으로 살릴 수 있도록.
                bool keepRollbackCopies = false;

                if (!committed)
                {
                    // 여기까지 바꾼 DB 만 되돌린다.
                    foreach (var fileName in replaced)
                    {
                        try
                        {
                            var savedCopy = Path.Combine(rollbackDir, fileName);
                            if (fileName.Equals("Settings.db", StringComparison.OrdinalIgnoreCase))
                            {
                                SettingsDb.Restore(savedCopy);   // 덮어쓴 뒤 다시 읽어야 한다
                            }
                            else
                            {
                                var targetPath = Path.Combine(UserDataPath, fileName);
                                PrepareForRestore(targetPath);
                                File.Copy(savedCopy, targetPath, true);
                            }
                        }
                        catch (Exception rex)
                        {
                            System.Diagnostics.Debug.WriteLine(
                                $"[Settings] 복원 롤백 실패({fileName}): {rex.Message} — 원본 사본: {rollbackDir}");
                            Logging.Log.Error("Settings",
                                $"복원 롤백 실패({fileName}). 원본 사본 위치: {rollbackDir}");
                            keepRollbackCopies = true;
                            break;
                        }
                    }
                }

                if (!keepRollbackCopies)
                {
                    try { if (Directory.Exists(rollbackDir)) Directory.Delete(rollbackDir, true); }
                    catch { /* 임시 폴더 정리 실패 무시 */ }
                }
            }
        }
        catch (Exception ex)
        {
            Logging.Log.Error("Settings", "백업 복원에 실패했다", ex);
            return false;
        }
    }

    /// <summary>
    /// 자동 백업 필요 여부 확인 및 실행
    /// </summary>
    public static string? RunAutoBackupIfNeeded()
    {
        if (!AutoBackup.Value) return null;

        var lastBackup = LastBackupTime.Value;
        if (!string.IsNullOrEmpty(lastBackup) && DateTime.TryParse(lastBackup, out var lastTime))
        {
            var interval = TimeSpan.FromDays(AutoBackupIntervalDays.Value);
            if (DateTime.Now - lastTime < interval) return null;
        }

        return Backup();
    }

    /// <summary>
    /// 오래된 백업 정리 (BackupRetentionCount 초과분 삭제)
    /// </summary>
    private static void CleanupOldBackups()
    {
        try
        {
            var backupsRoot = BackupDirectory;
            if (!Directory.Exists(backupsRoot)) return;

            // ZIP(신규)과 폴더(구버전) 백업을 함께 정렬 — 이름이 backup_yyyyMMdd_HHmmss 라 문자열 내림차순 = 최신순
            var backups = Directory.GetFiles(backupsRoot, "backup_*.zip")
                .Concat(Directory.GetDirectories(backupsRoot, "backup_*"))
                .OrderByDescending(Path.GetFileNameWithoutExtension)
                .ToArray();

            var retainCount = BackupRetentionCount.Value;
            for (int i = retainCount; i < backups.Length; i++)
            {
                if (File.Exists(backups[i])) File.Delete(backups[i]);
                else Directory.Delete(backups[i], true);
                System.Diagnostics.Debug.WriteLine($"[Settings] 오래된 백업 삭제: {backups[i]}");
            }
        }
        catch (Exception ex)
        {
            // 지우지 못하면 보존 개수(기본 20)를 넘겨 백업이 계속 쌓인다 — 도움말이 약속한 수치와
            // 어긋나는데 화면에는 아무 표시도 없다.
            Logging.Log.Warning("Settings", $"오래된 백업을 정리하지 못했다: {ex.Message}");
        }
    }

    /// <summary>
    /// 백업 폴더 경로. Data 폴더 <b>밖</b>에 둔다 — 안에 두면 백업이 자기 자신을 담고,
    /// Data 폴더째 복사할 때 백업 사본까지 딸려와 용량이 배로 불어난다.
    /// </summary>
    public static string BackupDirectory => Path.Combine(RootPath, "Backups");

    #region Windows 자동 시작

    private const string StartupRegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppRegistryName = "NewSchool";

    /// <summary>
    /// Windows 시작 시 자동 실행 설정/해제.
    ///
    /// <para>⚠ 예전에는 <c>void</c> 였고 실패를 <c>Debug.WriteLine</c> 으로만 흘렸다. 레지스트리를
    /// 못 열거나(정책·권한) 쓰지 못하면 <b>토글은 켜진 채, 설정 DB 는 옛 값, 레지스트리는 빈
    /// 상태</b>로 갈라졌고, 사용자는 다음 부팅에 앱이 안 뜨는 것으로만 알 수 있었다.
    /// 설정이 DB 밖(레지스트리)에 사는 자리라 더 잘 어긋난다(47차).</para>
    /// </summary>
    /// <returns>레지스트리와 설정 DB 에 모두 반영되면 true.</returns>
    public static bool SetStartWithWindows(bool enable)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(StartupRegistryKey, true);
            if (key == null)
            {
                Logging.Log.Error("Settings", $"자동 시작 레지스트리 키를 열지 못했다: HKCU\\{StartupRegistryKey}");
                return false;
            }

            if (enable)
            {
                var exePath = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath))
                {
                    // 경로를 모르면 등록해 봐야 아무것도 실행되지 않는다.
                    Logging.Log.Error("Settings", "실행 파일 경로를 알 수 없어 자동 시작을 등록하지 못했다");
                    return false;
                }

                key.SetValue(AppRegistryName, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(AppRegistryName, false);
            }

            StartWithWindows.Set(enable);  // Value 만 바꾸면 DB에 저장되지 않아 레지스트리와 어긋남
            System.Diagnostics.Debug.WriteLine($"[Settings] StartWithWindows = {enable}");
            return true;
        }
        catch (Exception ex)
        {
            Logging.Log.Error("Settings", $"자동 시작 설정 실패(enable={enable})", ex);
            return false;
        }
    }

    /// <summary>
    /// 현재 레지스트리에 자동 시작이 등록되어 있는지 확인
    /// </summary>
    public static bool IsStartWithWindowsRegistered()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(StartupRegistryKey);
            return key?.GetValue(AppRegistryName) != null;
        }
        catch (Exception ex)
        {
            // 읽지 못하면 "등록 안 됨" 으로 보이고 토글이 꺼진 채 뜬다 — 왜 그런지는 남겨야 한다.
            Logging.Log.Warning("Settings", $"자동 시작 등록 여부를 읽지 못했다: {ex.Message}");
            return false;
        }
    }

    #endregion
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
        var dataDir = Settings.UserDataPath;

        // ⚠ 정적 생성자에서 예외를 내보내면 안 된다. 한 번 실패한 타입은 그 프로세스에서
        //    영영 못 쓰고(TypeInitializationException 이 계속 다시 난다), 시작 경로라
        //    창도 뜨기 전이라 사용자에게는 "아이콘을 눌러도 아무 일이 없다" 로만 보인다.
        //    여기서는 삼키고, 실제로 DB 를 여는 Initialize 에서 예외가 나게 둔다
        //    — 그 자리는 App 이 잡아 이유를 안내할 수 있다.
        try
        {
            Directory.CreateDirectory(dataDir);
        }
        catch (Exception ex)
        {
            Logging.Log.Error("SettingsDb", $"데이터 폴더를 만들지 못했다: {dataDir}", ex);
        }

        DbPath = Path.Combine(dataDir, "Settings.db");
        // Cache=Shared 를 떼어 다른 DB 연결들과 기준을 맞췄다 — 공유 캐시는 WAL 위에서
        // 테이블 락을 만들 뿐 이 앱에 이득이 없다(인메모리 DB 를 쓰지 않는다).
        ConnectionString = $"Data Source={DbPath}";
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

    // 설정 DB 만 따로 뜨던 Backup() 은 호출처가 0건이라 지웠다(2026-09-04).
    // Data 폴더 안에 appsettings_backup_*.db 를 쌓는 옛 방식이라, 정리 정책도 복원 경로도 없고
    // 지금 백업 규칙(Backups\ + BackupDbFileNames 화이트리스트)과도 어긋났다.
    // 설정 DB 는 Settings.Backup 이 다른 DB 와 함께 담는다.

    /// <summary>
    /// 이 파일이 <b>설정 DB</b> 인가 — Settings 테이블이 있는 SQLite 파일인지 본다.
    ///
    /// <para>덮어쓰기 전에 물어야 하는 질문이다. 데이터 폴더에는 school.db·board.db·
    /// scheduler.db 가 나란히 있고, 그중 하나를 골라도 <c>CREATE TABLE IF NOT EXISTS</c> 가
    /// 뒤에서 성공시켜 주기 때문에 "복원 완료" 로 끝나 버렸다. 읽기 전용으로만 열어 본다.</para>
    /// </summary>
    internal static bool LooksLikeSettingsDb(string dbPath)
    {
        try
        {
            using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;Pooling=False");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name='Settings' LIMIT 1;";
            return cmd.ExecuteScalar() != null;
        }
        catch (Exception ex)
        {
            // SQLite 가 아니거나 열 수 없는 파일 — 복원 대상이 아니다.
            System.Diagnostics.Debug.WriteLine($"[SettingsDb] 설정 DB 판정 실패({Path.GetFileName(dbPath)}): {ex.Message}");
            return false;
        }
    }

    public static bool Restore(string backupPath)
    {
        lock (_lock)
        {
            if (!File.Exists(backupPath)) return false;

            // 덮어쓰기 전에 현재 것을 옆에 떠 둔다. 폴더 복원에는 되돌릴 자리가 있는데
            // 이 경로에만 없어서, 복사가 도중에 깨지면 설정을 살릴 방법이 없었다.
            var undoPath = Path.Combine(Path.GetTempPath(), $"newschool_settings_undo_{Guid.NewGuid():N}.db");
            bool undoSaved = false;
            bool keepUndoCopy = false;   // 되돌리기까지 실패하면 사본을 남긴다 — 손으로 살릴 수 있도록

            try
            {
                // 이 연결 문자열은 Pooling 기본값(켜짐)이다. 풀에 남은 연결이
                // 파일을 쥔 채로 덮어쓰면 복사가 막히거나, 성공해도 옛 -wal/-shm 이 남아
                // 새 파일과 짝이 안 맞는 상태가 된다. 폴더 복원 경로와 같은 준비를 거친다.
                Settings.PrepareForRestore(DbPath);

                if (File.Exists(DbPath))
                {
                    File.Copy(DbPath, undoPath, true);
                    undoSaved = true;
                }

                File.Copy(backupPath, DbPath, true);
                _isInitialized = false;
                Initialize();
                return true;
            }
            catch (Exception ex)
            {
                Logging.Log.Error("SettingsDb", "설정 DB 복원 실패", ex);

                if (undoSaved) keepUndoCopy = !TryUndo(undoPath);
                return false;
            }
            finally
            {
                if (!keepUndoCopy)
                {
                    try { if (File.Exists(undoPath)) File.Delete(undoPath); }
                    catch { /* 임시 사본 정리 실패 무시 */ }
                }
            }
        }
    }

    /// <summary>복원이 깨졌을 때 떠 둔 사본으로 되돌린다. 되돌렸으면 true.</summary>
    private static bool TryUndo(string undoPath)
    {
        try
        {
            Settings.PrepareForRestore(DbPath);
            File.Copy(undoPath, DbPath, true);
            _isInitialized = false;
            Initialize();
            return true;
        }
        catch (Exception ex)
        {
            // 사본을 지우지 않고 위치를 남긴다.
            Logging.Log.Error("SettingsDb", $"설정 DB 되돌리기 실패 — 사본: {undoPath}", ex);
            return false;
        }
    }

    // 디버그 덤프(PrintAllSettings)와 그 래퍼 Settings.PrintAll 은 호출부가 없어 지웠다(39차).
}


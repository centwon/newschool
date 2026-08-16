using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace NewSchool.Logging
{
    /// <summary>
    /// 파일 로거 - 비동기 로그 기록 (Native AOT 호환)
    /// </summary>
    public sealed class FileLogger : IDisposable
    {
        private static readonly Lazy<FileLogger> _instance = new(() => new FileLogger());
        public static FileLogger Instance => _instance.Value;

        private readonly ConcurrentQueue<LogEntry> _logQueue;
        private readonly SemaphoreSlim _signal;
        private readonly CancellationTokenSource _cts;
        private readonly Task _writerTask;
        private volatile bool _disposed;

        // 백그라운드 라이터와 동기 Flush()가 같은 파일에 동시에 쓰는 것을 막는다.
        private readonly object _fileLock = new();

        private string LogDirectory { get; }
        private LogLevel MinimumLevel { get; set; }
        private DateTime _lastCleanupTime = DateTime.MinValue;

        private FileLogger()
        {
            // 사용자 데이터 폴더에 로그 저장
            LogDirectory = Path.Combine(Settings.RootPath, "Logs");

            if (!Directory.Exists(LogDirectory))
            {
                Directory.CreateDirectory(LogDirectory);
            }

            _logQueue = new ConcurrentQueue<LogEntry>();
            _signal = new SemaphoreSlim(0);
            _cts = new CancellationTokenSource();
            MinimumLevel = LogLevel.Info;

            // 백그라운드 로그 기록 작업 시작
            _writerTask = Task.Run(ProcessLogQueueAsync);
        }

        #region Logging Methods

        public void Debug(string message, Exception? ex = null)
        {
            Log(LogLevel.Debug, message, ex);
        }

        public void Info(string message, Exception? ex = null)
        {
            Log(LogLevel.Info, message, ex);
        }

        public void Warning(string message, Exception? ex = null)
        {
            Log(LogLevel.Warning, message, ex);
        }

        public void Error(string message, Exception? ex = null)
        {
            Log(LogLevel.Error, message, ex);
        }

        public void Critical(string message, Exception? ex = null)
        {
            Log(LogLevel.Critical, message, ex);
        }

        private void Log(LogLevel level, string message, Exception? ex)
        {
            if (level < MinimumLevel)
                return;

            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Message = message,
                Exception = ex
            };

            _logQueue.Enqueue(entry);

            // Dispose 이후(종료 중)에는 라이터가 이미 멈췄고 _signal 도 해제됐다.
            // 큐에만 넣고 끝내면 유실되므로 즉시 동기 기록한다.
            if (_disposed)
            {
                FlushCore();
                return;
            }

            try
            {
                _signal.Release();
            }
            catch (ObjectDisposedException)
            {
                // Dispose 와 경합 — 위와 동일하게 동기 기록으로 처리
                FlushCore();
            }
        }

        /// <summary>
        /// 큐에 남은 로그를 즉시 동기 기록한다.
        /// 앱이 곧 종료될 수 있는 지점(치명적 예외 처리기 등)에서 호출하면
        /// 백그라운드 라이터가 스케줄되지 못해 로그가 유실되는 것을 막을 수 있다.
        /// </summary>
        public void Flush() => FlushCore();

        private void FlushCore()
        {
            var entries = new System.Collections.Generic.List<LogEntry>();
            while (_logQueue.TryDequeue(out var entry))
            {
                entries.Add(entry);
            }

            if (entries.Count > 0)
            {
                WriteEntriesToFile(entries);
            }
        }

        #endregion

        #region Background Writer

        private async Task ProcessLogQueueAsync()
        {
            // 토큰을 한 번만 캡처한다. Dispose 가 대기 시간을 초과해 _cts 를 먼저 해제한 경우
            // 루프 조건에서 _cts.Token 을 다시 읽으면 ObjectDisposedException 이 난다.
            var token = _cts.Token;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    await _signal.WaitAsync(token);

                    var entries = new System.Collections.Generic.List<LogEntry>();
                    while (_logQueue.TryDequeue(out var entry))
                    {
                        entries.Add(entry);
                    }

                    if (entries.Count > 0)
                    {
                        WriteEntriesToFile(entries);
                    }
                }
                catch (OperationCanceledException)
                {
                    // 종료 신호 — 남은 큐를 비우고 나간다(Dispose 가 유실을 막는 핵심 경로).
                    FlushCore();
                    break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"로그 기록 실패: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 실제 파일 기록. 백그라운드 라이터와 Flush() 양쪽에서 호출되므로
        /// _fileLock 으로 직렬화한다(같은 파일 동시 append 방지).
        /// </summary>
        private void WriteEntriesToFile(System.Collections.Generic.List<LogEntry> entries)
        {
            try
            {
                var sb = new StringBuilder();

                foreach (var entry in entries)
                {
                    sb.AppendLine(FormatLogEntry(entry));
                }

                lock (_fileLock)
                {
                    string logFile = GetLogFilePath();
                    File.AppendAllText(logFile, sb.ToString());

                    // 로그 파일 크기 확인 및 회전
                    RotateLogIfNeeded(logFile);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"로그 파일 쓰기 실패: {ex.Message}");
            }
        }

        #endregion

        #region Formatting

        private string FormatLogEntry(LogEntry entry)
        {
            var sb = new StringBuilder();
            sb.Append($"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] ");
            sb.Append($"[{entry.Level}] ");
            sb.Append(entry.Message);

            if (entry.Exception != null)
            {
                sb.AppendLine();
                sb.Append($"  Exception: {entry.Exception.GetType().Name}");
                sb.AppendLine();
                sb.Append($"  Message: {entry.Exception.Message}");
                sb.AppendLine();
                sb.Append($"  StackTrace: {entry.Exception.StackTrace}");
            }

            return sb.ToString();
        }

        #endregion

        #region File Management

        private string GetLogFilePath()
        {
            string fileName = $"log_{DateTime.Now:yyyyMMdd}.txt";
            return Path.Combine(LogDirectory, fileName);
        }

        private void RotateLogIfNeeded(string logFile)
        {
            try
            {
                var fileInfo = new FileInfo(logFile);
                if (fileInfo.Exists && fileInfo.Length > 10 * 1024 * 1024) // 10MB
                {
                    string archiveName = $"log_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                    string archivePath = Path.Combine(LogDirectory, "Archive", archiveName);

                    string? archiveDir = Path.GetDirectoryName(archivePath);
                    if (!string.IsNullOrEmpty(archiveDir) && !Directory.Exists(archiveDir))
                    {
                        Directory.CreateDirectory(archiveDir);
                    }

                    File.Move(logFile, archivePath);
                }

                // 오래된 로그 삭제 (30일 이상) — 플러시마다 디렉터리 스캔은 낭비라 1시간에 1회만
                if (DateTime.Now - _lastCleanupTime > TimeSpan.FromHours(1))
                {
                    _lastCleanupTime = DateTime.Now;
                    CleanupOldLogs();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"로그 회전 실패: {ex.Message}");
            }
        }

        private void CleanupOldLogs()
        {
            try
            {
                var cutoffDate = DateTime.Now.AddDays(-30);

                // Archive 하위까지 훑는다. 회전(RotateLogIfNeeded)은 10MB 마다 로그를
                // Archive 로 옮기는데 정리는 최상위만 보고 있어서, 보관본이 한 번도
                // 지워지지 않고 무한히 쌓였다.
                foreach (var file in Directory.GetFiles(LogDirectory, "log_*.txt", SearchOption.AllDirectories))
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.LastWriteTime < cutoffDate)
                    {
                        File.Delete(file);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"로그 정리 실패: {ex.Message}");
            }
        }

        #endregion

        #region Configuration

        public void SetMinimumLevel(LogLevel level)
        {
            MinimumLevel = level;
        }

        #endregion

        #region Dispose

        public void Dispose()
        {
            if (_disposed) return;

            // 순서가 중요하다. 취소를 먼저 걸면 라이터의 WaitAsync(token) 이 즉시 예외로 빠져
            // 큐가 비워지지 않은 채 종료된다(= 종료 직전 로그 유실).
            // 따라서 ① 남은 큐를 먼저 동기 기록하고 ② 그 다음 라이터를 정지시킨다.
            FlushCore();

            _cts.Cancel();

            try
            {
                if (!_writerTask.IsCompleted)
                    _writerTask.Wait(TimeSpan.FromSeconds(2));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FileLogger] Writer task 종료 대기 실패: {ex.Message}");
            }

            // 라이터 종료 대기 중에 추가로 쌓인 항목까지 마무리
            FlushCore();

            // _disposed 를 먼저 세워야 이후 Log() 호출이 해제된 _signal 을 만지지 않고
            // 동기 기록 경로로 빠진다.
            _disposed = true;

            _cts.Dispose();
            _signal.Dispose();
        }

        #endregion
    }

    #region Models

    internal class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public LogLevel Level { get; set; }
        public string Message { get; set; } = string.Empty;
        public Exception? Exception { get; set; }
    }

    public enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Warning = 2,
        Error = 3,
        Critical = 4
    }

    #endregion

    #region Extension Methods

    public static class LoggerExtensions
    {
        public static void LogOperation(this FileLogger logger, string operation, bool success, TimeSpan elapsed)
        {
            string message = $"{operation} - {(success ? "성공" : "실패")} ({elapsed.TotalMilliseconds:F2}ms)";

            if (success)
                logger.Info(message);
            else
                logger.Error(message);
        }

        public static void LogDatabaseOperation(this FileLogger logger, string operation, int recordsAffected)
        {
            logger.Info($"[DB] {operation} - {recordsAffected}개 레코드 영향받음");
        }

        public static void LogUserAction(this FileLogger logger, string action, string details = "")
        {
            string message = string.IsNullOrEmpty(details)
                ? $"[사용자] {action}"
                : $"[사용자] {action} - {details}";

            logger.Info(message);
        }
    }

    /// <summary>
    /// 서비스/페이지 코드에서 Debug.WriteLine + FileLogger를 함께 사용하기 위한 정적 헬퍼
    /// 기존 Debug.WriteLine 호출을 대체
    /// </summary>
    public static class Log
    {
        /// <summary>
        /// 설정에서 로그 레벨을 Debug 로 내리면 파일에도 남는다 (릴리스 빌드 문제 진단용).
        /// FileLogger 내부에서 MinimumLevel 로 걸러지므로 평상시(Info 이상) 오버헤드는 큐 진입 전 비교 1회뿐.
        /// </summary>
        public static void Debug(string tag, string message)
        {
            System.Diagnostics.Debug.WriteLine($"[{tag}] {message}");
            FileLogger.Instance.Debug($"[{tag}] {message}");
        }

        public static void Info(string tag, string message)
        {
            System.Diagnostics.Debug.WriteLine($"[{tag}] {message}");
            FileLogger.Instance.Info($"[{tag}] {message}");
        }

        public static void Warning(string tag, string message)
        {
            System.Diagnostics.Debug.WriteLine($"[{tag}] WARNING: {message}");
            FileLogger.Instance.Warning($"[{tag}] {message}");
        }

        public static void Error(string tag, string message, Exception? ex = null)
        {
            System.Diagnostics.Debug.WriteLine($"[{tag}] ERROR: {message}");
            if (ex != null)
            {
                System.Diagnostics.Debug.WriteLine($"  Exception: {ex.GetType().Name} - {ex.Message}");
            }
            FileLogger.Instance.Error($"[{tag}] {message}", ex);
        }
    }

    #endregion
}

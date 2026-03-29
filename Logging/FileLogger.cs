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
    public class FileLogger : IDisposable
    {
        private static readonly Lazy<FileLogger> _instance = new(() => new FileLogger());
        public static FileLogger Instance => _instance.Value;

        private readonly ConcurrentQueue<LogEntry> _logQueue;
        private readonly SemaphoreSlim _signal;
        private readonly CancellationTokenSource _cts;
        private readonly Task _writerTask;
        private bool _disposed;

        private string LogDirectory { get; }
        private LogLevel MinimumLevel { get; set; }

        private FileLogger()
        {
            // 사용자 데이터 폴더에 로그 저장
            LogDirectory = Path.Combine(Settings.UserDataPath, "Logs");

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
            _signal.Release();
        }

        #endregion

        #region Background Writer

        private async Task ProcessLogQueueAsync()
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    await _signal.WaitAsync(_cts.Token);

                    var entries = new System.Collections.Generic.List<LogEntry>();
                    while (_logQueue.TryDequeue(out var entry))
                    {
                        entries.Add(entry);
                    }

                    if (entries.Count > 0)
                    {
                        await WriteLogsAsync(entries);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"로그 기록 실패: {ex.Message}");
                }
            }
        }

        private async Task WriteLogsAsync(System.Collections.Generic.List<LogEntry> entries)
        {
            try
            {
                string logFile = GetLogFilePath();
                var sb = new StringBuilder();

                foreach (var entry in entries)
                {
                    sb.AppendLine(FormatLogEntry(entry));
                }

                await File.AppendAllTextAsync(logFile, sb.ToString());

                // 로그 파일 크기 확인 및 회전
                await RotateLogIfNeededAsync(logFile);
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

        private async Task RotateLogIfNeededAsync(string logFile)
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

                    await Task.Run(() => File.Move(logFile, archivePath));
                }

                // 오래된 로그 삭제 (30일 이상)
                CleanupOldLogs();
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
                var files = Directory.GetFiles(LogDirectory, "log_*.txt");

                foreach (var file in files)
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
            if (!_disposed)
            {
                _cts.Cancel();
                _signal.Release(); // 대기 중인 작업 깨우기

                try
                {
                    _writerTask.Wait(TimeSpan.FromSeconds(5));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[FileLogger] Writer task 종료 대기 실패: {ex.Message}");
                }

                _cts.Dispose();
                _signal.Dispose();
                _disposed = true;
            }
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
        [System.Diagnostics.Conditional("DEBUG")]
        public static void Debug(string tag, string message)
        {
            System.Diagnostics.Debug.WriteLine($"[{tag}] {message}");
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

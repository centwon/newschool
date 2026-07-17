using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NewSchool.Board.Services;

namespace NewSchool.Board.Caching
{
    /// <summary>
    /// 캐시 매니저 - 자주 조회되는 데이터 캐싱
    /// </summary>
    public sealed class CacheManager : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cleanupCts.Cancel();
            _cleanupCts.Dispose();
            GC.SuppressFinalize(this);
        }

        private static readonly Lazy<CacheManager> _instance = new(() => new CacheManager());
        public static CacheManager Instance => _instance.Value;

        private readonly Dictionary<string, CacheEntry> _cache;
        private readonly object _lockObject = new();
        private readonly TimeSpan _defaultExpiration = TimeSpan.FromMinutes(5);

        // 메모리 최적화: 캐시 크기 제한 (기본 15MB)
        // 캐시 대상(게시글 목록/본문/댓글)이 5분 만료라 평시 점유가 작고,
        // 상한 초과 시에도 미스는 로컬 SQLite 재조회(ms 단위)라 15MB 로 충분함.
        private readonly long _maxCacheSizeBytes = 15 * 1024 * 1024; // 15MB
        private long _currentCacheSize = 0;
        private readonly CancellationTokenSource _cleanupCts = new();

        private CacheManager()
        {
            _cache = new Dictionary<string, CacheEntry>();

            // 백그라운드 정리 작업 시작
            StartCleanupTask();
        }

        #region Cache Operations

        /// <summary>
        /// 캐시에 데이터 저장
        /// 메모리 제한을 초과하면 오래된 항목부터 제거 (LRU)
        /// </summary>
        public void Set<T>(string key, T value, TimeSpan? expiration = null)
        {
            lock (_lockObject)
            {
                long itemSize = EstimateSize(value);

                // 기존 항목이 있으면 크기 차감
                if (_cache.TryGetValue(key, out var oldEntry))
                {
                    _currentCacheSize -= EstimateSize(oldEntry.Value);
                }

                // 메모리 제한 체크 및 정리
                while (_currentCacheSize + itemSize > _maxCacheSizeBytes && _cache.Count > 0)
                {
                    EvictOldestEntry();
                }

                var entry = new CacheEntry
                {
                    Value = value,
                    ExpiresAt = DateTime.Now.Add(expiration ?? _defaultExpiration),
                    LastAccessTime = DateTime.Now,
                    Size = itemSize
                };

                _cache[key] = entry;
                _currentCacheSize += itemSize;
                Debug.WriteLine($"[Cache] 저장: {key} (크기: {FormatBytes(itemSize)}, 총: {FormatBytes(_currentCacheSize)})");
            }
        }

        /// <summary>
        /// 캐시에서 데이터 조회
        /// LRU: 조회 시 LastAccessTime 업데이트
        /// </summary>
        public T? Get<T>(string key)
        {
            lock (_lockObject)
            {
                if (_cache.TryGetValue(key, out var entry))
                {
                    if (entry.ExpiresAt > DateTime.Now)
                    {
                        entry.LastAccessTime = DateTime.Now; // LRU 갱신
                        Debug.WriteLine($"[Cache] 히트: {key}");
                        return (T?)entry.Value;
                    }
                    else
                    {
                        _currentCacheSize -= entry.Size;
                        _cache.Remove(key);
                        Debug.WriteLine($"[Cache] 만료: {key}");
                    }
                }
                else
                {
                    Debug.WriteLine($"[Cache] 미스: {key}");
                }

                return default;
            }
        }

        /// <summary>
        /// 캐시에서 데이터 조회 시도 (히트/미스를 명확히 구분)
        /// 값 타입의 default(0)나 캐시된 null 을 미스로 오인하지 않도록 bool 반환
        /// </summary>
        public bool TryGet<T>(string key, out T? value)
        {
            lock (_lockObject)
            {
                if (_cache.TryGetValue(key, out var entry))
                {
                    if (entry.ExpiresAt > DateTime.Now)
                    {
                        entry.LastAccessTime = DateTime.Now; // LRU 갱신
                        value = (T?)entry.Value;
                        return true;
                    }

                    // 만료 항목 제거
                    _currentCacheSize -= entry.Size;
                    _cache.Remove(key);
                }

                value = default;
                return false;
            }
        }

        /// <summary>
        /// 캐시에서 데이터 조회 또는 생성
        /// </summary>
        public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null)
        {
            // 먼저 캐시 확인 (TryGet 으로 히트/미스를 정확히 판별)
            if (TryGet<T>(key, out var cached))
            {
                return cached!;
            }

            // 캐시에 없으면 생성
            var value = await factory();
            Set(key, value, expiration);
            return value;
        }

        /// <summary>
        /// 특정 키 삭제
        /// </summary>
        public void Remove(string key)
        {
            lock (_lockObject)
            {
                if (_cache.TryGetValue(key, out var entry))
                {
                    _currentCacheSize -= entry.Size;
                    _cache.Remove(key);
                    Debug.WriteLine($"[Cache] 삭제: {key}");
                }
            }
        }

        /// <summary>
        /// 패턴에 맞는 키 모두 삭제
        /// </summary>
        public void RemoveByPattern(string pattern)
        {
            lock (_lockObject)
            {
                var keysToRemove = new List<string>();

                foreach (var key in _cache.Keys)
                {
                    if (key.Contains(pattern))
                    {
                        keysToRemove.Add(key);
                    }
                }

                foreach (var key in keysToRemove)
                {
                    if (_cache.TryGetValue(key, out var entry))
                    {
                        _currentCacheSize -= entry.Size;
                        _cache.Remove(key);
                    }
                }

                Debug.WriteLine($"[Cache] 패턴 삭제: {pattern} ({keysToRemove.Count}개)");
            }
        }

        /// <summary>
        /// 모든 캐시 삭제
        /// </summary>
        public void Clear()
        {
            lock (_lockObject)
            {
                var count = _cache.Count;
                _cache.Clear();
                _currentCacheSize = 0;
                Debug.WriteLine($"[Cache] 전체 삭제: {count}개");
            }
        }

        /// <summary>
        /// 캐시 통계
        /// </summary>
        public CacheStatistics GetStatistics()
        {
            lock (_lockObject)
            {
                int expired = 0;

                foreach (var entry in _cache.Values)
                {
                    if (entry.ExpiresAt < DateTime.Now)
                    {
                        expired++;
                    }
                }

                return new CacheStatistics
                {
                    TotalEntries = _cache.Count,
                    ActiveEntries = _cache.Count - expired,
                    ExpiredEntries = expired,
                    EstimatedSizeBytes = _currentCacheSize,
                    MaxSizeBytes = _maxCacheSizeBytes
                };
            }
        }

        #endregion

        #region Cleanup

        private void StartCleanupTask()
        {
            var token = _cleanupCts.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        await Task.Delay(TimeSpan.FromMinutes(1), token);
                        CleanupExpired();
                    }
                }
                catch (OperationCanceledException) { /* 정상 종료 */ }
            }, token);
        }

        /// <summary>
        /// 백그라운드 정리 태스크 중지
        /// </summary>
        public void StopCleanup()
        {
            _cleanupCts.Cancel();
        }

        private void CleanupExpired()
        {
            lock (_lockObject)
            {
                var now = DateTime.Now;
                var keysToRemove = new List<string>();

                foreach (var kvp in _cache)
                {
                    if (kvp.Value.ExpiresAt < now)
                    {
                        keysToRemove.Add(kvp.Key);
                    }
                }

                foreach (var key in keysToRemove)
                {
                    if (_cache.TryGetValue(key, out var entry))
                    {
                        _currentCacheSize -= entry.Size;
                        _cache.Remove(key);
                    }
                }

                if (keysToRemove.Count > 0)
                {
                    Debug.WriteLine($"[Cache] 자동 정리: {keysToRemove.Count}개");
                }
            }
        }

        /// <summary>
        /// LRU: 가장 오래 사용되지 않은 항목 제거
        /// </summary>
        private void EvictOldestEntry()
        {
            if (_cache.Count == 0) return;

            // O(n) 최소값 탐색 (OrderBy().First() 대비 성능 개선)
            string? oldestKey = null;
            DateTime oldestTime = DateTime.MaxValue;
            foreach (var kvp in _cache)
            {
                if (kvp.Value.LastAccessTime < oldestTime)
                {
                    oldestTime = kvp.Value.LastAccessTime;
                    oldestKey = kvp.Key;
                }
            }

            if (oldestKey != null && _cache.TryGetValue(oldestKey, out var entry))
            {
                _currentCacheSize -= entry.Size;
                _cache.Remove(oldestKey);
                Debug.WriteLine($"[Cache] LRU 제거: {oldestKey} (크기: {FormatBytes(entry.Size)})");
            }
        }

        #endregion

        #region Helpers

        private string FormatBytes(long bytes)
        {
            if (bytes < 1024)
                return $"{bytes} B";
            if (bytes < 1024 * 1024)
                return $"{bytes / 1024.0:F2} KB";
            return $"{bytes / (1024.0 * 1024.0):F2} MB";
        }

        private long EstimateSize(object? obj)
        {
            if (obj == null) return 0;

            // Post.Content(.flow BLOB)가 수십KB~MB급이라 개당 고정치로는 실제 메모리 사용량을 크게 과소평가함.
            // 실제 바이트 길이를 반영해야 _maxCacheSizeBytes 상한이 의미를 가짐.
            switch (obj)
            {
                case byte[] bytes:
                    return bytes.Length;
                case string str:
                    return str.Length * 2; // Unicode 문자당 2바이트
                case Post post:
                    return EstimatePostSize(post);
                case PagedResult<Post> paged:
                    return paged.Items.Sum(EstimatePostSize) + 200;
                case System.Collections.IEnumerable enumerable:
                    long total = 0;
                    int count = 0;
                    foreach (var item in enumerable)
                    {
                        total += EstimateSize(item);
                        count++;
                        if (count > 1000)
                        {
                            // 너무 큰 컬렉션은 순회 비용을 제한하고 대략치로 보정
                            total += 100;
                            break;
                        }
                    }
                    return Math.Max(total, 1);
                default:
                    return 100; // 기본값 (단순 값 타입 등)
            }
        }

        /// <summary>Post 1건의 실제 메모리 사용량 추정 (Content BLOB 포함)</summary>
        private static long EstimatePostSize(Post post) =>
            100
            + (post.Title?.Length ?? 0) * 2
            + (post.PlainText?.Length ?? 0) * 2
            + (post.Content?.Length ?? 0);

        #endregion

        private class CacheEntry
        {
            public object? Value { get; set; }
            public DateTime ExpiresAt { get; set; }
            public DateTime LastAccessTime { get; set; }
            public long Size { get; set; }
        }
    }

    /// <summary>
    /// 캐시 통계
    /// </summary>
    public class CacheStatistics
    {
        public int TotalEntries { get; set; }
        public int ActiveEntries { get; set; }
        public int ExpiredEntries { get; set; }
        public long EstimatedSizeBytes { get; set; }
        public long MaxSizeBytes { get; set; }

        public string EstimatedSizeFormatted
        {
            get
            {
                if (EstimatedSizeBytes < 1024)
                    return $"{EstimatedSizeBytes} B";
                if (EstimatedSizeBytes < 1024 * 1024)
                    return $"{EstimatedSizeBytes / 1024.0:F2} KB";
                return $"{EstimatedSizeBytes / (1024.0 * 1024.0):F2} MB";
            }
        }

        public string MaxSizeFormatted
        {
            get
            {
                if (MaxSizeBytes < 1024)
                    return $"{MaxSizeBytes} B";
                if (MaxSizeBytes < 1024 * 1024)
                    return $"{MaxSizeBytes / 1024.0:F2} KB";
                return $"{MaxSizeBytes / (1024.0 * 1024.0):F2} MB";
            }
        }

        public double UsagePercentage => MaxSizeBytes > 0
            ? (double)EstimatedSizeBytes / MaxSizeBytes * 100
            : 0;
    }

    /// <summary>
    /// Board 전용 캐시 키 생성기
    /// </summary>
    public static class CacheKeys
    {
        public static string Categories() => "board:categories";
        public static string Subjects(string category) => $"board:subjects:{category}";
        public static string Post(int no) => $"board:post:{no}";
        public static string Posts(int page, int pageSize, string category, string subject, string search)
            => $"board:posts:{page}:{pageSize}:{category}:{subject}:{search}";
        public static string Comments(int postNo) => $"board:comments:{postNo}";
        public static string PostFiles(int postNo) => $"board:files:{postNo}";
        public static string PostCount(string category) => $"board:count:{category}";
    }
}

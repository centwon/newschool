using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace NewSchool.Services
{
    /// <summary>
    /// 학생 사진 관리 서비스
    /// 사진 선택, 저장, 로드, 삭제 처리
    /// </summary>
    public class PhotoService
    {
        private readonly string _baseDirectory;

        // 메모리 최적화: 이미지 캐시 (최근 50개 이미지 캐싱)
        // LRU 판정은 접근 순번(_accessCounter)으로 한다 — Dictionary 는 삭제 후 재삽입 시
        // 순서 보존이 보장되지 않아 Keys.First() 방식은 "가장 오래된 항목"이 아니었음
        private static readonly Dictionary<string, ImageCacheEntry> _imageCache = new();
        private static readonly object _cacheLock = new object();
        private static long _accessCounter;
        private const int MaxCacheSize = 50;

        private sealed class ImageCacheEntry
        {
            public required WeakReference<BitmapImage> Ref { get; init; }
            public long LastAccess { get; set; }
        }

        public PhotoService()
        {
            _baseDirectory = Settings.UserDataPath;
        }

        public PhotoService(string baseDirectory)
        {
            _baseDirectory = baseDirectory;
        }

        #region 사진 선택 및 저장

        /// <summary>
        /// 사진 파일 선택 및 저장
        /// </summary>
        /// <param name="studentId">학생 ID</param>
        /// <returns>저장된 사진의 상대 경로 (실패 시 null)</returns>
        public async Task<string?> PickAndSavePhotoAsync(string studentId)
        {
            if (string.IsNullOrEmpty(studentId))
                return null;

            try
            {
                // FileOpenPicker 생성
                var picker = new FileOpenPicker
                {
                    ViewMode = PickerViewMode.Thumbnail,
                    SuggestedStartLocation = PickerLocationId.PicturesLibrary
                };

                picker.FileTypeFilter.Add(".jpg");
                picker.FileTypeFilter.Add(".jpeg");
                picker.FileTypeFilter.Add(".png");
                picker.FileTypeFilter.Add(".bmp");
                picker.FileTypeFilter.Add(".gif");

                // WinUI 3 Window 핸들 설정
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                // 파일 선택
                var file = await picker.PickSingleFileAsync();
                if (file == null)
                    return null;

                // 저장 경로: Photos/{Year}/{StudentID}.확장자
                return await SavePhotoAsync(file, studentId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PhotoService] PickAndSavePhotoAsync 오류: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 파일을 지정된 경로에 저장
        /// </summary>
        private async Task<string> SavePhotoAsync(StorageFile sourceFile, string studentId)
        {
            // 저장 디렉토리: Photos/{Year}/
            string year = Settings.WorkYear.Value.ToString();
            string photoDir = Path.Combine(_baseDirectory, "Photos", year);

            // 디렉토리 생성
            Directory.CreateDirectory(photoDir);

            // 파일명: {StudentID}.확장자
            string extension = Path.GetExtension(sourceFile.Name);
            string fileName = $"{studentId}{extension}";
            string destPath = Path.Combine(photoDir, fileName);

            // 확장자가 다른 이전 사진 정리 (.jpg → .png 교체 시 고아 파일 방지)
            foreach (var oldExt in new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif" })
            {
                if (string.Equals(oldExt, extension, StringComparison.OrdinalIgnoreCase)) continue;
                string oldPath = Path.Combine(photoDir, $"{studentId}{oldExt}");
                try
                {
                    if (File.Exists(oldPath))
                    {
                        File.Delete(oldPath);
                        InvalidateCache(oldPath);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[PhotoService] 이전 사진 정리 실패: {oldPath} — {ex.Message}");
                }
            }

            // 파일 복사
            var destFolder = await StorageFolder.GetFolderFromPathAsync(photoDir);
            await sourceFile.CopyAsync(destFolder, fileName, NameCollisionOption.ReplaceExisting);

            // 같은 경로에 덮어쓰기이므로 캐시에 남은 이전 이미지를 무효화해야
            // 교체 직후에도 새 사진이 표시된다
            InvalidateCache(destPath);

            // 상대 경로 반환
            return Path.Combine("Photos", year, fileName);
        }

        /// <summary>
        /// 특정 파일 경로의 캐시 항목(모든 디코딩 크기)을 제거.
        /// 캐시 키가 "{경로}:{크기}" 형식이므로 경로 접두어로 일괄 삭제한다.
        /// </summary>
        private static void InvalidateCache(string fullPath)
        {
            lock (_cacheLock)
            {
                string prefix = fullPath + ":";
                var stale = _imageCache.Keys
                    .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                foreach (var key in stale)
                    _imageCache.Remove(key);
            }
        }

        #endregion

        #region 사진 로드

        /// <summary>
        /// 상대 사진 경로를 절대 경로로 변환 (저장 기준인 UserDataPath 기준).
        /// 사진은 "Photos/{연도}/{파일}" 상대 경로로 저장되므로,
        /// 모든 소비자(PhotoCard, 인쇄 등)는 반드시 이 헬퍼로 경로를 풀어야 한다.
        /// (과거 PhotoCard 는 AppContext.BaseDirectory 기준으로 풀어 설치 환경에서 사진 누락이 발생했음)
        /// </summary>
        public static string? ResolveFullPath(string? photoPath)
        {
            if (string.IsNullOrEmpty(photoPath))
                return null;

            return Path.IsPathRooted(photoPath)
                ? photoPath
                : Path.Combine(Settings.UserDataPath, photoPath);
        }

        /// <summary>
        /// 사진 로드 (BitmapImage 반환)
        /// 메모리 최적화: DecodePixelWidth 설정 + WeakReference 캐싱
        /// </summary>
        /// <param name="photoPath">사진 경로 (상대 또는 절대)</param>
        /// <param name="decodePixelWidth">디코딩 너비 (기본값: 400px)</param>
        /// <returns>BitmapImage (실패 시 null)</returns>
        public async Task<BitmapImage?> LoadPhotoAsync(string? photoPath, int decodePixelWidth = 400)
        {
            if (string.IsNullOrEmpty(photoPath))
                return null;

            try
            {
                // 절대 경로 변환
                string fullPath = Path.IsPathRooted(photoPath)
                    ? photoPath
                    : Path.Combine(_baseDirectory, photoPath);

                if (!File.Exists(fullPath))
                {
                    System.Diagnostics.Debug.WriteLine($"[PhotoService] 사진 파일 없음: {fullPath}");
                    return null;
                }

                // 캐시 키 생성 (경로 + 크기)
                string cacheKey = $"{fullPath}:{decodePixelWidth}";

                // 캐시 확인 (WeakReference 사용으로 메모리 압박 시 자동 해제)
                lock (_cacheLock)
                {
                    if (_imageCache.TryGetValue(cacheKey, out var entry))
                    {
                        if (entry.Ref.TryGetTarget(out var cachedImage))
                        {
                            entry.LastAccess = ++_accessCounter; // LRU 갱신
                            return cachedImage;
                        }

                        _imageCache.Remove(cacheKey); // GC 로 회수된 항목 정리
                    }
                }

                // WinUI 3 비동기 이미지 로딩
                var file = await StorageFile.GetFileFromPathAsync(fullPath);
                using var stream = await file.OpenReadAsync();

                var bitmap = new BitmapImage();

                // 메모리 최적화: 표시 크기에 맞게 디코딩
                // 원본 크기가 아닌 지정된 크기로 디코딩하여 메모리 절약
                if (decodePixelWidth > 0)
                {
                    bitmap.DecodePixelWidth = decodePixelWidth;
                    bitmap.DecodePixelType = DecodePixelType.Logical;
                }

                await bitmap.SetSourceAsync(stream);

                // 캐시에 추가 (WeakReference로 저장하여 메모리 압박 시 GC가 회수 가능)
                lock (_cacheLock)
                {
                    // 캐시 크기 제한: GC 로 회수된 항목 우선, 없으면 가장 오래 접근 안 된 항목 제거
                    if (_imageCache.Count >= MaxCacheSize)
                    {
                        string? evictKey = null;
                        long oldest = long.MaxValue;
                        foreach (var kvp in _imageCache)
                        {
                            if (!kvp.Value.Ref.TryGetTarget(out _))
                            {
                                evictKey = kvp.Key; // 이미 죽은 항목이 최우선
                                break;
                            }
                            if (kvp.Value.LastAccess < oldest)
                            {
                                oldest = kvp.Value.LastAccess;
                                evictKey = kvp.Key;
                            }
                        }
                        if (evictKey != null)
                        {
                            _imageCache.Remove(evictKey);
                            System.Diagnostics.Debug.WriteLine($"[PhotoService] 캐시 제거 (용량 초과): {Path.GetFileName(evictKey)}");
                        }
                    }

                    _imageCache[cacheKey] = new ImageCacheEntry
                    {
                        Ref = new WeakReference<BitmapImage>(bitmap),
                        LastAccess = ++_accessCounter,
                    };
                    System.Diagnostics.Debug.WriteLine($"[PhotoService] 캐시 추가: {Path.GetFileName(fullPath)}");
                }

                return bitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PhotoService] LoadPhotoAsync 오류: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region 사진 삭제

        /// <summary>
        /// 사진 파일 삭제
        /// </summary>
        /// <param name="photoPath">사진 경로 (상대 또는 절대)</param>
        /// <returns>성공 여부</returns>
        public async Task<bool> DeletePhotoAsync(string? photoPath)
        {
            if (string.IsNullOrEmpty(photoPath))
                return true; // 경로가 없으면 성공으로 간주

            try
            {
                // 절대 경로 변환
                string fullPath = Path.IsPathRooted(photoPath)
                    ? photoPath
                    : Path.Combine(_baseDirectory, photoPath);

                if (!File.Exists(fullPath))
                {
                    System.Diagnostics.Debug.WriteLine($"[PhotoService] 삭제할 사진 파일 없음: {fullPath}");
                    return true; // 이미 없으면 성공으로 간주
                }

                // 파일 삭제
                File.Delete(fullPath);
                InvalidateCache(fullPath); // 같은 경로로 재등록 시 이전 이미지가 보이지 않도록
                System.Diagnostics.Debug.WriteLine($"[PhotoService] 사진 삭제 완료: {fullPath}");

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PhotoService] DeletePhotoAsync 오류: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region 헬퍼 메서드

        /// <summary>
        /// 사진 디렉토리 존재 여부 확인 및 생성
        /// </summary>
        public void EnsurePhotoDirectory(int year)
        {
            string photoDir = Path.Combine(_baseDirectory, "Photos", year.ToString());
            Directory.CreateDirectory(photoDir);
        }

        /// <summary>
        /// 사진 파일 경로 가져오기
        /// </summary>
        public string GetPhotoFullPath(string? relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
                return string.Empty;

            return Path.IsPathRooted(relativePath)
                ? relativePath
                : Path.Combine(_baseDirectory, relativePath);
        }

        /// <summary>
        /// 사진 파일 존재 여부 확인
        /// </summary>
        public bool PhotoExists(string? photoPath)
        {
            if (string.IsNullOrEmpty(photoPath))
                return false;

            string fullPath = GetPhotoFullPath(photoPath);
            return File.Exists(fullPath);
        }

        #endregion
    }
}

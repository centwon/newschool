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
        private static readonly Dictionary<string, WeakReference<BitmapImage>> _imageCache = new();
        private static readonly object _cacheLock = new object();
        private const int MaxCacheSize = 50;

        public PhotoService()
        {
            _baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
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

            // 파일 복사
            var destFolder = await StorageFolder.GetFolderFromPathAsync(photoDir);
            await sourceFile.CopyAsync(destFolder, fileName, NameCollisionOption.ReplaceExisting);

            // 상대 경로 반환
            return Path.Combine("Photos", year, fileName);
        }

        #endregion

        #region 사진 로드

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
                    if (_imageCache.TryGetValue(cacheKey, out var weakRef))
                    {
                        if (weakRef.TryGetTarget(out var cachedImage))
                        {
                            System.Diagnostics.Debug.WriteLine($"[PhotoService] 캐시 히트: {Path.GetFileName(fullPath)}");
                            return cachedImage;
                        }
                        else
                        {
                            // WeakReference가 해제됨
                            _imageCache.Remove(cacheKey);
                        }
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
                    // 캐시 크기 제한 (LRU 방식으로 오래된 항목 제거)
                    if (_imageCache.Count >= MaxCacheSize)
                    {
                        // 첫 번째 항목 제거 (간단한 FIFO)
                        var firstKey = _imageCache.Keys.First();
                        _imageCache.Remove(firstKey);
                        System.Diagnostics.Debug.WriteLine($"[PhotoService] 캐시 제거 (용량 초과): {Path.GetFileName(firstKey)}");
                    }

                    _imageCache[cacheKey] = new WeakReference<BitmapImage>(bitmap);
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

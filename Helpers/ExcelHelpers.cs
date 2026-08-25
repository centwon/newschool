using System;
using System.Data;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using Microsoft.UI.Xaml;
using NewSchool.Helpers;

namespace NewSchool.Helpers
{
    /// <summary>
    /// 템플릿 내려받기 결과. <b>취소와 실패를 갈라야</b> 사용자에게 맞는 말을 할 수 있다 —
    /// 예전에는 둘 다 <c>false</c> 라 오류가 "취소되었습니다" 로 둔갑했다.
    /// </summary>
    public enum TemplateDownloadResult
    {
        /// <summary>저장하고 열었다.</summary>
        Completed,

        /// <summary>사용자가 저장 위치 선택을 취소했다.</summary>
        Canceled,

        /// <summary>오류로 만들지 못했다. 자세한 내용은 로그에 남는다.</summary>
        Failed
    }

    /// <summary>
    /// WinUI3용 Excel 파일 처리 헬퍼
    /// MiniExcel 기반
    /// </summary>
    public static class ExcelHelpers
    {
        /// <summary>
        /// Excel 파일 선택 다이얼로그
        /// </summary>
        public static async Task<StorageFile?> PickExcelFileAsync(Window window)
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                ViewMode = PickerViewMode.List
            };
            // .xls(BIFF)는 넣지 않는다 — MiniExcel 은 xlsx/csv 만 읽어서
            // 구버전 .xls 를 고르면 "Excel 파일 읽기 오류" 로만 끝난다.
            picker.FileTypeFilter.Add(".xlsx");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            return await picker.PickSingleFileAsync();
        }

        /// <summary>
        /// Excel 파일 저장 다이얼로그
        /// </summary>
        public static async Task<StorageFile?> SaveExcelFileAsync(
            Window window,
            string defaultFileName = "데이터.xlsx",
            string? suggestedFolder = null)
        {
            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = defaultFileName
            };
            picker.FileTypeChoices.Add("Excel 파일", new[] { ".xlsx" });

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            return await picker.PickSaveFileAsync();
        }

        /// <summary>
        /// DataTable을 Excel 파일로 저장하고 저장 위치 선택
        /// </summary>
        public static async Task<bool> SaveDataTableToExcelAsync(
            Window window,
            DataTable data,
            string? title = null,
            string? subtitle = null,
            bool openAfterSave = true)
        {
            try
            {
                // 기본 파일명 생성 — title 은 화면의 제목 입력칸에서 그대로 오므로
                // "국어/문학 명렬표" 처럼 파일명에 못 쓰는 문자가 섞일 수 있다.
                var safeTitle = Helpers.FileNameHelper.Sanitize(title);
                string defaultFileName = string.IsNullOrWhiteSpace(safeTitle)
                    ? $"데이터_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
                    : $"{safeTitle}_{DateTime.Now:yyyyMMdd}.xlsx";

                // 파일 저장 위치 선택
                var saveFile = await SaveExcelFileAsync(window, defaultFileName);
                if (saveFile == null)
                    return false;

                // 임시 파일에 먼저 저장
                string tempPath = await ExcelHelper.WriteDataAsync(data, title, subtitle);

                // 선택한 위치로 복사
                var tempFile = await StorageFile.GetFileFromPathAsync(tempPath);
                await tempFile.CopyAndReplaceAsync(saveFile);

                // 임시 파일 삭제
                System.IO.File.Delete(tempPath);

                // 파일 열기
                if (openAfterSave)
                {
                    await Launcher.LaunchFileAsync(saveFile);
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 학생 목록을 Excel로 내보내기
        /// </summary>
        public static async Task<bool> ExportStudentsToExcelAsync<T>(
            Window window,
            System.Collections.Generic.IEnumerable<T> students,
            string title = "학생 명단",
            bool openAfterSave = true) where T : class
        {
            try
            {
                var safeTitle = Helpers.FileNameHelper.Sanitize(title);
                if (string.IsNullOrWhiteSpace(safeTitle)) safeTitle = "학생 명단";

                string defaultFileName = $"{safeTitle}_{DateTime.Now:yyyyMMdd}.xlsx";
                var saveFile = await SaveExcelFileAsync(window, defaultFileName);

                if (saveFile == null)
                    return false;

                // 임시 파일에 저장
                string tempPath = await ExcelHelper.WriteListAsync(students);

                // 선택한 위치로 복사
                var tempFile = await StorageFile.GetFileFromPathAsync(tempPath);
                await tempFile.CopyAndReplaceAsync(saveFile);

                System.IO.File.Delete(tempPath);

                if (openAfterSave)
                {
                    await Launcher.LaunchFileAsync(saveFile);
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Excel 템플릿 다운로드
        /// </summary>
        public static async Task<TemplateDownloadResult> DownloadStudentTemplateAsync(Window window)
        {
            string? tempPath = null;

            try
            {
                var saveFile = await SaveExcelFileAsync(
                    window,
                    $"학생명단_템플릿_{DateTime.Now:yyyyMMdd}.xlsx");

                if (saveFile == null)
                    return TemplateDownloadResult.Canceled;

                tempPath = ExcelHelper.CreateStudentTemplate();
                var tempFile = await StorageFile.GetFileFromPathAsync(tempPath);
                await tempFile.CopyAndReplaceAsync(saveFile);

                await Launcher.LaunchFileAsync(saveFile);

                return TemplateDownloadResult.Completed;
            }
            catch (Exception ex)
            {
                // 저장 위치까지 고른 뒤의 실패를 "취소" 로 뭉뚱그리면 사용자는 자기가 취소한 줄 안다.
                System.Diagnostics.Debug.WriteLine($"[ExcelHelpers] 템플릿 다운로드 실패: {ex.Message}");
                NewSchool.Logging.Log.Error("ExcelHelpers", "학생 명단 템플릿 다운로드 실패", ex);
                return TemplateDownloadResult.Failed;
            }
            finally
            {
                // 중간 산출물은 성공하든 실패하든 남기지 않는다 — 예전에는 실패 시 그대로 남았다.
                if (tempPath != null)
                {
                    try { if (System.IO.File.Exists(tempPath)) System.IO.File.Delete(tempPath); }
                    catch { /* 임시 파일 정리 실패 무시 */ }
                }
            }
        }

        // 여러 시트 저장(SaveMultipleSheetsAsync), CSV→Excel 변환(ConvertCsvToExcelAsync),
        // 간편 내보내기(QuickExportAsync)는 호출부가 없어 지웠다(39차).
        // 내보내기는 통합 내보내기 한 경로로 모였다.
    }
}

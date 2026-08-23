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
                // 기본 파일명 생성
                string defaultFileName = string.IsNullOrWhiteSpace(title)
                    ? $"데이터_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
                    : $"{title}_{DateTime.Now:yyyyMMdd}.xlsx";

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
                string defaultFileName = $"{title}_{DateTime.Now:yyyyMMdd}.xlsx";
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
        public static async Task<bool> DownloadStudentTemplateAsync(Window window)
        {
            try
            {
                var saveFile = await SaveExcelFileAsync(
                    window,
                    $"학생명단_템플릿_{DateTime.Now:yyyyMMdd}.xlsx");

                if (saveFile == null)
                    return false;

                string tempPath = ExcelHelper.CreateStudentTemplate();
                var tempFile = await StorageFile.GetFileFromPathAsync(tempPath);
                await tempFile.CopyAndReplaceAsync(saveFile);

                System.IO.File.Delete(tempPath);
                await Launcher.LaunchFileAsync(saveFile);

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // 여러 시트 저장(SaveMultipleSheetsAsync), CSV→Excel 변환(ConvertCsvToExcelAsync),
        // 간편 내보내기(QuickExportAsync)는 호출부가 없어 지웠다(39차).
        // 내보내기는 통합 내보내기 한 경로로 모였다.
    }
}

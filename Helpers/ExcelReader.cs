using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MiniExcelLibs;

namespace NewSchool.Helpers
{
    /// <summary>
    /// Excel 파일 읽기/쓰기 헬퍼 (Native AOT 호환)
    /// MiniExcel 라이브러리만 사용 - 읽기와 쓰기 모두 지원
    /// </summary>
    public static class ExcelHelper
    {
        static ExcelHelper()
        {
            // 한글 지원을 위한 Encoding 등록
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        #region Excel 읽기

        /// <summary>
        /// 기존 코드 호환: Excel 파일의 모든 시트 데이터를 string[,] 배열 리스트로 반환
        /// string[,] 배열은 1-based 인덱스 사용 (Excel과 동일)
        /// </summary>
        public static List<string[,]> DataToText(string filePath, int? sheetNumber = null, string? sheetName = null)
        {
            var result = new List<string[,]>();

            if (!File.Exists(filePath))
                throw new FileNotFoundException("Excel 파일을 찾을 수 없습니다.", filePath);

            try
            {
                // 모든 시트를 DataTable로 읽기
                var sheetNames = MiniExcel.GetSheetNames(filePath);

                // 특정 시트 번호가 지정된 경우
                if (sheetNumber.HasValue)
                {
                    int index = sheetNumber.Value - 1; // 1-based를 0-based로 변환
                    if (index >= 0 && index < sheetNames.Count)
                    {
                        var data = ReadSheetAsArray(filePath, sheetNames[index]);
                        result.Add(data);
                    }
                    return result;
                }

                // 특정 시트 이름이 지정된 경우
                if (!string.IsNullOrWhiteSpace(sheetName))
                {
                    if (sheetNames.Contains(sheetName))
                    {
                        var data = ReadSheetAsArray(filePath, sheetName);
                        result.Add(data);
                    }
                    return result;
                }

                // 모든 시트 반환
                foreach (var sheet in sheetNames)
                {
                    var data = ReadSheetAsArray(filePath, sheet);
                    result.Add(data);
                }

                return result;
            }
            catch (Exception ex)
            {
                throw new Exception($"Excel 파일 읽기 오류: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Excel 시트를 string[,] 배열로 읽기 (1-based 인덱스)
        /// </summary>
        private static string[,] ReadSheetAsArray(string filePath, string sheetName)
        {
            var rows = MiniExcel.Query(filePath, sheetName: sheetName, useHeaderRow: false)
                .Cast<IDictionary<string, object>>()
                .ToList();

            if (rows.Count == 0)
                return new string[1, 1];

            int rowCount = rows.Count;
            // 모든 행에서 최대 열 수 계산 (행마다 열 수가 다를 수 있음)
            int colCount = rows.Max(r => r.Count);

            // 1-based 인덱스를 위해 +1 크기로 생성
            string[,] result = new string[rowCount + 1, colCount + 1];

            for (int row = 0; row < rowCount; row++)
            {
                var rowData = rows[row];
                int col = 0;
                foreach (var cell in rowData.Values)
                {
                    if (col < colCount) // 배열 범위 보호
                    {
                        // 1-based 인덱스로 저장
                        result[row + 1, col + 1] = cell?.ToString() ?? string.Empty;
                    }
                    col++;
                }
            }

            return result;
        }

        // 미사용 메서드 제거 (2026-04-22): GetData, ReadSheetAsObjectArray,
        //   ReadWithHeaders, GetSheetNames, ReadAsDataTable — 호출처 0건 확인
        //   유지되는 공개 읽기 API: DataToText, DataToTextAsync

        #endregion

        #region Excel 쓰기

        /// <summary>
        /// DataTable을 Excel 파일로 저장 (기존 WriteData 메서드 호환)
        /// </summary>
        public static string WriteData(DataTable data, string? title = null, string? subtitle = null, string? filePath = null)
        {
            if (data == null || data.Rows.Count == 0)
                throw new ArgumentException("데이터가 비어있습니다.", nameof(data));

            string outputPath = filePath ?? Path.Combine(Path.GetTempPath(), $"excel_{Guid.NewGuid()}.xlsx");

            var rows = new List<Dictionary<string, object>>();

            // 제목 행
            if (!string.IsNullOrWhiteSpace(title))
            {
                var titleRow = new Dictionary<string, object>();
                for (int i = 0; i < data.Columns.Count; i++)
                {
                    titleRow[data.Columns[i].ColumnName] = i == 0 ? title : "";
                }
                rows.Add(titleRow);
            }

            // 부제목 행
            if (!string.IsNullOrWhiteSpace(subtitle))
            {
                var subtitleRow = new Dictionary<string, object>();
                for (int i = 0; i < data.Columns.Count; i++)
                {
                    subtitleRow[data.Columns[i].ColumnName] = i == 0 ? subtitle : "";
                }
                rows.Add(subtitleRow);
            }

            // 데이터 행
            foreach (DataRow row in data.Rows)
            {
                var rowData = new Dictionary<string, object>();
                for (int i = 0; i < data.Columns.Count; i++)
                {
                    rowData[data.Columns[i].ColumnName] = row[i];
                }
                rows.Add(rowData);
            }

            MiniExcel.SaveAs(outputPath, rows);
            return outputPath;
        }

        /// <summary>
        /// 제네릭 리스트를 Excel로 저장
        /// </summary>
        public static string WriteList<T>(IEnumerable<T> data, string? filePath = null) where T : class
        {
            if (data == null || !data.Any())
                throw new ArgumentException("데이터가 비어있습니다.", nameof(data));

            string outputPath = filePath ?? Path.Combine(Path.GetTempPath(), $"excel_{Guid.NewGuid()}.xlsx");

            MiniExcel.SaveAs(outputPath, data);
            return outputPath;
        }

        // 미사용 메서드 제거 (2026-04-22): WriteArray — 호출처 0건

        /// <summary>
        /// 학생 명단 템플릿 생성
        /// </summary>
        public static string CreateStudentTemplate(string? filePath = null)
        {
            // 이 파일은 저장 위치로 복사한 뒤 지우는 <b>중간 산출물</b>이다.
            // 예전에는 Exports\학생명단_템플릿_20260825.xlsx 처럼 날짜만 붙여 그 폴더에 썼는데,
            //  ① 갓 설치한 PC 에는 Exports 폴더가 없어 DirectoryNotFoundException
            //  ② 같은 날 두 번째부터는 파일이 이미 있어 IOException
            // (MiniExcel.SaveAs 는 기본이 덮어쓰기 금지다 — 실측 확인)
            // 둘 다 "취소되었습니다" 로 둔갑했다. 다른 임시 파일들과 같은 방식으로 맞춘다.
            string outputPath = filePath ?? Path.Combine(
                Path.GetTempPath(), $"학생명단_템플릿_{Guid.NewGuid():N}.xlsx");

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            var template = new List<Dictionary<string, object>>
            {
                new() { ["학년"] = 1, ["반"] = 1, ["번호"] = 1, ["이름"] = "홍길동", ["성별"] = "남" },
                new() { ["학년"] = 1, ["반"] = 1, ["번호"] = 2, ["이름"] = "김철수", ["성별"] = "남" },
                new() { ["학년"] = 1, ["반"] = 1, ["번호"] = 3, ["이름"] = "이영희", ["성별"] = "여" }
            };

            MiniExcel.SaveAs(outputPath, template);
            return outputPath;
        }

        // 미사용 메서드 제거 (2026-04-22): CreateExcelStream(DataTable),
        //   CreateExcelStream<T>(IEnumerable<T>) — 호출처 0건
        // 미사용 메서드 제거 (39차): WriteMultipleSheets, ConvertCsvToExcel — 이들을 감싸던
        //   ExcelHelpers 의 SaveMultipleSheetsAsync·ConvertCsvToExcelAsync 와 함께 사라졌다.

        #endregion

        #region 비동기 메서드

        /// <summary>
        /// 비동기로 Excel 읽기
        /// </summary>
        public static async Task<List<string[,]>> DataToTextAsync(string filePath, int? sheetNumber = null, string? sheetName = null)
        {
            return await Task.Run(() => DataToText(filePath, sheetNumber, sheetName));
        }

        /// <summary>
        /// 비동기로 Excel 쓰기
        /// </summary>
        public static async Task<string> WriteDataAsync(DataTable data, string? title = null, string? subtitle = null, string? filePath = null)
        {
            return await Task.Run(() => WriteData(data, title, subtitle, filePath));
        }

        /// <summary>
        /// 비동기로 리스트 저장
        /// </summary>
        public static async Task<string> WriteListAsync<T>(IEnumerable<T> data, string? filePath = null) where T : class
        {
            return await Task.Run(() => WriteList(data, filePath));
        }

        #endregion
    }
}

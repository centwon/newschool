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

        /// <summary>
        /// object[,] 형식으로 읽기 (기존 GetData 메서드 호환)
        /// </summary>
        public static List<object[,]> GetData(string filePath, int? sheetNumber = null, string? sheetName = null)
        {
            var result = new List<object[,]>();

            if (!File.Exists(filePath))
                throw new FileNotFoundException("Excel 파일을 찾을 수 없습니다.", filePath);

            var sheetNames = MiniExcel.GetSheetNames(filePath);

            // 특정 시트 번호
            if (sheetNumber.HasValue)
            {
                int index = sheetNumber.Value - 1;
                if (index >= 0 && index < sheetNames.Count)
                {
                    result.Add(ReadSheetAsObjectArray(filePath, sheetNames[index]));
                }
                return result;
            }

            // 특정 시트 이름
            if (!string.IsNullOrWhiteSpace(sheetName))
            {
                if (sheetNames.Contains(sheetName))
                {
                    result.Add(ReadSheetAsObjectArray(filePath, sheetName));
                }
                return result;
            }

            // 모든 시트
            foreach (var sheet in sheetNames)
            {
                result.Add(ReadSheetAsObjectArray(filePath, sheet));
            }

            return result;
        }

        /// <summary>
        /// Excel 시트를 object[,] 배열로 읽기 (1-based)
        /// </summary>
        private static object[,] ReadSheetAsObjectArray(string filePath, string sheetName)
        {
            var rows = MiniExcel.Query(filePath, sheetName: sheetName, useHeaderRow: false)
                .Cast<IDictionary<string, object>>()
                .ToList();

            if (rows.Count == 0)
                return new object[1, 1];

            int rowCount = rows.Count;
            // 모든 행에서 최대 열 수 계산
            int colCount = rows.Max(r => r.Count);

            object[,] result = new object[rowCount + 1, colCount + 1];

            for (int row = 0; row < rowCount; row++)
            {
                var rowData = rows[row];
                int col = 0;
                foreach (var cell in rowData.Values)
                {
                    if (col < colCount) // 배열 범위 보호
                    {
                        result[row + 1, col + 1] = cell ?? string.Empty;
                    }
                    col++;
                }
            }

            return result;
        }

        /// <summary>
        /// 헤더와 함께 데이터 읽기
        /// </summary>
        public static List<Dictionary<string, object?>> ReadWithHeaders(string filePath, string? sheetName = null)
        {
            var result = new List<Dictionary<string, object?>>();

            var rows = MiniExcel.Query(filePath, sheetName: sheetName, useHeaderRow: true);

            foreach (var row in rows)
            {
                var dict = new Dictionary<string, object?>();
                var rowDict = row as IDictionary<string, object>;

                if (rowDict != null)
                {
                    foreach (var kvp in rowDict)
                    {
                        dict[kvp.Key] = kvp.Value;
                    }
                }

                result.Add(dict);
            }

            return result;
        }

        /// <summary>
        /// 시트 이름 목록 가져오기
        /// </summary>
        public static List<string> GetSheetNames(string filePath)
        {
            return MiniExcel.GetSheetNames(filePath).ToList();
        }

        /// <summary>
        /// DataTable로 읽기
        /// </summary>
        public static DataTable ReadAsDataTable(string filePath, string? sheetName = null)
        {
            var dt = new DataTable();

            var rows = MiniExcel.Query(filePath, sheetName: sheetName, useHeaderRow: true)
                .Cast<IDictionary<string, object>>()
                .ToList();

            if (rows.Count == 0)
                return dt;

            // 열 추가
            foreach (var key in rows[0].Keys)
            {
                dt.Columns.Add(key);
            }

            // 행 추가
            foreach (var row in rows)
            {
                var dataRow = dt.NewRow();
                foreach (var kvp in row)
                {
                    dataRow[kvp.Key] = kvp.Value ?? DBNull.Value;
                }
                dt.Rows.Add(dataRow);
            }

            return dt;
        }

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

        /// <summary>
        /// 여러 시트를 포함한 Excel 파일 생성
        /// </summary>
        public static string WriteMultipleSheets(Dictionary<string, DataTable> sheets, string? filePath = null)
        {
            if (sheets == null || sheets.Count == 0)
                throw new ArgumentException("시트 데이터가 비어있습니다.", nameof(sheets));

            string outputPath = filePath ?? Path.Combine(Path.GetTempPath(), $"excel_{Guid.NewGuid()}.xlsx");

            var sheetData = new Dictionary<string, object>();

            foreach (var sheet in sheets)
            {
                var rows = new List<Dictionary<string, object>>();
                foreach (DataRow row in sheet.Value.Rows)
                {
                    var rowData = new Dictionary<string, object>();
                    for (int i = 0; i < sheet.Value.Columns.Count; i++)
                    {
                        rowData[sheet.Value.Columns[i].ColumnName] = row[i];
                    }
                    rows.Add(rowData);
                }
                sheetData[sheet.Key] = rows;
            }

            MiniExcel.SaveAs(outputPath, sheetData);
            return outputPath;
        }

        /// <summary>
        /// 2차원 배열을 Excel로 저장
        /// </summary>
        public static string WriteArray(object[,] data, string? filePath = null)
        {
            if (data == null || data.GetLength(0) == 0)
                throw new ArgumentException("데이터가 비어있습니다.", nameof(data));

            string outputPath = filePath ?? Path.Combine(Path.GetTempPath(), $"excel_{Guid.NewGuid()}.xlsx");

            int rowCount = data.GetLength(0);
            int colCount = data.GetLength(1);

            var rows = new List<List<object>>();
            for (int row = 0; row < rowCount; row++)
            {
                var rowData = new List<object>();
                for (int col = 0; col < colCount; col++)
                {
                    rowData.Add(data[row, col]);
                }
                rows.Add(rowData);
            }

            MiniExcel.SaveAs(outputPath, rows);
            return outputPath;
        }

        /// <summary>
        /// 학생 명단 템플릿 생성
        /// </summary>
        public static string CreateStudentTemplate(string? filePath = null)
        {
            string outputPath = filePath ?? Path.Combine(
                Settings.UserDataPath, "Exports",
                $"학생명단_템플릿_{DateTime.Now:yyyyMMdd}.xlsx");

            var template = new List<Dictionary<string, object>>
            {
                new() { ["학년"] = 1, ["반"] = 1, ["번호"] = 1, ["이름"] = "홍길동", ["성별"] = "남" },
                new() { ["학년"] = 1, ["반"] = 1, ["번호"] = 2, ["이름"] = "김철수", ["성별"] = "남" },
                new() { ["학년"] = 1, ["반"] = 1, ["번호"] = 3, ["이름"] = "이영희", ["성별"] = "여" }
            };

            MiniExcel.SaveAs(outputPath, template);
            return outputPath;
        }

        /// <summary>
        /// CSV를 Excel로 변환
        /// </summary>
        public static string ConvertCsvToExcel(string csvPath, string? excelPath = null)
        {
            if (!File.Exists(csvPath))
                throw new FileNotFoundException("CSV 파일을 찾을 수 없습니다.", csvPath);

            string outputPath = excelPath ?? Path.ChangeExtension(csvPath, ".xlsx");

            var rows = new List<Dictionary<string, object>>();
            var lines = File.ReadAllLines(csvPath);

            if (lines.Length == 0)
                throw new Exception("CSV 파일이 비어있습니다.");

            // 첫 줄을 헤더로 사용
            var headers = lines[0].Split(',');

            // 데이터 행 처리
            for (int i = 1; i < lines.Length; i++)
            {
                var values = lines[i].Split(',');
                var row = new Dictionary<string, object>();

                for (int j = 0; j < Math.Min(headers.Length, values.Length); j++)
                {
                    row[headers[j]] = values[j];
                }

                rows.Add(row);
            }

            MiniExcel.SaveAs(outputPath, rows);
            return outputPath;
        }

        /// <summary>
        /// MemoryStream으로 Excel 생성 (다운로드용)
        /// </summary>
        public static MemoryStream CreateExcelStream(DataTable data)
        {
            var stream = new MemoryStream();

            var rows = new List<Dictionary<string, object>>();
            foreach (DataRow row in data.Rows)
            {
                var rowData = new Dictionary<string, object>();
                for (int i = 0; i < data.Columns.Count; i++)
                {
                    rowData[data.Columns[i].ColumnName] = row[i];
                }
                rows.Add(rowData);
            }

            MiniExcel.SaveAs(stream, rows);
            stream.Position = 0;
            return stream;
        }

        /// <summary>
        /// 제네릭 리스트를 MemoryStream으로
        /// </summary>
        public static MemoryStream CreateExcelStream<T>(IEnumerable<T> data) where T : class
        {
            var stream = new MemoryStream();
            MiniExcel.SaveAs(stream, data);
            stream.Position = 0;
            return stream;
        }

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

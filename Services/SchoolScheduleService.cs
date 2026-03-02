using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Controls;
using NewSchool.Models;
using NewSchool.Repositories;

namespace NewSchool.Services;

/// <summary>
/// SchoolSchedule Service
/// 학사일정 비즈니스 로직 및 NEIS API 연동
/// </summary>
public class SchoolScheduleService : IDisposable
{
    private readonly string _dbPath;
    private SchoolScheduleRepository? _repository;
    private bool _disposed;

    // HttpClient는 재사용
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    public SchoolScheduleService(string dbPath)
    {
        _dbPath = SchoolDatabase.DbPath;
    }

    private SchoolScheduleRepository Repository => 
        _repository ??= new SchoolScheduleRepository(_dbPath);

    #region CRUD Operations

    /// <summary>
    /// 학사일정 생성
    /// </summary>
    public async Task<(bool Success, string Message, int No)> CreateScheduleAsync(SchoolSchedule schedule)
    {
        try
        {
            schedule.CreatedAt = DateTime.Now;
            schedule.UpdatedAt = DateTime.Now;
            schedule.IsDeleted = false;

            int no = await Repository.CreateAsync(schedule);
            return (true, "학사일정이 생성되었습니다.", no);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SchoolScheduleService] 학사일정 생성 실패: {ex.Message}");
            return (false, $"학사일정 생성 중 오류가 발생했습니다: {ex.Message}", -1);
        }
    }
    /// <summary>
    /// 학사일정 생성
    /// </summary>
    public async Task<(bool Success, string Message, int Count)> CreateBulkScheduleAsync(List<SchoolSchedule >schedules)
    {
        try
        {
            foreach (var schedule in schedules)
            {
                schedule.CreatedAt = DateTime.Now;
                schedule.UpdatedAt = DateTime.Now;
                schedule.IsDeleted = false;
            }
            int no = await Repository.CreateBulkAsync(schedules);
            return (true, "학사일정이 생성되었습니다.", no);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SchoolScheduleService] 학사일정 생성 실패: {ex.Message}");
            return (false, $"학사일정 생성 중 오류가 발생했습니다: {ex.Message}", -1);
        }
    }
    /// <summary>
    /// 학사일정 수정
    /// </summary>
    public async Task<(bool Success, string Message)> UpdateScheduleAsync(SchoolSchedule schedule)
    {
        try
        {
            schedule.UpdatedAt = DateTime.Now;
            bool success = await Repository.UpdateAsync(schedule);
            return success
                ? (true, "학사일정이 수정되었습니다.")
                : (false, "학사일정 수정에 실패했습니다.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SchoolScheduleService] 학사일정 수정 실패: {ex.Message}");
            return (false, $"학사일정 수정 중 오류가 발생했습니다: {ex.Message}");
        }
    }

    /// <summary>
    /// 학사일정 삭제
    /// </summary>
    public async Task<(bool Success, string Message)> DeleteScheduleAsync(int no)
    {
        try
        {
            bool success = await Repository.DeleteAsync(no);
            return success
                ? (true, "학사일정이 삭제되었습니다.")
                : (false, "학사일정 삭제에 실패했습니다.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SchoolScheduleService] 학사일정 삭제 실패: {ex.Message}");
            return (false, $"학사일정 삭제 중 오류가 발생했습니다: {ex.Message}");
        }
    }
    /// <summary>
    /// 학사일정 삭제
    /// </summary>
    public async Task<(bool Success, string Message, int Count)> DeleteBulkScheduleAsync(List<int> schedules)
    {
        if (schedules.Count == 0)  
        {
            return (true, "삭제할 학사일정이 없습니다.", 0);
        }
        try
        {
            var count = await Repository.DeleteBulkAsync(schedules);
            if (count == schedules.Count)
            {
                return (true, "학사일정이 삭제되었습니다.", count);
            }
            else
            {
                return (false, "일부 학사일정 삭제에 실패했습니다.", count);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SchoolScheduleService] 학사일정 삭제 실패: {ex.Message}");
            return (false, $"학사일정 삭제 중 오류가 발생했습니다: {ex.Message}",-1);
        }
    }
    /// <summary>
    /// 학사일정 조회 (ID)
    /// </summary>
    public async Task<SchoolSchedule?> GetScheduleAsync(int no)
    {
        try
        {
            return await Repository.GetByIdAsync(no);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SchoolScheduleService] 학사일정 조회 실패: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 학년도로 학사일정 조회
    /// </summary>
    public async Task<List<SchoolSchedule>> GetSchedulesByYearAsync(string schoolCode, int year)
    {
        try
        {
            return await Repository.GetBySchoolYearAsync(schoolCode, year);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SchoolScheduleService] 학년도별 학사일정 조회 실패: {ex.Message}");
            return new List<SchoolSchedule>();
        }
    }


    /// <summary>
    /// DB에서 학사일정 조회 (순수 조회 기능)
    /// </summary>
    /// <param name="schoolCode">학교 코드</param>
    /// <param name="startDate">시작 날짜 (선택)</param>
    /// <param name="endDate">종료 날짜 (선택)</param>
    /// <returns>학사일정 리스트</returns>
    public async Task<(bool Success, string Message, List<SchoolSchedule> Schedules)>  GetSchedulesByDataRangeAsync(string schoolCode, DateTime startDate, DateTime endDatel)
    {
        try
        {
            // DB에서 조회
            var schedules = await Repository.GetByDateRangeAsync(schoolCode, startDate, endDatel);
            Debug.WriteLine($"[SchoolScheduleService] DB 조회 완료: {schedules.Count}개");

            string message = schedules.Count > 0
                ? $"DB에서 {schedules.Count}개 조회 완료"
                : "조회된 데이터가 없습니다";

            return (true, message, schedules);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SchoolScheduleService] DB 조회 오류: {ex.Message}");
            return (false, $"DB 조회 오류: {ex.Message}", new List<SchoolSchedule>());
        }
    }
    /// <summary>
    /// DB에서 학사일정 조회 (순수 조회 기능)
    /// </summary>
    /// <param name="schoolCode">학교 코드</param>
    /// <param name="schoolyear">학년도</param>
    /// <param name="endDate">종료 날짜 (선택)</param>
    /// <returns>학사일정 리스트</returns>
    public async Task<(bool Success, string Message, List<SchoolSchedule> Schedules)> GetSchedulesBySchoolYearAsync(string schoolCode, int schoolyear)
    {
        try
        {
            // DB에서 조회
            var schedules = await Repository.GetBySchoolYearAsync(schoolCode, schoolyear);
            Debug.WriteLine($"[SchoolScheduleService] DB 조회 완료: {schedules.Count}개");

            string message = schedules.Count > 0
                ? $"DB에서 {schedules.Count}개 조회 완료"
                : "조회된 데이터가 없습니다";

            return (true, message, schedules);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SchoolScheduleService] DB 조회 오류: {ex.Message}");
            return (false, $"DB 조회 오류: {ex.Message}", new List<SchoolSchedule>());
        }
    }
    /// <summary>
    /// NEIS API에서 학사일정 다운로드 후 DB에 저장
    /// </summary>
    /// <param name="schoolCode">학교 코드</param>
    /// <param name="provinceCode">시도 교육청 코드</param>
    /// <param name="year">다운로드 년도</param>
    /// <param name="startDate">시작 날짜 (선택)</param>
    /// <param name="endDate">종료 날짜 (선택)</param>
    /// <param name="xamlRoot">UI 다이얼로그용 XamlRoot (선택)</param>
    /// <returns>성공 여부, 메시지, 저장된 데이터 개수</returns>
    public async Task<(bool Success, string Message, int SavedCount)> DownloadSchedulesAsync(
            string schoolCode,
            string provinceCode,
            int year,
            DateTime? startDate = null,
            DateTime? endDate = null
            )
    {
        try
        {
            Debug.WriteLine($"[SchoolScheduleService] NEIS API 다운로드 시작: {schoolCode}, {year}년");

            // NEIS API 호출
            var downloadResult = await DownloadFromNeisAsync(
                schoolCode, provinceCode, year, startDate, endDate);

            if (!downloadResult.Success)
            {
                Debug.WriteLine($"[SchoolScheduleService] NEIS API 다운로드 실패: {downloadResult.Message}");
                return (false, downloadResult.Message, 0);
            }

            // 다운로드한 데이터가 없는 경우
            if (downloadResult.Schedules.Count == 0)
            {
                Debug.WriteLine("[SchoolScheduleService] 다운로드된 데이터가 없습니다");
                return (true, "다운로드된 데이터가 없습니다", 0);
            }

            // DB에 저장
            int savedCount = await Repository.CreateBulkAsync(downloadResult.Schedules);
            Debug.WriteLine($"[SchoolScheduleService] DB 저장 완료: {savedCount}개");

            return (true, $"NEIS에서 {savedCount}개 다운로드 및 저장 완료", savedCount);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SchoolScheduleService] 다운로드 오류: {ex.Message}");
            return (false, $"다운로드 오류: {ex.Message}", 0);
        }
    }
    #endregion

    #region NEIS API Integration

    /// <summary>
    /// NEIS API에서 학사일정 다운로드 (DB 저장 없이)
    /// 나이스 데이터포털 API 직접 호출
    /// </summary>
    public async Task<(bool Success, string Message, List<SchoolSchedule> Schedules)> 
        DownloadFromNeisAsync(
            string schoolCode,
            string provinceCode,
            int year,
            DateTime? startDate = null,
            DateTime? endDate = null)
    {
        var schedules = new List<SchoolSchedule>();
        
        // API 키 확인
        if (string.IsNullOrWhiteSpace(Settings.NeisApiKey.Value))
        {
            Debug.WriteLine("[SchoolScheduleService] NEIS API 키가 설정되지 않았습니다.");
            return (false, "NEIS API 키가 설정되지 않았습니다. 설정에서 API 키를 입력해주세요.", schedules);
        }

        try
        {
            // API URL 생성
            string apiUrl = BuildApiUrl(schoolCode, provinceCode, year, startDate, endDate);
            Debug.WriteLine($"[SchoolScheduleService] NEIS API 호출: {apiUrl}");

            // HTTP 요청
            using var response = await _httpClient.GetAsync(apiUrl);
            
            // 응답 상태 확인
            if (!response.IsSuccessStatusCode)
            {
                var errorMsg = $"NEIS API 요청 실패: HTTP {(int)response.StatusCode}";
                Debug.WriteLine($"[SchoolScheduleService] {errorMsg}");
                return (false, errorMsg, schedules);
            }

            // Content-Type 확인
            var contentType = response.Content.Headers?.ContentType?.MediaType;
            if (!string.Equals(contentType, "application/xml", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(contentType, "text/xml", StringComparison.OrdinalIgnoreCase))
            {
                // JSON 응답일 수 있음 (에러 메시지)
                var responseText = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"[SchoolScheduleService] 예상치 못한 응답: {responseText}");
                return (false, $"NEIS API 응답 형식 오류: {contentType}", schedules);
            }

            // XML 파싱
            var responseBody = await response.Content.ReadAsStringAsync();
            var xmlDoc = XDocument.Parse(responseBody);

            // API 에러 확인 (RESULT 태그)
            var resultCode = xmlDoc.Descendants("CODE").FirstOrDefault()?.Value;
            var resultMessage = xmlDoc.Descendants("MESSAGE").FirstOrDefault()?.Value;
            
            if (!string.IsNullOrEmpty(resultCode) && resultCode != "INFO-000")
            {
                // INFO-200: 해당하는 데이터가 없습니다
                if (resultCode == "INFO-200")
                {
                    Debug.WriteLine($"[SchoolScheduleService] NEIS API: 데이터 없음 ({startDate:yyyy-MM-dd} ~ {endDate:yyyy-MM-dd})");
                    return (true, "해당 기간의 학사일정이 없습니다.", schedules);
                }
                
                Debug.WriteLine($"[SchoolScheduleService] NEIS API 에러: {resultCode} - {resultMessage}");
                return (false, $"NEIS API 오류: {resultMessage ?? resultCode}", schedules);
            }

            // 데이터 변환
            foreach (var node in xmlDoc.Descendants("row"))
            {
                var eventName = node.Element("EVENT_NM")?.Value ?? string.Empty;

                // "토요휴업" 필터링
                if (eventName.Contains("토요휴업", StringComparison.Ordinal))
                    continue;

                var schedule = CreateScheduleFromXml(node);
                if (schedule != null)
                {
                    schedule.CreatedAt = DateTime.Now;
                    schedule.UpdatedAt = DateTime.Now;
                    schedule.IsManual = false;
                    schedule.IsDeleted = false;
                    schedules.Add(schedule);
                }
            }

            Debug.WriteLine($"[SchoolScheduleService] NEIS 학사일정 다운로드 완료: {schedules.Count}개");
            return (true, $"NEIS에서 {schedules.Count}개 학사일정 로드 완료", schedules);
        }
        catch (TaskCanceledException ex) when (ex.CancellationToken == default)
        {
            // Timeout
            Debug.WriteLine($"[SchoolScheduleService] NEIS API 타임아웃: {ex.Message}");
            return (false, "NEIS API 요청 시간이 초과되었습니다. 네트워크 연결을 확인해주세요.", schedules);
        }
        catch (HttpRequestException ex)
        {
            Debug.WriteLine($"[SchoolScheduleService] 네트워크 오류: {ex.Message}");
            return (false, $"네트워크 오류: {ex.Message}", schedules);
        }
        catch (System.Xml.XmlException ex)
        {
            Debug.WriteLine($"[SchoolScheduleService] XML 파싱 오류: {ex.Message}");
            return (false, "NEIS API 응답을 파싱할 수 없습니다.", schedules);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SchoolScheduleService] 예기치 않은 오류: {ex.Message}");
            return (false, $"학사일정 로드 중 오류: {ex.Message}", schedules);
        }
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// NEIS API URL 생성
    /// </summary>
    private string BuildApiUrl(
        string schoolCode,
        string provinceCode,
        int year,
        DateTime? startDate,
        DateTime? endDate)
    {
        var sb = new StringBuilder(256);
        sb.Append("http://open.neis.go.kr/hub/SchoolSchedule?KEY=");
        sb.Append(Settings.NeisApiKey.Value);
        sb.Append("&Type=xml&pSize=1000&ATPT_OFCDC_SC_CODE=");
        sb.Append(provinceCode);
        sb.Append("&SD_SCHUL_CODE=");
        sb.Append(schoolCode);
        sb.Append("&AY=");
        sb.Append(year);

        if (startDate.HasValue)
        {
            if (!endDate.HasValue)
            {
                sb.AppendFormat("&AA_YMD={0:yyyyMMdd}", startDate.Value);
            }
            else
            {
                // 날짜 교환 (시작일이 종료일보다 큰 경우)
                if (startDate > endDate)
                {
                    (startDate, endDate) = (endDate, startDate);
                }
                sb.AppendFormat("&AA_FROM_YMD={0:yyyyMMdd}&AA_TO_YMD={1:yyyyMMdd}",
                    startDate.Value, endDate.Value);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// XML 노드에서 SchoolSchedule 생성
    /// </summary>
    private SchoolSchedule? CreateScheduleFromXml(XElement node)
    {
        try
        {
            // 날짜 파싱
            if (!DateTime.TryParseExact(
                node.Element("AA_YMD")?.Value ?? string.Empty,
                "yyyyMMdd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var aaYmd))
            {
                return null;
            }

            // 학년도 파싱
            if (!int.TryParse(node.Element("AY")?.Value, out var ay))
            {
                return null;
            }

            return new SchoolSchedule
            {
                SCHUL_NM = node.Element("SCHUL_NM")?.Value ?? string.Empty,
                ATPT_OFCDC_SC_CODE = node.Element("ATPT_OFCDC_SC_CODE")?.Value ?? string.Empty,
                ATPT_OFCDC_SC_NM = node.Element("ATPT_OFCDC_SC_NM")?.Value ?? string.Empty,
                SD_SCHUL_CODE = node.Element("SD_SCHUL_CODE")?.Value ?? string.Empty,
                AY = ay,
                SBTR_DD_SC_NM = node.Element("SBTR_DD_SC_NM")?.Value ?? string.Empty,
                AA_YMD = aaYmd,
                EVENT_NM = node.Element("EVENT_NM")?.Value ?? string.Empty,
                EVENT_CNTNT = node.Element("EVENT_CNTNT")?.Value ?? string.Empty,
                ONE_GRADE_EVENT_YN = string.Equals(node.Element("ONE_GRADE_EVENT_YN")?.Value, "Y", StringComparison.Ordinal),
                TW_GRADE_EVENT_YN = string.Equals(node.Element("TW_GRADE_EVENT_YN")?.Value, "Y", StringComparison.Ordinal),
                THREE_GRADE_EVENT_YN = string.Equals(node.Element("THREE_GRADE_EVENT_YN")?.Value, "Y", StringComparison.Ordinal),
                FR_GRADE_EVENT_YN = string.Equals(node.Element("FR_GRADE_EVENT_YN")?.Value, "Y", StringComparison.Ordinal),
                FIV_GRADE_EVENT_YN = string.Equals(node.Element("FIV_GRADE_EVENT_YN")?.Value, "Y", StringComparison.Ordinal),
                SIX_GRADE_EVENT_YN = string.Equals(node.Element("SIX_GRADE_EVENT_YN")?.Value, "Y", StringComparison.Ordinal)
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SchoolScheduleService] XML 파싱 실패: {ex.Message}");
            return null;
        }
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (!_disposed)
        {
            _repository?.Dispose();
            _disposed = true;
        }
    }

    #endregion
}

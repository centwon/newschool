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
using NewSchool.Logging;
using NewSchool.Models;
using NewSchool.Repositories;

namespace NewSchool.Services;

/// <summary>
/// SchoolSchedule Service
/// 학사일정 비즈니스 로직 및 NEIS API 연동
/// </summary>
public sealed class SchoolScheduleService : IDisposable
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
        // 주입된 dbPath 를 사용 (정적 SchoolDatabase.DbPath 를 쓰면 주입이 무력화됨)
        _dbPath = dbPath;
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
            Log.Error("SchoolScheduleService", "학사일정 생성 실패", ex);
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
            Log.Error("SchoolScheduleService", "학사일정 생성 실패", ex);
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
            Log.Error("SchoolScheduleService", "학사일정 수정 실패", ex);
            return (false, $"학사일정 수정 중 오류가 발생했습니다: {ex.Message}");
        }
    }

    /// <summary>
    /// 학사일정 삭제(여러 건). 한 건짜리 DeleteScheduleAsync 는 호출부가 없어 지웠다(39차) —
    /// 화면은 선택한 것들을 한꺼번에 지운다.
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
            Log.Error("SchoolScheduleService", "학사일정 삭제 실패", ex);
            return (false, $"학사일정 삭제 중 오류가 발생했습니다: {ex.Message}",-1);
        }
    }
    // ID 한 건 조회(GetScheduleAsync)와 학년도 조회(GetSchedulesByYearAsync)는 호출부가 없어
    // 지웠다(39차). 화면은 기간(GetSchedulesByDataRangeAsync)이나 학년도 범위
    // (GetSchedulesBySchoolYearAsync)로 묶어 읽는다.

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
            Log.Error("SchoolScheduleService", "DB 조회 오류", ex);
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
            Log.Error("SchoolScheduleService", "DB 조회 오류", ex);
            return (false, $"DB 조회 오류: {ex.Message}", new List<SchoolSchedule>());
        }
    }
    // 다운로드 후 곧바로 저장하는 DownloadSchedulesAsync 는 호출부가 없어 지웠다(39차).
    // 화면은 DownloadFromNeisAsync 로 받아 사용자에게 보여 준 뒤, 고른 것만 CreateBulkScheduleAsync
    // 로 저장한다(받은 즉시 전부 저장하지 않는다).
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
            // ⚠ "설정에서 입력해주세요" 라고 말하면 안 된다 — 설정 화면에 인증키 입력칸은 없다.
            //    키는 secrets.json 으로 빌드에 내장되므로 사용자가 채울 수 있는 자리가 아니다.
            return (false, "이 설치본에 NEIS 인증키가 없어 학사일정을 내려받을 수 없습니다. "
                         + "프로그램을 다시 설치하거나 배포자에게 문의해주세요.", schedules);
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
            
            if (NewSchool.Helpers.NeisResult.IsNoData(resultCode))
            {
                Debug.WriteLine($"[SchoolScheduleService] NEIS API: 데이터 없음 ({startDate:yyyy-MM-dd} ~ {endDate:yyyy-MM-dd})");
                return (true, "해당 기간의 학사일정이 없습니다.", schedules);
            }

            if (NewSchool.Helpers.NeisResult.IsError(resultCode))
            {
                Debug.WriteLine($"[SchoolScheduleService] NEIS API 에러: {resultCode} - {resultMessage}");
                return (false, $"NEIS API 오류: {NewSchool.Helpers.NeisResult.Describe(resultCode, resultMessage)}", schedules);
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
            Log.Error("SchoolScheduleService", "NEIS API 타임아웃", ex);
            return (false, "NEIS API 요청 시간이 초과되었습니다. 네트워크 연결을 확인해주세요.", schedules);
        }
        catch (HttpRequestException ex)
        {
            Log.Error("SchoolScheduleService", "네트워크 오류", ex);
            return (false, $"네트워크 오류: {ex.Message}", schedules);
        }
        catch (System.Xml.XmlException ex)
        {
            Log.Error("SchoolScheduleService", "XML 파싱 오류", ex);
            return (false, "NEIS API 응답을 파싱할 수 없습니다.", schedules);
        }
        catch (Exception ex)
        {
            Log.Error("SchoolScheduleService", "예기치 않은 오류", ex);
            return (false, $"학사일정 로드 중 오류: {ex.Message}", schedules);
        }
    }

    /// <summary>
    /// 한 학년도(3/1 ~ 이듬해 2월 말)의 학사일정을 NEIS 에서 받아 <b>DB 에 저장</b>한다.
    ///
    /// <para>예전에는 화면마다 <see cref="DownloadFromNeisAsync"/> 를 직접 부른 뒤 받은 것을
    /// 그리기만 하고 저장하지 않은 채 <c>Settings.IsNeisEventDownloaded</c> 만 켰다. 그러면
    /// 그 다음부터는 "이미 받았다"는 판단으로 DB 를 읽는데 DB 는 비어 있어서, 학사일정이
    /// 딱 한 번 보이고 영영 사라졌다. 받는 것과 저장하는 것과 깃발 세우는 것을 이 한 곳에
    /// 묶어 그 어긋남을 없앤다.</para>
    ///
    /// <para>중복은 <see cref="Repositories.SchoolScheduleRepository.CreateBulkAsync"/> 가
    /// (학교코드+날짜+행사명)으로 걸러내므로 여러 번 불러도 쌓이지 않는다.</para>
    /// </summary>
    /// <returns>Downloaded 는 NEIS 가 준 건수, Saved 는 그중 새로 저장된 건수.</returns>
    public async Task<(bool Success, string Message, int Downloaded, int Saved)> SyncSchoolYearFromNeisAsync(
        string schoolCode,
        string provinceCode,
        int year)
    {
        // 학년도 = 그 해 3/1 ~ 이듬해 2월 말일(윤년이면 2/29)
        var startDate = new DateTime(year, 3, 1);
        var endDate = new DateTime(year + 1, 3, 1).AddDays(-1);

        var (success, message, schedules) = await DownloadFromNeisAsync(
            schoolCode, provinceCode, year, startDate, endDate);

        if (!success)
            return (false, message, 0, 0);

        if (schedules.Count == 0)
            return (true, "해당 학년도의 학사일정이 없습니다.", 0, 0);

        foreach (var schedule in schedules)
        {
            schedule.IsManual = false;
            schedule.CreatedAt = DateTime.Now;
            schedule.UpdatedAt = DateTime.Now;
        }

        var saved = await CreateBulkScheduleAsync(schedules);
        if (!saved.Success)
            return (false, saved.Message, schedules.Count, 0);

        // 저장에 성공한 뒤에만 깃발을 세운다 — 깃발이 켜지면 이후 조회는 DB 만 본다.
        Settings.IsNeisEventDownloaded.Set(true);

        return (true, $"{schedules.Count}개 중 {saved.Count}개 신규 저장", schedules.Count, saved.Count);
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
            Log.Error("SchoolScheduleService", "XML 파싱 실패", ex);
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

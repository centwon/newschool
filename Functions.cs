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
using NewSchool.Scheduler;
using NewSchool.Models;
using NewSchool.Services;
using NewSchool.Controls;

namespace NewSchool
{
    /// <summary>
    /// Native AOT 호환 Functions 클래스
    /// </summary>
    internal static class Functions
    {
        // HttpClient 인스턴스를 한 번만 생성하고 재사용
        private static readonly HttpClient _httpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        // 설정값 캐시 (Native AOT를 위한 정적 필드)
        private static readonly object _settingsLock = new object();

        /// <summary>
        /// 급식 정보 가져오기 (Native AOT 최적화)
        /// </summary>
        public static async Task<List<SchoolMeal>> GetSchoolMealsAsync(
            DateTime? startDate = null,
            DateTime? endDate = null,
            string mmealScCode = "2"            )
        {
            var meals = new List<SchoolMeal>();

            try
            {
                // 네트워크 연결 확인
                if (!IsNetworkAvailable())
                {
                    _ = await MessageBox.ShowAsync("네트워크 연결을 확인하세요","오류");
                    return meals;
                }

                // API URL 생성
                string requestUrl = BuildMealApiUrl(startDate, endDate, mmealScCode);

                // HTTP 요청
                Debug.WriteLine($"[Functions] 급식 API 요청: {requestUrl}");
                using var response = await _httpClient.GetAsync(requestUrl).ConfigureAwait(false);
                Debug.WriteLine($"[Functions] 급식 API 응답: {(int)response.StatusCode}, Content-Type: {response.Content.Headers?.ContentType?.MediaType}");

                response.EnsureSuccessStatusCode();

                // XML 파싱 및 변환
                var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                Debug.WriteLine($"[Functions] 급식 응답 본문 앞 200자: {responseBody[..Math.Min(200, responseBody.Length)]}");
                var xmlDoc = XDocument.Parse(responseBody);

                // NEIS 오류 코드 확인
                var resultCode = xmlDoc.Descendants("CODE").FirstOrDefault()?.Value;
                var resultMsg  = xmlDoc.Descendants("MESSAGE").FirstOrDefault()?.Value;
                if (resultCode != null)
                {
                    Debug.WriteLine($"[Functions] NEIS 결과코드: {resultCode} / {resultMsg}");
                    if (!resultCode.StartsWith("INFO")) return meals;  // 오류 응답
                }

                foreach (var node in xmlDoc.Descendants("row"))
                {
                    var meal = CreateMeal(node);
                    if (meal != null)
                        meals.Add(meal);
                }
                Debug.WriteLine($"[Functions] 급식 파싱 완료: {meals.Count}건");
            }
            catch (TaskCanceledException)
            {
                Debug.WriteLine("[Functions] 급식 API 타임아웃 - 네트워크 또는 서버 응답 없음");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Functions] 급식 정보 오류: {ex.Message}");
            }

            return meals;
        }

        /// <summary>
        /// 현재 교시 정보 가져오기 (Native AOT 호환)
        /// </summary>
        public static Period GetPeriodNow()
        {
            var now = DateTime.Now;
            var dayOfWeek = (int)now.DayOfWeek;

            // 주말 체크
            if (dayOfWeek == 0 || dayOfWeek == 6)
            {
                return new Period { Index = 0, Name = "방과후" };
            }

            var currentTime = new TimeSpan(now.Hour, now.Minute, now.Second);

            // 등교 전
            if (currentTime < Settings.DayStarting)
            {
                return new Period { Index = 0, Name = "방과후" };
            }

            // 조례 시간
            if (currentTime < Settings.DayStarting.Value + Settings.AssemblyTime.Value)
            {
                return new Period
                {
                    Index = 0,
                    Name = "조례",
                    Time = Settings.DayStarting,
                    Duration = Settings.AssemblyTime
                };
            }

            // 교시 계산
            
            TimeSpan firstPeriodStart = Settings.DayStarting.Value + Settings.AssemblyTime.Value + Settings.BreakTime.Value;
            int totalPeriods = (dayOfWeek == 1 || dayOfWeek == 3) ? 6 : 7;

            var schoolEndTime = firstPeriodStart +
                (Settings.OnePeriod.Value * totalPeriods) +
                (Settings.BreakTime.Value * (totalPeriods - 2)) +
                Settings.LunchTime;

            // 1교시 전 휴식
            if (currentTime < firstPeriodStart)
            {
                return new Period
                {
                    Index = 0,
                    Name = "휴식: 조례~1교시",
                    Time = firstPeriodStart - Settings.BreakTime,
                    Duration = Settings.BreakTime
                };
            }

            // 수업 시간대
            if (currentTime < schoolEndTime)
            {
                for (int i = 1; i <= totalPeriods; i++)
                {
                    var periodEnd = CalculatePeriodEnd(i, firstPeriodStart);
                    var breakEnd = periodEnd + (i == 4 ? Settings.LunchTime : Settings.BreakTime);

                    if (currentTime < periodEnd)
                    {
                        return new Period
                        {
                            Index = i,
                            Name = $"{i}교시",
                            Time = CalculatePeriodStart(i, firstPeriodStart),
                            Duration = Settings.OnePeriod
                        };
                    }

                    if (i < totalPeriods && currentTime < breakEnd)
                    {
                        var breakName = i == 4 ? "점심 시간" : $"휴식: {i}교시~{i + 1}교시";
                        return new Period
                        {
                            Index = 0,
                            Name = breakName,
                            Time = periodEnd,
                            Duration = i == 4 ? Settings.LunchTime : Settings.BreakTime
                        };
                    }
                }
            }

            // 청소 시간
            if (currentTime < schoolEndTime + TimeSpan.FromMinutes(10))
            {
                return new Period
                {
                    Index = 0,
                    Name = "청소",
                    Time = schoolEndTime,
                    Duration = TimeSpan.FromMinutes(10)
                };
            }

            // 종례 시간
            if (currentTime < schoolEndTime + TimeSpan.FromMinutes(20))
            {
                return new Period
                {
                    Index = 0,
                    Name = "종례",
                    Time = schoolEndTime + TimeSpan.FromMinutes(10),
                    Duration = TimeSpan.FromMinutes(10)
                };
            }

            // 방과후
            return new Period { Index = 0, Name = "방과후" };
        }

        #region Helper Methods

        private static string BuildMealApiUrl(DateTime? startDate, DateTime? endDate, string mmealScCode)
        {
            var sb = new StringBuilder(256);
            sb.Append("https://open.neis.go.kr/hub/mealServiceDietInfo?Type=xml&KEY=");
            sb.Append(Settings.NeisApiKey);
            sb.Append("&ATPT_OFCDC_SC_CODE=");
            sb.Append(Settings.ProvinceCode);
            sb.Append("&SD_SCHUL_CODE=");
            sb.Append(Settings.SchoolCode);
            if (!string.IsNullOrEmpty(mmealScCode))
            {
                sb.Append("&MMEAL_SC_CODE=");
                sb.Append(mmealScCode);
            }

            if (startDate.HasValue)
            {
                if (!endDate.HasValue)
                {
                    sb.AppendFormat("&MLSV_YMD={0:yyyyMMdd}", startDate.Value);
                }
                else
                {
                    if (startDate > endDate)
                    {
                        (startDate, endDate) = (endDate, startDate);
                    }
                    sb.AppendFormat("&MLSV_FROM_YMD={0:yyyyMMdd}&MLSV_TO_YMD={1:yyyyMMdd}",
                        startDate.Value, endDate.Value);
                }
            }

            return sb.ToString();
        }

        private static SchoolMeal? CreateMeal(XElement node)
        {
            // 날짜 파싱
            if (!DateTime.TryParseExact(
                node.Element("MLSV_YMD")?.Value ?? string.Empty,
                "yyyyMMdd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var mlsvYmd))
            {
                return null;
            }

            return new SchoolMeal
            {
                ATPT_OFCDC_SC_CODE = node.Element("ATPT_OFCDC_SC_CODE")?.Value ?? string.Empty,
                ATPT_OFCDC_SC_NM = node.Element("ATPT_OFCDC_SC_NM")?.Value ?? string.Empty,
                DDISH_NM = GetMenuStringOptimized(node.Element("DDISH_NM")?.Value ?? string.Empty),
                MLSV_YMD = mlsvYmd,
                MMEAL_SC_NM = node.Element("MMEAL_SC_NM")?.Value ?? string.Empty,
                SCHUL_NM = node.Element("SCHUL_NM")?.Value ?? string.Empty,
                SD_SCHUL_CODE = node.Element("SD_SCHUL_CODE")?.Value ?? string.Empty
            };
        }

        private static string GetMenuStringOptimized(string dishName)
        {
            if (string.IsNullOrWhiteSpace(dishName))
                return string.Empty;

            var sb = new StringBuilder();
            var items = dishName.Split(new[] { "<br/>" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var item in items)
            {
                var cleaned = item;

                // 특수 문자 제거
                var idx = cleaned.IndexOfAny(new[] { '*', ' ', '(' });
                if (idx > 0)
                {
                    cleaned = cleaned.Substring(0, idx);
                }

                if (sb.Length > 0)
                {
                    sb.AppendLine();
                }
                sb.Append(cleaned);
            }

            return sb.ToString();
        }

        private static TimeSpan CalculatePeriodStart(int period, TimeSpan firstPeriodStart)
        {
            if (period <= 0) return firstPeriodStart;

            var elapsedPeriods = period - 1;
            var breakCount = period <= 4 ? elapsedPeriods : elapsedPeriods - 1;
            var lunchCount = period > 4 ? 1 : 0;

            return firstPeriodStart +
                (Settings.OnePeriod.Value * elapsedPeriods) +
                (Settings.BreakTime.Value * breakCount) +
                (Settings.LunchTime.Value * lunchCount);
        }

        private static TimeSpan CalculatePeriodEnd(int period, TimeSpan firstPeriodStart)
        {
            return CalculatePeriodStart(period, firstPeriodStart) + Settings.OnePeriod;
        }

        internal static bool IsNetworkAvailable()
        {
            // WinUI 3에서 네트워크 상태 확인
            var profile = Windows.Networking.Connectivity.NetworkInformation.GetInternetConnectionProfile();
            return profile != null &&
                   profile.GetNetworkConnectivityLevel() == Windows.Networking.Connectivity.NetworkConnectivityLevel.InternetAccess;
        }

        #endregion
    }
}

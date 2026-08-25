using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using NewSchool.Models;

namespace NewSchool.Dialogs
{
    public sealed partial class SchoolSearchDialog : ContentDialog
    {
        // 검색마다 new HttpClient() 하면 소켓 고갈(TIME_WAIT) 위험 → 공유 인스턴스 사용
        private static readonly HttpClient _httpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        private bool _isSchoolSelected = false;
        public bool IsSchoolSelected
        {
            get => _isSchoolSelected;
            set
            {
                _isSchoolSelected = value;
                IsPrimaryButtonEnabled = value;
            }
        }

        public School? SelectedSchool { get; private set; }

        public SchoolSearchDialog()
        {
            this.InitializeComponent();
        }

        private async void OnSearchClick(object sender, RoutedEventArgs e)
        {
            await SearchSchoolAsync();
        }

        private async void OnSchoolNameKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                await SearchSchoolAsync();
            }
        }

        private async System.Threading.Tasks.Task SearchSchoolAsync()
        {
            string schoolName = SchoolNameTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(schoolName))
            {
                InfoTextBlock.Text = "학교 이름을 입력해주세요.";
                return;
            }

            // 키가 비면 NEIS 는 200 응답에 오류 코드를 실어 보낸다 — 그것을 "검색 결과 없음"으로
            // 보여주면 사용자는 학교 이름을 잘못 쳤다고 믿고 계속 헤맨다. 아예 보내지 않고 사실대로 말한다.
            if (string.IsNullOrWhiteSpace(Settings.NeisApiKey.Value))
            {
                InfoTextBlock.Text = "이 설치본에 NEIS 인증키가 없어 학교를 검색할 수 없습니다.\n"
                                   + "프로그램을 다시 설치하거나 배포자에게 문의해주세요.";
                SchoolListView.ItemsSource = null;
                return;
            }

            try
            {
                // UI 상태 업데이트
                LoadingRing.IsActive = true;
                LoadingRing.Visibility = Visibility.Visible;
                SearchButton.IsEnabled = false;
                SchoolNameTextBox.IsEnabled = false;
                InfoTextBlock.Text = "검색 중...";

                // API 호출 (공유 HttpClient)
                string apiEndpoint = "https://open.neis.go.kr/hub/schoolInfo";
                string apiKey = Settings.NeisApiKey.Value;
                string requestUrl = $"{apiEndpoint}?KEY={apiKey}&Type=xml&pSize=100&SCHUL_NM={Uri.EscapeDataString(schoolName)}";

                // API 키가 로그에 남지 않도록 KEY 값을 가린다(급식·연간계획 로그와 동일 정책).
                // 빈 키면 Replace("") 가 예외를 내므로 그대로 출력.
                string maskedUrl = string.IsNullOrEmpty(apiKey) ? requestUrl : requestUrl.Replace(apiKey, "***");
                Debug.WriteLine($"[SchoolSearch] 요청 URL: {maskedUrl}");

                HttpResponseMessage response = await _httpClient.GetAsync(requestUrl);
                response.EnsureSuccessStatusCode();
                string responseBody = await response.Content.ReadAsStringAsync();

                Debug.WriteLine($"[SchoolSearch] 응답 수신: {responseBody.Length} bytes");

                // XML 파싱
                List<School> schools = ParseSchoolInfo(responseBody);

                // 결과 표시
                SchoolListView.ItemsSource = schools;

                if (schools.Count > 0)
                {
                    InfoTextBlock.Text = $"{schools.Count}개의 학교를 찾았습니다.";
                }
                else
                {
                    InfoTextBlock.Text = "검색 결과가 없습니다. 다른 검색어를 입력해주세요.";
                }
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine($"[SchoolSearch] HTTP 오류: {ex.Message}");
                InfoTextBlock.Text = "네트워크 오류가 발생했습니다. 인터넷 연결을 확인해주세요.";
                SchoolListView.ItemsSource = null;
            }
            catch (TaskCanceledException)
            {
                Debug.WriteLine($"[SchoolSearch] 타임아웃");
                InfoTextBlock.Text = "요청 시간이 초과되었습니다. 다시 시도해주세요.";
                SchoolListView.ItemsSource = null;
            }
            catch (InvalidOperationException ex)
            {
                // NEIS 가 오류 코드를 실어 보낸 경우 — 급식(Functions.GetMealsAsync)과 같은 규칙으로
                // 그 메시지를 그대로 올린다. "검색 결과가 없습니다"로 뭉개면 원인을 알 길이 없다.
                Debug.WriteLine($"[SchoolSearch] API 오류: {ex.Message}");
                InfoTextBlock.Text = ex.Message;
                SchoolListView.ItemsSource = null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SchoolSearch] 오류: {ex.Message}");
                InfoTextBlock.Text = $"오류가 발생했습니다: {ex.Message}";
                SchoolListView.ItemsSource = null;
            }
            finally
            {
                // UI 상태 복원
                LoadingRing.IsActive = false;
                LoadingRing.Visibility = Visibility.Collapsed;
                SearchButton.IsEnabled = true;
                SchoolNameTextBox.IsEnabled = true;
            }
        }

        private static List<School> ParseSchoolInfo(string xml)
        {
            var schools = new List<School>();

            try
            {
                XmlDocument xmlDoc = new();
                xmlDoc.LoadXml(xml);

                // 에러 체크 — NEIS 는 실패도 HTTP 200 에 코드로 실어 보낸다.
                // INFO-000(정상)·INFO-200(해당 데이터 없음)만 "결과 0건"이고, 나머지(인증키 오류,
                // 호출 한도 초과, 서비스 점검 등)는 검색이 성립하지 않은 것이므로 올려 보낸다.
                XmlNodeList? errorNodes = xmlDoc.GetElementsByTagName("RESULT");
                if (errorNodes != null && errorNodes.Count > 0)
                {
                    string? errorCode = errorNodes[0]?["CODE"]?.InnerText;
                    string? errorMsg = errorNodes[0]?["MESSAGE"]?.InnerText;

                    if (NewSchool.Helpers.NeisResult.IsError(errorCode))
                    {
                        Debug.WriteLine($"[SchoolSearch] API 오류: {errorCode} - {errorMsg}");
                        throw new InvalidOperationException(
                            $"학교 정보를 받지 못했습니다: {NewSchool.Helpers.NeisResult.Describe(errorCode, errorMsg)}");
                    }
                }

                // 학교 정보 파싱
                XmlNodeList? schoolNodes = xmlDoc.GetElementsByTagName("row");
                if (schoolNodes == null)
                {
                    return schools;
                }

                foreach (XmlNode schoolNode in schoolNodes)
                {
                    try
                    {
                        // NEIS API 필드명 매핑
                        XmlNode? schoolCode = schoolNode["SD_SCHUL_CODE"];
                        XmlNode? schoolName = schoolNode["SCHUL_NM"];
                        XmlNode? address = schoolNode["ORG_RDNMA"];
                        XmlNode? atptOfcdcScCode = schoolNode["ATPT_OFCDC_SC_CODE"];
                        XmlNode? atptOfcdcScName = schoolNode["ATPT_OFCDC_SC_NM"];
                        XmlNode? schoolType = schoolNode["SCHUL_KND_SC_NM"]; // 학교종류명
                        XmlNode? foundationDate = schoolNode["FOND_YMD"]; // 개교기념일
                        XmlNode? phone = schoolNode["ORG_TELNO"]; // 전화번호
                        XmlNode? fax = schoolNode["ORG_FAXNO"]; // 팩스번호
                        XmlNode? website = schoolNode["HMPG_ADRES"]; // 홈페이지주소

                        // 필수 필드 체크
                        if (schoolCode == null || schoolName == null || address == null ||
                            atptOfcdcScName == null || atptOfcdcScCode == null)
                        {
                            continue;
                        }

                        School school = new()
                        {
                            SchoolCode = schoolCode.InnerText,
                            SchoolName = schoolName.InnerText,
                            Address = address.InnerText,
                            ATPT_OFCDC_SC_CODE = atptOfcdcScCode.InnerText,
                            ATPT_OFCDC_SC_NAME = atptOfcdcScName.InnerText,
                            SchoolType = schoolType?.InnerText ?? string.Empty,
                            FoundationDate = foundationDate?.InnerText ?? string.Empty,
                            Phone = phone?.InnerText ?? string.Empty,
                            Fax = fax?.InnerText ?? string.Empty,
                            Website = website?.InnerText ?? string.Empty,
                            IsActive = true,
                            CreatedAt = DateTime.Now,
                            UpdatedAt = DateTime.Now
                        };

                        schools.Add(school);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[SchoolSearch] 개별 노드 파싱 오류: {ex.Message}");
                        continue;
                    }
                }
            }
            catch (InvalidOperationException)
            {
                throw;   // 위에서 만든 NEIS 오류 안내 — 여기서 삼키면 다시 "결과 없음"이 된다
            }
            catch (Exception ex)
            {
                // 응답이 XML 이 아니거나 형태가 다른 경우(점검 안내 HTML 등)도 "결과 0건"이 아니다
                Debug.WriteLine($"[SchoolSearch] XML 파싱 오류: {ex.Message}");
                throw new InvalidOperationException("학교 정보 응답을 읽지 못했습니다. 잠시 후 다시 시도해주세요.", ex);
            }

            return schools;
        }

        private void OnSchoolSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SchoolListView.SelectedItem is School school)
            {
                SelectedSchool = school;
                IsSchoolSelected = true;
                InfoTextBlock.Text = $"선택됨: {school.SchoolName}";
            }
            else
            {
                SelectedSchool = null;
                IsSchoolSelected = false;
            }
        }

        private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            // 선택된 학교 정보는 SelectedSchool 속성에 저장되어 있음
        }
    }
}

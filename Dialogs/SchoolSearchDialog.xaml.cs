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

            try
            {
                // UI 상태 업데이트
                LoadingRing.IsActive = true;
                LoadingRing.Visibility = Visibility.Visible;
                SearchButton.IsEnabled = false;
                SchoolNameTextBox.IsEnabled = false;
                InfoTextBlock.Text = "검색 중...";

                // API 호출
                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(10);

                string apiEndpoint = "https://open.neis.go.kr/hub/schoolInfo";
                string apiKey = Settings.NeisApiKey.Value;
                string requestUrl = $"{apiEndpoint}?KEY={apiKey}&Type=xml&pSize=100&SCHUL_NM={Uri.EscapeDataString(schoolName)}";

                Debug.WriteLine($"[SchoolSearch] 요청 URL: {requestUrl}");

                HttpResponseMessage response = await httpClient.GetAsync(requestUrl);
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

                // 에러 체크
                XmlNodeList? errorNodes = xmlDoc.GetElementsByTagName("RESULT");
                if (errorNodes != null && errorNodes.Count > 0)
                {
                    XmlNode? errorCode = errorNodes[0]?["CODE"];
                    XmlNode? errorMsg = errorNodes[0]?["MESSAGE"];

                    if (errorCode?.InnerText != "INFO-000") // 정상 응답 코드
                    {
                        Debug.WriteLine($"[SchoolSearch] API 오류: {errorCode?.InnerText} - {errorMsg?.InnerText}");
                        return schools;
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
                        XmlNode? principalName = schoolNode["COEDU_SC_NM"]; // 교장명은 API에 없음

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
            catch (Exception ex)
            {
                Debug.WriteLine($"[SchoolSearch] XML 파싱 오류: {ex.Message}");
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

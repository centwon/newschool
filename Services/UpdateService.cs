using System;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace NewSchool.Services;

public static class UpdateService
{
    private const string GitHubOwner = "centwon";
    private const string GitHubRepo = "NewSchool";
    private const string ApiUrl = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";

    private static readonly HttpClient _http = CreateHttpClient();

    /// <summary>
    /// 현재 앱 버전
    /// </summary>
    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

    /// <summary>
    /// 최신 릴리스 정보 확인
    /// </summary>
    public static async Task<UpdateResult> CheckForUpdateAsync()
    {
        try
        {
            var response = await _http.GetAsync(ApiUrl);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return UpdateResult.Fail("아직 게시된 릴리스가 없습니다.");

            if (!response.IsSuccessStatusCode)
                return UpdateResult.Fail($"서버 응답 오류: {(int)response.StatusCode}");

            var json = await response.Content.ReadAsStringAsync();
            var release = JsonSerializer.Deserialize(json, GitHubJsonContext.Default.GitHubRelease);
            if (release == null)
                return UpdateResult.Fail("릴리스 정보를 읽을 수 없습니다.");

            // tag_name에서 버전 추출 (v1.2.3 → 1.2.3)
            var versionStr = release.TagName?.TrimStart('v', 'V') ?? "";
            if (!Version.TryParse(versionStr, out var latestVersion))
                return UpdateResult.Fail($"버전 형식을 인식할 수 없습니다: {release.TagName}");

            // 다운로드 URL 찾기 (.exe 파일)
            string? downloadUrl = null;
            if (release.Assets != null)
            {
                foreach (var asset in release.Assets)
                {
                    if (asset.Name?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        downloadUrl = asset.DownloadUrl;
                        break;
                    }
                }
            }

            // 릴리스 페이지 URL (다운로드 URL이 없을 때 폴백)
            downloadUrl ??= release.HtmlUrl ?? "";

            return UpdateResult.Success(new UpdateInfo
            {
                LatestVersion = latestVersion,
                IsUpdateAvailable = latestVersion > CurrentVersion,
                DownloadUrl = downloadUrl,
                ReleaseNotes = release.Body ?? "",
                ReleaseName = release.Name ?? ""
            });
        }
        catch (HttpRequestException ex)
        {
            Debug.WriteLine($"[UpdateService] 네트워크 오류: {ex.Message}");
            return UpdateResult.Fail("서버에 연결할 수 없습니다. 인터넷 연결을 확인하세요.");
        }
        catch (TaskCanceledException)
        {
            return UpdateResult.Fail("요청 시간이 초과되었습니다. 나중에 다시 시도하세요.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UpdateService] 업데이트 확인 실패: {ex.Message}");
            return UpdateResult.Fail($"업데이트 확인 중 오류가 발생했습니다: {ex.Message}");
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("User-Agent", "NewSchool-App");
        client.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
        client.Timeout = TimeSpan.FromSeconds(10);
        return client;
    }
}

public class UpdateResult
{
    public bool IsSuccess { get; set; }
    public UpdateInfo? Info { get; set; }
    public string ErrorMessage { get; set; } = "";

    public static UpdateResult Success(UpdateInfo info) => new() { IsSuccess = true, Info = info };
    public static UpdateResult Fail(string message) => new() { IsSuccess = false, ErrorMessage = message };
}

public class UpdateInfo
{
    public Version LatestVersion { get; set; } = new(1, 0, 0);
    public bool IsUpdateAvailable { get; set; }
    public string DownloadUrl { get; set; } = "";
    public string ReleaseNotes { get; set; } = "";
    public string ReleaseName { get; set; } = "";
}

// GitHub API 응답 모델
internal class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }

    [JsonPropertyName("assets")]
    public GitHubAsset[]? Assets { get; set; }
}

internal class GitHubAsset
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("browser_download_url")]
    public string? DownloadUrl { get; set; }
}

[JsonSerializable(typeof(GitHubRelease))]
internal partial class GitHubJsonContext : JsonSerializerContext
{
}

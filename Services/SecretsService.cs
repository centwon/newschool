using System;
using System.IO;
using System.Text.Json;

namespace NewSchool.Services;

/// <summary>
/// 로컬 secrets.json 에서 API 키·OAuth 자격증명을 로드하는 통합 서비스.
/// 파일은 git 에 포함되지 않으며(.gitignore), 빌드 산출물 디렉터리에 조건부 복사된다.
/// 파일이 없거나 키가 없으면 빈 문자열을 반환 — 호출부에서 기능 비활성 처리.
/// </summary>
public static class SecretsService
{
    /// <summary>Google OAuth 클라이언트 ID</summary>
    public static string GoogleClientId { get; }

    /// <summary>Google OAuth 클라이언트 Secret</summary>
    public static string GoogleClientSecret { get; }

    /// <summary>나이스 데이터포털 Open API 인증키</summary>
    public static string NeisApiKey { get; }

    static SecretsService()
    {
        GoogleClientId = "";
        GoogleClientSecret = "";
        NeisApiKey = "";

        var configPath = Path.Combine(AppContext.BaseDirectory, "secrets.json");
        if (!File.Exists(configPath))
        {
            System.Diagnostics.Debug.WriteLine($"[SecretsService] secrets.json 없음 — 빈 값으로 초기화.");
            return;
        }

        try
        {
            var json = File.ReadAllText(configPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("google_oauth", out var g))
            {
                GoogleClientId = TryGetString(g, "client_id");
                GoogleClientSecret = TryGetString(g, "client_secret");
            }
            NeisApiKey = TryGetString(root, "neis_api_key");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SecretsService] secrets.json 파싱 실패: {ex.Message}");
        }
    }

    private static string TryGetString(JsonElement elem, string name)
    {
        return elem.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? (v.GetString() ?? "")
            : "";
    }
}

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace NewSchool.Services;

/// <summary>
/// API 키·OAuth 자격증명을 로드하는 통합 서비스.
/// secrets.json 은 빌드 시 어셈블리에 내장(EmbeddedResource)되므로 외부 파일이 필요 없다.
/// (단, exe 옆에 secrets.json 이 있으면 그 값이 우선 적용 — 재빌드 없이 키 교체용 override)
/// 키가 없으면 빈 문자열을 반환 — 호출부에서 기능 비활성 처리.
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

        var json = LoadSecretsJson();
        if (string.IsNullOrWhiteSpace(json))
        {
            System.Diagnostics.Debug.WriteLine("[SecretsService] secrets 없음 — 빈 값으로 초기화.");
            return;
        }

        try
        {
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
            System.Diagnostics.Debug.WriteLine($"[SecretsService] secrets 파싱 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// secrets.json 내용을 가져온다.
    /// 1순위: exe 옆 외부 파일(override) → 2순위: 어셈블리 내장 리소스.
    /// </summary>
    private static string LoadSecretsJson()
    {
        // 1) 외부 override 파일 (있을 때만 — 기본 배포에는 없음)
        try
        {
            var external = Path.Combine(AppContext.BaseDirectory, "secrets.json");
            if (File.Exists(external))
                return File.ReadAllText(external);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SecretsService] 외부 secrets.json 읽기 실패: {ex.Message}");
        }

        // 2) 내장 리소스 (빌드 시 포함된 secrets.json)
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var resName = asm.GetManifestResourceNames()
                             .FirstOrDefault(n => n.EndsWith("secrets.json", StringComparison.OrdinalIgnoreCase));
            if (resName != null)
            {
                using var stream = asm.GetManifestResourceStream(resName);
                if (stream != null)
                {
                    using var sr = new StreamReader(stream);
                    return sr.ReadToEnd();
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SecretsService] 내장 secrets 읽기 실패: {ex.Message}");
        }

        return "";
    }

    private static string TryGetString(JsonElement elem, string name)
    {
        return elem.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? (v.GetString() ?? "")
            : "";
    }
}

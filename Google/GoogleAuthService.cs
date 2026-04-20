using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NewSchool.Google;

/// <summary>
/// Google OAuth 2.0 인증 서비스
/// - Loopback redirect + PKCE 방식
/// - 토큰 DPAPI 암호화 후 Settings.db에 저장
/// </summary>
public class GoogleAuthService : IDisposable
{
    private const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string RevokeEndpoint = "https://oauth2.googleapis.com/revoke";
    private const string Scope = "https://www.googleapis.com/auth/calendar";

    // ──────────────────────────────────────────────────────────────
    // Google Cloud Console에서 발급받은 OAuth 2.0 인증 정보
    // secrets.json 의 google_oauth 섹션에서 로드 (SecretsService, git 제외)
    // ──────────────────────────────────────────────────────────────
    internal static string ClientId => Services.SecretsService.GoogleClientId;
    internal static string ClientSecret => Services.SecretsService.GoogleClientSecret;

    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private bool _disposed;

    /// <summary>OAuth 자격증명이 설정되어 있는지 확인</summary>
    public static bool HasCredentials =>
        ClientId != "YOUR_CLIENT_ID_HERE" && !string.IsNullOrEmpty(ClientId);

    /// <summary>인증 완료 여부</summary>
    public bool IsAuthenticated =>
        HasCredentials && !string.IsNullOrEmpty(Settings.GoogleRefreshToken.Value);

    /// <summary>
    /// 유효한 Access Token 반환 (만료 시 자동 갱신)
    /// </summary>
    public async Task<string?> GetValidAccessTokenAsync()
    {
        if (!IsAuthenticated) return null;

        // 만료 확인
        string expiryStr = Decrypt(Settings.GoogleTokenExpiry.Value);
        if (DateTime.TryParse(expiryStr, out var expiry) && DateTime.UtcNow < expiry.AddMinutes(-2))
        {
            // 아직 유효
            string token = Decrypt(Settings.GoogleAccessToken.Value);
            if (!string.IsNullOrEmpty(token))
                return token;
        }

        // 갱신 필요
        if (await RefreshTokenAsync())
            return Decrypt(Settings.GoogleAccessToken.Value);

        return null;
    }

    /// <summary>
    /// 전체 OAuth 플로우 실행 (브라우저 팝업)
    /// </summary>
    public async Task<bool> AuthenticateAsync(CancellationToken ct = default)
    {
        string clientId = ClientId;
        string clientSecret = ClientSecret;

        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
        {
            Debug.WriteLine("[GoogleAuth] Client ID/Secret이 설정되지 않았습니다.");
            return false;
        }

        try
        {
            // 1. PKCE 생성
            string codeVerifier = GenerateCodeVerifier();
            string codeChallenge = GenerateCodeChallenge(codeVerifier);

            // 2. 사용 가능한 포트 찾기
            int port = FindAvailablePort();
            string redirectUri = $"http://127.0.0.1:{port}/callback/";

            // 3. HttpListener 시작
            using var listener = new HttpListener();
            listener.Prefixes.Add(redirectUri);
            listener.Start();
            Debug.WriteLine($"[GoogleAuth] Listener 시작: {redirectUri}");

            // 4. 인증 URL 생성 + 브라우저 열기
            string authUrl = $"{AuthEndpoint}" +
                $"?client_id={Uri.EscapeDataString(clientId)}" +
                $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                $"&response_type=code" +
                $"&scope={Uri.EscapeDataString(Scope)}" +
                $"&code_challenge={Uri.EscapeDataString(codeChallenge)}" +
                $"&code_challenge_method=S256" +
                $"&access_type=offline" +
                $"&prompt=consent";

            Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });
            Debug.WriteLine("[GoogleAuth] 브라우저 열림");

            // 5. 콜백 대기 (5분 타임아웃)
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromMinutes(5));

            var context = await listener.GetContextAsync().WaitAsync(timeoutCts.Token);
            string? code = context.Request.QueryString["code"];
            string? error = context.Request.QueryString["error"];

            // 6. 브라우저에 응답 보내기
            string responseHtml = error == null
                ? "<html><body><h2>인증 완료!</h2><p>이 창을 닫아도 됩니다.</p></body></html>"
                : $"<html><body><h2>인증 실패</h2><p>{error}</p></body></html>";
            byte[] responseBytes = Encoding.UTF8.GetBytes(responseHtml);
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = responseBytes.Length;
            await context.Response.OutputStream.WriteAsync(responseBytes, ct);
            context.Response.Close();
            listener.Stop();

            if (string.IsNullOrEmpty(code))
            {
                Debug.WriteLine($"[GoogleAuth] 인증 실패: {error}");
                return false;
            }

            // 7. Authorization code → Token 교환
            return await ExchangeCodeForTokenAsync(code, codeVerifier, redirectUri, clientId, clientSecret);
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine("[GoogleAuth] 인증 시간 초과 또는 취소");
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GoogleAuth] 인증 실패: {ex.Message}");
            return false;
        }
    }

    /// <summary>Refresh Token으로 Access Token 갱신</summary>
    public async Task<bool> RefreshTokenAsync()
    {
        try
        {
            string refreshToken = Decrypt(Settings.GoogleRefreshToken.Value);
            string clientId = ClientId;
            string clientSecret = ClientSecret;

            if (string.IsNullOrEmpty(refreshToken) || string.IsNullOrEmpty(clientId))
                return false;

            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("client_id", clientId),
                new KeyValuePair<string, string>("client_secret", clientSecret),
                new KeyValuePair<string, string>("refresh_token", refreshToken),
                new KeyValuePair<string, string>("grant_type", "refresh_token")
            });

            var response = await _httpClient.PostAsync(TokenEndpoint, content);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Debug.WriteLine($"[GoogleAuth] 토큰 갱신 실패: {json}");
                return false;
            }

            var tokenResp = JsonSerializer.Deserialize(json, GoogleCalendarJsonContext.Default.GoogleTokenResponse);
            if (tokenResp == null) return false;

            // Access Token 저장 (Refresh Token은 갱신 응답에 포함되지 않을 수 있음)
            SaveAccessToken(tokenResp.AccessToken, tokenResp.ExpiresIn);
            Debug.WriteLine("[GoogleAuth] 토큰 갱신 완료");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GoogleAuth] 토큰 갱신 실패: {ex.Message}");
            return false;
        }
    }

    /// <summary>연결 해제 (토큰 revoke + 삭제)</summary>
    public async Task SignOutAsync()
    {
        try
        {
            string accessToken = Decrypt(Settings.GoogleAccessToken.Value);
            if (!string.IsNullOrEmpty(accessToken))
            {
                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("token", accessToken)
                });
                await _httpClient.PostAsync(RevokeEndpoint, content);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GoogleAuth] Revoke 실패 (무시): {ex.Message}");
        }

        // 토큰 삭제
        Settings.GoogleAccessToken.Set("");
        Settings.GoogleRefreshToken.Set("");
        Settings.GoogleTokenExpiry.Set("");
        Debug.WriteLine("[GoogleAuth] 로그아웃 완료");
    }

    #region Token Exchange

    private async Task<bool> ExchangeCodeForTokenAsync(
        string code, string codeVerifier, string redirectUri,
        string clientId, string clientSecret)
    {
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("code", code),
            new KeyValuePair<string, string>("client_id", clientId),
            new KeyValuePair<string, string>("client_secret", clientSecret),
            new KeyValuePair<string, string>("redirect_uri", redirectUri),
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("code_verifier", codeVerifier)
        });

        var response = await _httpClient.PostAsync(TokenEndpoint, content);
        var json = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            Debug.WriteLine($"[GoogleAuth] 토큰 교환 실패: {json}");
            return false;
        }

        var tokenResp = JsonSerializer.Deserialize(json, GoogleCalendarJsonContext.Default.GoogleTokenResponse);
        if (tokenResp == null) return false;

        // 토큰 저장
        SaveAccessToken(tokenResp.AccessToken, tokenResp.ExpiresIn);

        if (!string.IsNullOrEmpty(tokenResp.RefreshToken))
        {
            Settings.GoogleRefreshToken.Set(Encrypt(tokenResp.RefreshToken));
        }

        Debug.WriteLine("[GoogleAuth] 토큰 교환 완료");
        return true;
    }

    private static void SaveAccessToken(string accessToken, int expiresIn)
    {
        Settings.GoogleAccessToken.Set(Encrypt(accessToken));
        var expiry = DateTime.UtcNow.AddSeconds(expiresIn);
        Settings.GoogleTokenExpiry.Set(Encrypt(expiry.ToString("o")));
    }

    #endregion

    #region PKCE

    private static string GenerateCodeVerifier()
    {
        byte[] bytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Base64UrlEncode(bytes);
    }

    private static string GenerateCodeChallenge(string codeVerifier)
    {
        using var sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(Encoding.ASCII.GetBytes(codeVerifier));
        return Base64UrlEncode(hash);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    #endregion

    #region DPAPI 암호화

    private static string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return string.Empty;
        try
        {
            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
            byte[] encryptedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encryptedBytes);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GoogleAuth] 암호화 실패: {ex.Message}");
            return string.Empty;
        }
    }

    private static string Decrypt(string encryptedBase64)
    {
        if (string.IsNullOrEmpty(encryptedBase64)) return string.Empty;
        try
        {
            byte[] encryptedBytes = Convert.FromBase64String(encryptedBase64);
            byte[] plainBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GoogleAuth] 복호화 실패: {ex.Message}");
            return string.Empty;
        }
    }

    #endregion

    #region Helpers

    private static int FindAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    #endregion

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }
}

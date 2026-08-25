namespace NewSchool.Helpers;

/// <summary>
/// NEIS 오픈 API 의 결과 코드(<c>RESULT/CODE</c>) 판정. <b>한 벌</b>로 둔다.
///
/// <para>NEIS 는 실패도 HTTP 200 에 XML 로 실어 보내므로, 어느 화면이든 이 코드를 봐야
/// "자료가 없다" 와 "요청이 실패했다" 를 가를 수 있다. 예전에는 급식·학사일정·학교검색이
/// 각자 다른 규칙을 들고 있었고, 그중 급식은 <c>StartsWith("INFO")</c> 로 판정해
/// <b>인증키 오류(INFO-300)를 정상으로 통과</b>시켰다 — 결과가 0건이 되어 "오늘 급식 없음"
/// 으로 둔갑했다.</para>
/// </summary>
public static class NeisResult
{
    /// <summary>정상 응답. 코드가 아예 없는 응답(정상 데이터만 오는 경우)도 정상으로 본다.</summary>
    public const string Ok = "INFO-000";

    /// <summary>해당하는 데이터가 없음 — 오류가 아니라 "결과 0건" 이다.</summary>
    public const string NoData = "INFO-200";

    /// <summary>요청이 성립했는가(정상 또는 결과 0건).</summary>
    public static bool IsSuccess(string? code)
        => string.IsNullOrEmpty(code) || code == Ok || code == NoData;

    /// <summary>결과가 0건임을 알리는 코드인가.</summary>
    public static bool IsNoData(string? code) => code == NoData;

    /// <summary>
    /// 요청 자체가 실패했는가. 인증키 오류(INFO-300)·호출 한도 초과·서비스 점검 등이 여기 든다.
    /// </summary>
    public static bool IsError(string? code) => !IsSuccess(code);

    /// <summary>사용자에게 보일 실패 사유. MESSAGE 가 비면 코드라도 보여준다.</summary>
    public static string Describe(string? code, string? message)
        => string.IsNullOrWhiteSpace(message) ? (code ?? "알 수 없는 오류") : message;
}

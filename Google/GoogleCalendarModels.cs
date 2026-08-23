using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NewSchool.Google;

// ─────────────────────────────────────────────
// OAuth 토큰 응답
// ─────────────────────────────────────────────

public class GoogleTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    // token_type 은 읽는 곳이 없어 지웠다(39차). 응답 전용 DTO 라 필드를 받지 않을 뿐,
    // 요청 본문에는 영향이 없다(전송 직렬화 대상은 InsertRequest·Event 둘뿐).

    [JsonPropertyName("scope")]
    public string? Scope { get; set; }
}

// ─────────────────────────────────────────────
// Calendar List API
// ─────────────────────────────────────────────

public class GoogleCalendarListResponse
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonPropertyName("items")]
    public List<GoogleCalendarListEntry>? Items { get; set; }

    [JsonPropertyName("nextPageToken")]
    public string? NextPageToken { get; set; }
}

public class GoogleCalendarListEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("backgroundColor")]
    public string? BackgroundColor { get; set; }

    [JsonPropertyName("primary")]
    public bool? Primary { get; set; }

    // foregroundColor·accessRole·selected 는 읽는 곳이 없어 지웠다(39차).
    // 앱은 캘린더 목록에서 id·요약·배경색·primary 만 쓴다.
}

/// <summary>Google Calendar 생성 요청 (POST /calendars)</summary>
public class GoogleCalendarInsertRequest
{
    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("timeZone")]
    public string? TimeZone { get; set; }
}

/// <summary>Google Calendar 생성 응답</summary>
public class GoogleCalendarResource
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("timeZone")]
    public string? TimeZone { get; set; }
}

// ─────────────────────────────────────────────
// Events API
// ─────────────────────────────────────────────

public class GoogleEventsListResponse
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("items")]
    public List<GoogleEvent>? Items { get; set; }

    [JsonPropertyName("nextPageToken")]
    public string? NextPageToken { get; set; }

    [JsonPropertyName("nextSyncToken")]
    public string? NextSyncToken { get; set; }
}

public class GoogleEvent
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("location")]
    public string? Location { get; set; }

    [JsonPropertyName("colorId")]
    public string? ColorId { get; set; }

    [JsonPropertyName("start")]
    public GoogleEventDateTime? Start { get; set; }

    [JsonPropertyName("end")]
    public GoogleEventDateTime? End { get; set; }

    [JsonPropertyName("recurrence")]
    public List<string>? Recurrence { get; set; }

    [JsonPropertyName("updated")]
    public string? Updated { get; set; }

    [JsonPropertyName("created")]
    public string? Created { get; set; }

    [JsonPropertyName("extendedProperties")]
    public GoogleExtendedProperties? ExtendedProperties { get; set; }
}

public class GoogleEventDateTime
{
    /// <summary>종일 이벤트용 날짜 (yyyy-MM-dd)</summary>
    [JsonPropertyName("date")]
    public string? Date { get; set; }

    /// <summary>시간 이벤트용 일시 (RFC3339)</summary>
    [JsonPropertyName("dateTime")]
    public string? DateTime { get; set; }

    [JsonPropertyName("timeZone")]
    public string? TimeZone { get; set; }
}

public class GoogleExtendedProperties
{
    [JsonPropertyName("private")]
    public Dictionary<string, string>? Private { get; set; }
}

// ─────────────────────────────────────────────
// Error 응답
// ─────────────────────────────────────────────

public class GoogleErrorWrapper
{
    [JsonPropertyName("error")]
    public GoogleError? Error { get; set; }
}

public class GoogleError
{
    // code 는 읽는 곳이 없어 지웠다(39차) — 오류는 HTTP 상태와 message 로만 다룬다.

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("errors")]
    public List<GoogleErrorDetail>? Errors { get; set; }
}

public class GoogleErrorDetail
{
    // domain·reason 도 같은 이유로 지웠다(39차).

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

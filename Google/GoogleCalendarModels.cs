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

    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = "Bearer";

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

    [JsonPropertyName("foregroundColor")]
    public string? ForegroundColor { get; set; }

    [JsonPropertyName("accessRole")]
    public string? AccessRole { get; set; }

    [JsonPropertyName("primary")]
    public bool? Primary { get; set; }

    [JsonPropertyName("selected")]
    public bool? Selected { get; set; }
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
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("errors")]
    public List<GoogleErrorDetail>? Errors { get; set; }
}

public class GoogleErrorDetail
{
    [JsonPropertyName("domain")]
    public string? Domain { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

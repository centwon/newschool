using System.Text.Json.Serialization;

namespace NewSchool.Google;

/// <summary>
/// Native AOT 호환 JSON Serialization Context for Google Calendar API
/// System.Text.Json 소스 생성기를 통해 런타임 리플렉션 없이 직렬화/역직렬화 수행
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(GoogleTokenResponse))]
[JsonSerializable(typeof(GoogleCalendarListResponse))]
[JsonSerializable(typeof(GoogleCalendarListEntry))]
[JsonSerializable(typeof(GoogleCalendarInsertRequest))]
[JsonSerializable(typeof(GoogleCalendarResource))]
[JsonSerializable(typeof(GoogleEventsListResponse))]
[JsonSerializable(typeof(GoogleEvent))]
[JsonSerializable(typeof(GoogleExtendedProperties))]
[JsonSerializable(typeof(GoogleEventDateTime))]
[JsonSerializable(typeof(GoogleErrorWrapper))]
[JsonSerializable(typeof(GoogleError))]
[JsonSerializable(typeof(GoogleErrorDetail))]
internal partial class GoogleCalendarJsonContext : JsonSerializerContext
{
}

using System.Text.Json.Serialization;
using NewSchool.Models;

namespace NewSchool.Services;

/// <summary>
/// Native AOT 호환 JSON Serialization Context for SeatOptions.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(SeatOptions))]
[JsonSerializable(typeof(SeatOptions.PairRule))]
internal partial class SeatOptionsJsonContext : JsonSerializerContext
{
}

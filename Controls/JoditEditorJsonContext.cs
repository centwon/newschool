using System.Text.Json.Serialization;

namespace NewSchool.Controls;

/// <summary>
/// Native AOT 호환 JSON Serialization Context for JoditEditor
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(string))]
internal partial class JoditEditorJsonContext : JsonSerializerContext
{
}

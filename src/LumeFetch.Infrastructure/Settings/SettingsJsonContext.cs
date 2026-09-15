using System.Text.Json.Serialization;
using LumeFetch.Core.Settings;

namespace LumeFetch.Infrastructure.Settings;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;

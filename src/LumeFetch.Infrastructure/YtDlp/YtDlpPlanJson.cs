using System.Text.Json;
using System.Text.Json.Serialization;

namespace LumeFetch.Infrastructure.YtDlp;

/// <summary>Stable provider data without runtime reflection, including on trimmed/AOT hosts.</summary>
public static class YtDlpPlanJson
{
    public static string Serialize(YtDlpDownloadPlan plan) =>
        JsonSerializer.Serialize(plan, YtDlpJsonContext.Default.YtDlpDownloadPlan);

    public static YtDlpDownloadPlan Deserialize(string json) =>
        JsonSerializer.Deserialize(json, YtDlpJsonContext.Default.YtDlpDownloadPlan)
        ?? throw new InvalidOperationException("The selected format has an invalid download plan.");
}

[JsonSerializable(typeof(YtDlpDownloadPlan))]
internal sealed partial class YtDlpJsonContext : JsonSerializerContext;

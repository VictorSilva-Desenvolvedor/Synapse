using System.Text.Json.Serialization;

namespace Synapse.Core.Logging;

/// <summary>
/// Representa uma entrada completa de auditoria e log de atividades do Synapse,
/// registrando ações de usuário, cliques, perguntas, respostas da IA, latências e contexto.
/// </summary>
public sealed class SynapseActivityEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("localTime")]
    public string LocalTime => Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff");

    [JsonPropertyName("appVersion")]
    public string AppVersion { get; set; } = "1.0.0";

    [JsonPropertyName("component")]
    public string Component { get; set; } = "General";

    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("details")]
    public string? Details { get; set; }

    [JsonPropertyName("question")]
    public string? Question { get; set; }

    [JsonPropertyName("answer")]
    public string? Answer { get; set; }

    [JsonPropertyName("durationMs")]
    public long? DurationMs { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "Success";

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    [JsonPropertyName("affectedPath")]
    public string? AffectedPath { get; set; }

    [JsonIgnore]
    public bool IsTimeout => DurationMs >= 120_000 || Status.Equals("Timeout", StringComparison.OrdinalIgnoreCase);
}

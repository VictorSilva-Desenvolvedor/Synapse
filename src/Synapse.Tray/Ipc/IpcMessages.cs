using System.Text.Json;
using System.Text.Json.Serialization;

namespace Synapse.Tray.Ipc;

public sealed class IpcEnvelope
{
    [JsonPropertyName("versao")]
    public int Versao { get; set; } = 1;

    [JsonPropertyName("tipo")]
    public string Tipo { get; set; } = string.Empty;

    [JsonPropertyName("payload")]
    public JsonElement? Payload { get; set; }
}

public sealed class IpcStatusPayload
{
    [JsonPropertyName("estado")]
    public string Estado { get; set; } = "Sincronizado";

    [JsonPropertyName("pausado")]
    public bool Pausado { get; set; }

    [JsonPropertyName("ultimaSincronizacaoEm")]
    public DateTimeOffset? UltimaSincronizacaoEm { get; set; }

    [JsonPropertyName("itensPendentes")]
    public int ItensPendentes { get; set; }
}

public sealed class IpcConflictPayload
{
    [JsonPropertyName("caminho")]
    public string Caminho { get; set; } = string.Empty;
}

public sealed class IpcLogPathPayload
{
    [JsonPropertyName("caminho")]
    public string Caminho { get; set; } = string.Empty;
}

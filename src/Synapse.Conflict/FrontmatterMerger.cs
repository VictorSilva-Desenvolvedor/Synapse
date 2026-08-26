using Synapse.Core.Ports;
using YamlDotNet.Serialization;

namespace Synapse.Conflict;

/// <summary>
/// Merge de frontmatter YAML por chave (RF-CONFLICT.3), via ADR-016 (YamlDotNet). Recebe YAML puro, sem
/// os delimitadores "---" (que já foram removidos por quem monta a nota antes de chamar este merger).
/// Chave alterada só de um lado é aplicada; chave alterada dos dois lados com valores diferentes é
/// conflito de chave, mesmo que outras chaves tenham sido combinadas automaticamente.
/// </summary>
public sealed class FrontmatterMerger
{
    private readonly IDeserializer _deserializer = new DeserializerBuilder().Build();
    private readonly ISerializer _serializer = new SerializerBuilder().Build();

    public MergeResult Merge(string baseYaml, string localYaml, string remoteYaml)
    {
        var baseMap = Parse(baseYaml);
        var localMap = Parse(localYaml);
        var remoteMap = Parse(remoteYaml);

        var allKeys = baseMap.Keys.Concat(localMap.Keys).Concat(remoteMap.Keys).Distinct();
        var merged = new Dictionary<string, object>();

        foreach (var key in allKeys)
        {
            var baseHas = baseMap.TryGetValue(key, out var baseVal);
            var localHas = localMap.TryGetValue(key, out var localVal);
            var remoteHas = remoteMap.TryGetValue(key, out var remoteVal);

            var localChanged = HasChanged(baseHas, baseVal, localHas, localVal);
            var remoteChanged = HasChanged(baseHas, baseVal, remoteHas, remoteVal);

            if (localChanged && remoteChanged)
            {
                var mesmoResultado = localHas == remoteHas && (!localHas || ValuesEqual(localVal!, remoteVal!));
                if (!mesmoResultado)
                    return new MergeResult.Unresolvable(localYaml, remoteYaml);

                if (localHas) merged[key] = localVal!;
                continue;
            }

            if (localChanged)
            {
                if (localHas) merged[key] = localVal!;
                continue;
            }

            if (remoteChanged)
            {
                if (remoteHas) merged[key] = remoteVal!;
                continue;
            }

            if (baseHas) merged[key] = baseVal!;
        }

        return new MergeResult.Resolved(_serializer.Serialize(merged));
    }

    private Dictionary<string, object> Parse(string yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
            return new Dictionary<string, object>();

        return _deserializer.Deserialize<Dictionary<string, object>>(yaml) ?? new Dictionary<string, object>();
    }

    private bool HasChanged(bool baseHas, object? baseVal, bool sideHas, object? sideVal) =>
        baseHas != sideHas || (baseHas && sideHas && !ValuesEqual(baseVal!, sideVal!));

    // Compara pelo YAML canônico em vez de escrever um comparador recursivo próprio para grafos de
    // objeto arbitrários (listas, mapas aninhados) que o YamlDotNet já produz na desserialização.
    private bool ValuesEqual(object a, object b) =>
        _serializer.Serialize(a) == _serializer.Serialize(b);
}

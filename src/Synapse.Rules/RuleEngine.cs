using System.Globalization;
using System.Text.RegularExpressions;
using Synapse.Core.Ports;
using YamlDotNet.Serialization;

namespace Synapse.Rules;

/// <summary>
/// Motor de automação (RF-RULES.1-5). LoadRulesAsync (re)carrega as regras de um arquivo YAML,
/// substituindo as anteriores - suporta o recarregamento "sem reiniciar o serviço" de RF-RULES.1 (o
/// watcher dedicado ao arquivo de regras que dispara esse recarregamento é responsabilidade de
/// composição do Synapse.Host, não deste motor). EvaluateAsync nunca produz uma ação de exclusão -
/// garantia do próprio tipo RuleAction (RF-RULES.5), não uma convenção deste código.
/// </summary>
public sealed class RuleEngine : IRuleEngine
{
    private readonly IFileSystem _fileSystem;
    private readonly string _vaultRootPath;
    private readonly TimeProvider _timeProvider;
    private readonly IDeserializer _frontmatterDeserializer = new DeserializerBuilder().Build();
    private IReadOnlyList<RuleDefinition> _rules = [];

    public RuleEngine(IFileSystem fileSystem, string vaultRootPath, TimeProvider? timeProvider = null)
    {
        _fileSystem = fileSystem;
        _vaultRootPath = vaultRootPath;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task LoadRulesAsync(string rulesFilePath, CancellationToken ct)
    {
        var yaml = await _fileSystem.ReadAllTextAsync(rulesFilePath, ct);
        _rules = RulesYamlParser.Parse(yaml);
    }

    public async Task<IReadOnlyList<RuleAction>> EvaluateAsync(NoteContext note, CancellationToken ct)
    {
        var acoes = new List<RuleAction>();

        foreach (var regra in _rules)
        {
            var acao = regra switch
            {
                RuleDefinition.NotaDiaria notaDiaria => await AvaliarNotaDiariaAsync(notaDiaria, ct),
                RuleDefinition.AutoTag autoTag => AvaliarAutoTag(autoTag, note),
                RuleDefinition.MoverPorStatus moverPorStatus => AvaliarMoverPorStatus(moverPorStatus, note),
                _ => null,
            };

            if (acao is not null)
                acoes.Add(acao);
        }

        return acoes;
    }

    // RF-RULES.2: nao depende da nota que disparou a avaliacao, e sim do dia atual - dedup via
    // existencia do arquivo no disco (nao um estado em memoria), para nao recriar a nota diaria depois
    // de um reinicio do servico no mesmo dia em que ela ja foi criada.
    private async Task<RuleAction?> AvaliarNotaDiariaAsync(RuleDefinition.NotaDiaria regra, CancellationToken ct)
    {
        var agora = _timeProvider.GetUtcNow();
        var caminhoResolvido = ResolverTemplateDeData(regra.Caminho, agora);
        var caminhoCompleto = Path.Combine(_vaultRootPath, caminhoResolvido);

        if (await _fileSystem.ExistsAsync(caminhoCompleto, ct))
            return null;

        return new RuleAction.CreateNote(caminhoResolvido, regra.Template);
    }

    private static RuleAction? AvaliarAutoTag(RuleDefinition.AutoTag regra, NoteContext note)
    {
        var pastaOrigem = NormalizarPastaComBarraFinal(regra.PastaOrigem);
        var caminhoNota = note.RelativePath.Replace('\\', '/');

        return caminhoNota.StartsWith(pastaOrigem, StringComparison.OrdinalIgnoreCase)
            ? new RuleAction.AddTags(note.RelativePath, regra.Tags)
            : null;
    }

    private RuleAction? AvaliarMoverPorStatus(RuleDefinition.MoverPorStatus regra, NoteContext note)
    {
        if (!FrontmatterTemValor(note.FrontmatterYaml, regra.CampoFrontmatter, regra.Valor))
            return null;

        var destino = $"{regra.PastaDestino.TrimEnd('/')}/{Path.GetFileName(note.RelativePath)}";
        return new RuleAction.MoveNote(note.RelativePath, destino);
    }

    private bool FrontmatterTemValor(string frontmatterYaml, string campo, string valorEsperado)
    {
        if (string.IsNullOrWhiteSpace(frontmatterYaml))
            return false;

        var mapa = _frontmatterDeserializer.Deserialize<Dictionary<string, object>>(frontmatterYaml);
        return mapa is not null
            && mapa.TryGetValue(campo, out var valor)
            && string.Equals(valor?.ToString(), valorEsperado, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolverTemplateDeData(string caminho, DateTimeOffset agora) =>
        Regex.Replace(caminho, @"\{\{data:([^}]+)\}\}", m => agora.ToString(m.Groups[1].Value, CultureInfo.InvariantCulture));

    private static string NormalizarPastaComBarraFinal(string caminho) => caminho.Replace('\\', '/').TrimEnd('/') + "/";
}

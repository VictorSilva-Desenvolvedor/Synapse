using Synapse.Sync.Diagnostics;
using Xunit;

namespace Synapse.Tests.Sync;

/// <summary>
/// O resolvedor precisa casar com o layout que o SyncQueueProcessor grava de verdade:
/// _conflitos/{caminho/da/nota.md}/local-{ts}.md e remoto-{ts}.md.
/// A versao anterior da tela procurava "Nota.conflito-{ts}.md", formato que o gravador
/// nunca produziu — entao ela nunca achava nada.
/// </summary>
public sealed class ConflictSetResolverTests : IDisposable
{
    private readonly string _vault;
    private readonly string _baseCache;

    public ConflictSetResolverTests()
    {
        _vault = Path.Combine(Path.GetTempPath(), "SynapseConflictTests_" + Guid.NewGuid().ToString("N")[..8]);
        _baseCache = Path.Combine(_vault, "__base_cache");
        Directory.CreateDirectory(_vault);
        Directory.CreateDirectory(_baseCache);
    }

    private string CriarConflito(string notaRelativa, string local, string remoto, string? baseContent = null)
    {
        var dir = Path.Combine(_vault, "_conflitos", notaRelativa);
        Directory.CreateDirectory(dir);

        var localPath = Path.Combine(dir, "local-20260828-203100.md");
        var remotePath = Path.Combine(dir, "remoto-20260828-203100.md");
        File.WriteAllText(localPath, local);
        File.WriteAllText(remotePath, remoto);

        if (baseContent is not null)
        {
            var basePath = Path.Combine(_baseCache, notaRelativa);
            Directory.CreateDirectory(Path.GetDirectoryName(basePath)!);
            File.WriteAllText(basePath, baseContent);
        }

        return localPath;
    }

    [Fact]
    public void Resolve_AchaAsTresVersoes()
    {
        var clicado = CriarConflito("Notas/Diario.md", "versao local", "versao remota", "versao base");

        var set = ConflictSetResolver.Resolve(_vault, clicado, _baseCache);

        Assert.NotNull(set);
        Assert.Equal("Notas/Diario.md", set!.TargetRelativePath);
        Assert.Equal("versao local", File.ReadAllText(set.LocalPath));
        Assert.Equal("versao remota", File.ReadAllText(set.RemotePath));
        Assert.NotNull(set.BasePath);
        Assert.Equal("versao base", File.ReadAllText(set.BasePath!));
    }

    [Fact]
    public void Resolve_FuncionaClicandoNoRemoto()
    {
        var local = CriarConflito("Notas/Diario.md", "versao local", "versao remota");
        var remoto = Path.Combine(Path.GetDirectoryName(local)!, "remoto-20260828-203100.md");

        var set = ConflictSetResolver.Resolve(_vault, remoto, _baseCache);

        // Clicar no remoto nao pode inverter os lados.
        Assert.NotNull(set);
        Assert.Equal("versao local", File.ReadAllText(set!.LocalPath));
        Assert.Equal("versao remota", File.ReadAllText(set.RemotePath));
    }

    [Fact]
    public void Resolve_SemBaseNoCache_DevolveBaseNula()
    {
        var clicado = CriarConflito("Notas/Nova.md", "local", "remoto");

        var set = ConflictSetResolver.Resolve(_vault, clicado, _baseCache);

        // Conflito no primeiro sync da nota: nao ha base, e isso e legitimo.
        Assert.NotNull(set);
        Assert.Null(set!.BasePath);
    }

    [Fact]
    public void Resolve_ForaDaPastaDeConflitos_DevolveNull()
    {
        var solto = Path.Combine(_vault, "Notas", "Solta.md");
        Directory.CreateDirectory(Path.GetDirectoryName(solto)!);
        File.WriteAllText(solto, "conteudo");

        Assert.Null(ConflictSetResolver.Resolve(_vault, solto, _baseCache));
    }

    [Fact]
    public void Resolve_EscolheARodadaMaisRecente()
    {
        var clicado = CriarConflito("Notas/Diario.md", "local antigo", "remoto antigo");
        var dir = Path.GetDirectoryName(clicado)!;

        var novoLocal = Path.Combine(dir, "local-20260829-090000.md");
        var novoRemoto = Path.Combine(dir, "remoto-20260829-090000.md");
        File.WriteAllText(novoLocal, "local novo");
        File.WriteAllText(novoRemoto, "remoto novo");

        var agora = DateTime.UtcNow;
        File.SetLastWriteTimeUtc(novoLocal, agora);
        File.SetLastWriteTimeUtc(novoRemoto, agora);
        File.SetLastWriteTimeUtc(clicado, agora.AddHours(-2));
        File.SetLastWriteTimeUtc(Path.Combine(dir, "remoto-20260828-203100.md"), agora.AddHours(-2));

        var set = ConflictSetResolver.Resolve(_vault, clicado, _baseCache);

        Assert.Equal("local novo", File.ReadAllText(set!.LocalPath));
        Assert.Equal("remoto novo", File.ReadAllText(set.RemotePath));
    }

    public void Dispose()
    {
        try { Directory.Delete(_vault, true); } catch { /* limpeza best-effort */ }
    }
}

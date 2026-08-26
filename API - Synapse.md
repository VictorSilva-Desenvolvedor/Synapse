# Especificação de API — Synapse

*Versão 1.1 · 26/08/2026*
*Baseado em: `SAD - Synapse.md` (v1.0, seção 3 — Visão Lógica) e `SRS - Synapse.md` (v1.0, seção 3.2.3)*

---

## 1. Introdução e Escopo

O Synapse tem três superfícies de API distintas, e este documento especifica as duas que ainda não tinham assinatura formal em nenhum documento anterior:

1. **API interna (portas do domínio)** — as interfaces C# que a arquitetura hexagonal (SAD, ADR-001) já previa por nome, mas sem assinatura completa. É a API entre `Synapse.Core` e todo o resto.
2. **API de IPC local (Bandeja ↔ Serviço)** — **lacuna real identificada agora**: o Windows isola serviços (Session 0) da sessão interativa do usuário, então o ícone de bandeja (RF-UX.1) não pode rodar dentro do próprio Windows Service — precisa ser um processo separado, e os dois processos precisam de um protocolo para conversar. Nenhum documento anterior havia especificado isso.
3. **API externa consumida (Google Drive API)** — já especificada em `SRS - Synapse.md`, seção 3.2.3. Este documento só referencia, não duplica.

### 1.1 Convenções adotadas

- Todo método de I/O é assíncrono (`Task`/`Task<T>`), com `CancellationToken` como último parâmetro — permite cancelamento cooperativo em qualquer chamada de rede ou disco.
- **Resultados esperados** (ex.: "o merge não deu para resolver automaticamente") usam tipos de retorno discriminados (`record` com casos), não exceções — exceção é só para o inesperado.
- **Falhas de infraestrutura** (rede, cota, autenticação) usam exceções tipadas específicas, porque o `SyncQueueProcessor` já precisa distinguir esses casos para aplicar o backoff certo (RF-SYNC.6) — exceção aqui é a ferramenta certa, não anti-padrão.
- Tipos anuláveis (`string?`) são explícitos — nada de `null` implícito sem aviso no contrato.

---

## 2. API Interna — Portas do Domínio (`Synapse.Core`)

### 2.1 `ICloudProvider`

Abstrai o provedor de nuvem (RNF-6). Implementação da v1: `GoogleDriveProvider` em `Synapse.Sync`.

```csharp
namespace Synapse.Core.Ports;

public interface ICloudProvider
{
    Task<CloudFile> UploadAsync(string localPath, string remoteFolderId, CancellationToken ct);

    Task<CloudFile> UpdateAsync(string cloudFileId, string localPath, CancellationToken ct);

    Task DownloadAsync(string cloudFileId, string destinationPath, CancellationToken ct);

    Task DeleteAsync(string cloudFileId, CancellationToken ct);

    Task<CloudFile> GetMetadataAsync(string cloudFileId, CancellationToken ct);

    Task<string> GetStartPageTokenAsync(CancellationToken ct);

    Task<ChangesPage> GetChangesAsync(string pageToken, CancellationToken ct);
}

public sealed record CloudFile(
    string Id,
    string Name,
    string Md5Checksum,
    DateTimeOffset ModifiedTime,
    bool Trashed);

public sealed record ChangesPage(
    IReadOnlyList<CloudFile> ChangedFiles,
    string? NextPageToken,
    string? NewStartPageToken);
```

**Contrato de exceções** (todas em `Synapse.Core.Ports`, para o `SyncQueueProcessor` capturar por tipo, sem depender do SDK do Google):

| Exceção | Quando | Tratamento esperado (RF-SYNC.6) |
|---|---|---|
| `CloudAuthExpiredException` | Token expirado/revogado (401) | Dispara renovação (RF-AUTH.3); se falhar, estado `AuthRequired` |
| `CloudQuotaExceededException` | Cota excedida (403/429) | Backoff exponencial com jitter |
| `CloudTransientException` | Erro 5xx / timeout de rede | Backoff exponencial com jitter |
| `CloudNotFoundException` | Arquivo remoto não existe mais (404) | Tratado como exclusão remota (ver SRS, matriz de erros) |

**Pré-condições:** `localPath` deve existir e ser legível no momento da chamada; `cloudFileId` deve ser um ID previamente retornado por este mesmo provider (não é permitido inventar IDs).
**Pós-condição de `UploadAsync`/`UpdateAsync`:** o `CloudFile` retornado reflete o estado *confirmado* no Drive (não uma estimativa local) — é isso que o chamador grava no índice.

### 2.2 `ISyncIndexStore`

Abstrai a persistência local (RF-SYNC.3/4, esquema em `SRS - Synapse.md` seção 3.3). Implementação da v1: repositório SQLite em `Synapse.Data`.

```csharp
namespace Synapse.Core.Ports;

public interface ISyncIndexStore
{
    Task<SyncedFileRecord?> FindByLocalPathAsync(string localPath, CancellationToken ct);
    Task<SyncedFileRecord?> FindByCloudFileIdAsync(string cloudFileId, CancellationToken ct);
    Task UpsertAsync(SyncedFileRecord record, CancellationToken ct);
    Task RemoveAsync(string localPath, CancellationToken ct);

    Task EnqueueAsync(SyncQueueItem item, CancellationToken ct);
    Task<SyncQueueItem?> PeekNextAsync(CancellationToken ct);
    Task MarkDoneAsync(long queueItemId, CancellationToken ct);
    Task MarkFailedAsync(long queueItemId, string error, CancellationToken ct);

    Task<string?> GetChangesPageTokenAsync(CancellationToken ct);
    Task SaveChangesPageTokenAsync(string pageToken, CancellationToken ct);

    Task RecordConflictAsync(ConflictRecord record, CancellationToken ct);
}

public sealed record SyncedFileRecord(
    long Id,
    string LocalPath,
    string? CloudFileId,
    string ContentHash,
    DateTimeOffset LocalMtime,
    DateTimeOffset? CloudModifiedTime,
    DateTimeOffset LastSyncedAt,
    SyncStatus Status);

public enum SyncStatus { Synced, PendingUpload, PendingDownload, Conflict, Failed }

public sealed record SyncQueueItem(
    long Id,
    string FilePath,
    SyncEventType EventType,
    DateTimeOffset EnqueuedAt,
    int Attempts,
    string? LastError);

public enum SyncEventType { Created, Modified, Deleted, Renamed }

public sealed record ConflictRecord(
    string FilePath,
    string LocalVersionPath,
    string RemoteVersionPath,
    DateTimeOffset DetectedAt);
```

**Contrato:** `PeekNextAsync` não remove o item da fila — só `MarkDoneAsync`/`MarkFailedAsync` fazem isso, garantindo que uma queda do processo entre o `Peek` e o `MarkDone` deixe o item pendente para a próxima execução (é o mecanismo que sustenta RF-SYNC.4).

### 2.3 `IConflictResolver`

Algoritmo de merge de 3 vias (RF-CONFLICT.1–4), puro e sem I/O — testável com strings em memória.

```csharp
namespace Synapse.Core.Ports;

public interface IConflictResolver
{
    MergeResult TryMergeBody(string baseContent, string localContent, string remoteContent);
    MergeResult TryMergeFrontmatter(string baseYaml, string localYaml, string remoteYaml);
}

public abstract record MergeResult
{
    public sealed record Resolved(string MergedContent) : MergeResult;
    public sealed record Unresolvable(string LocalContent, string RemoteContent) : MergeResult;

    private MergeResult() { }
}
```

Nota de design: `TryMergeBody`/`TryMergeFrontmatter` são **síncronos** (sem `Task`) de propósito — são funções puras (string → resultado), não fazem I/O. Isso simplifica os testes unitários exigidos pela DoD do Backlog (nenhum `await` necessário no teste).

### 2.4 `IRuleEngine`

Motor de automação (RF-RULES.1–5).

```csharp
namespace Synapse.Core.Ports;

public interface IRuleEngine
{
    Task LoadRulesAsync(string rulesFilePath, CancellationToken ct);
    Task<IReadOnlyList<RuleAction>> EvaluateAsync(NoteContext note, CancellationToken ct);
}

public sealed record NoteContext(
    string RelativePath,
    string FrontmatterYaml,
    DateTimeOffset CreatedAt);

public abstract record RuleAction
{
    public sealed record CreateNote(string TargetPath, string TemplatePath) : RuleAction;
    public sealed record AddTags(string TargetPath, IReadOnlyList<string> Tags) : RuleAction;
    public sealed record MoveNote(string FromPath, string ToPath) : RuleAction;

    private RuleAction() { }
}
```

**Contrato de segurança (RF-RULES.5):** `RuleAction` deliberadamente **não tem** um caso `DeleteNote`. Nenhuma regra pode apagar conteúdo — isso não é uma convenção documental, é uma restrição no próprio tipo (impossível de violar sem alterar este contrato conscientemente).

### 2.5 `IVaultWatcher`

Abstrai o monitoramento do sistema de arquivos (RF-SYNC.1) — porta nova em relação ao SAD, adicionada aqui porque sem ela `Synapse.Sync` dependeria diretamente de `System.IO.FileSystemWatcher`, dificultando testar o `SyncQueueProcessor` sem um disco real.

```csharp
namespace Synapse.Core.Ports;

public interface IVaultWatcher : IDisposable
{
    event EventHandler<VaultChangeEvent>? Changed;
    void Start(string vaultRootPath);
    void Stop();
}

public sealed record VaultChangeEvent(string RelativePath, SyncEventType EventType);
```

### 2.6 `IFileSystem`

Abstrai o sistema de arquivos local — porta nova em relação à v1.0 desta API, pelo mesmo motivo da seção 2.5: sem ela, `SyncQueueProcessor` (que lê/escreve o conteúdo das notas, calcula hash e cacheia o conteúdo-base do merge de 3 vias) dependeria diretamente de `System.IO`, dificultando testar sem disco real (`Plano de Testes - Synapse.md` seção 4 já previa "`IFileSystem` em memória" para os testes de sistema simulados, mas a porta em si nunca tinha sido formalizada em código).

```csharp
namespace Synapse.Core.Ports;

public interface IFileSystem
{
    Task<bool> ExistsAsync(string path, CancellationToken ct);
    Task<string> ReadAllTextAsync(string path, CancellationToken ct);
    Task WriteAllTextAsync(string path, string content, CancellationToken ct);
    Task DeleteAsync(string path, CancellationToken ct);
}
```

Implementação da v1: `LocalFileSystem` (`Synapse.Sync`, sobre `System.IO`). Dublê de teste: `InMemoryFileSystem` (`Synapse.Tests`).

---

## 3. API de IPC Local — Bandeja ↔ Serviço

### 3.1 Por que essa API precisa existir

O Windows executa serviços na Session 0, sem acesso à área de notificação (bandeja) da sessão interativa do usuário desde o Windows Vista. Como RF-UX.1/UX.2 exigem ícone de bandeja com status e controle de pausa/retomada, **a bandeja precisa ser um processo separado** (`Synapse.Tray`, rodando na sessão do usuário) que conversa com o Windows Service (`Synapse.Host`, rodando na Session 0) por algum canal local. Nenhum documento anterior (PRD, SRS, SAD) havia especificado esse canal — é a lacuna que esta seção fecha.

### 3.2 Transporte

**Decisão: Named Pipe local** (`System.IO.Pipes`, nome fixo `\\.\pipe\synapse-ipc`), não HTTP loopback nem gRPC. Ver **ADR-010** (adicionada a `ADR - Synapse.md` nesta revisão) para o raciocínio completo — resumo: é a opção mais simples (KISS) para comunicação 1-para-N local, não expõe nenhuma porta de rede (superfície de ataque menor que HTTP loopback), e é nativa do .NET sem dependência nova.

### 3.3 Formato de mensagem

Envelope único em JSON (via `System.Text.Json`) para todas as mensagens, nos dois sentidos:

```json
{
  "versao": 1,
  "tipo": "ComandoOuEvento",
  "payload": { }
}
```

O campo `versao` existe para o caso (raro, mas possível) de a bandeja e o serviço ficarem em versões diferentes do Synapse após uma atualização parcial — uma mensagem com `versao` desconhecida é ignorada com um log de aviso, não derruba a conexão.

### 3.4 Comandos (Bandeja → Serviço)

| `tipo` | Payload | Resposta esperada |
|---|---|---|
| `GetStatus` | `{}` | `StatusChanged` (ver 3.5) imediato |
| `Pause` | `{}` | `StatusChanged` com `pausado: true` |
| `Resume` | `{}` | `StatusChanged` com `pausado: false` |
| `Reconnect` | `{}` | Dispara novo fluxo OAuth (RF-AUTH.4); `StatusChanged` ao concluir |
| `GetLogPath` | `{}` | `{ "tipo": "LogPath", "payload": { "caminho": "..." } }` |

### 3.5 Eventos (Serviço → Bandeja)

| `tipo` | Payload | Quando é emitido |
|---|---|---|
| `StatusChanged` | `{ "estado": "Sincronizado\|Sincronizando\|Offline\|Erro\|AuthRequired", "pausado": bool, "ultimaSincronizacaoEm": "ISO8601", "itensPendentes": int }` | A cada transição de estado da máquina de estados (SRS, seção 3.7) |
| `ConflitoDetectado` | `{ "caminho": "..." }` | Ao gravar arquivos em `_conflitos/` (RF-CONFLICT.4/5) |

### 3.6 Sequência de conexão

1. `Synapse.Tray` inicia e tenta conectar ao pipe.
2. Se o serviço não estiver disponível (pipe inexistente): bandeja mostra ícone "desconectado" e tenta reconectar a cada 5s — **nunca falha silenciosamente nem trava a inicialização da bandeja**.
3. Ao conectar: bandeja envia `GetStatus` e passa a escutar eventos `StatusChanged` publicados pelo serviço continuamente na mesma conexão (não é preciso repetir `GetStatus` em loop — o serviço empurra a mudança de estado assim que ela ocorre).

### 3.7 Segurança

O `NamedPipeServerStream` do .NET, por padrão, já restringe a conexão ao mesmo usuário do Windows que criou o pipe — nenhuma configuração adicional de ACL é necessária para o caso de uso (usuário único, mesma máquina). Sem exposição de rede: o pipe não é acessível fora da máquina local, atendendo RNF-4 (segurança) sem introduzir superfície nova.

---

## 4. API Externa Consumida — Google Drive API (referência)

Especificação completa em `SRS - Synapse.md`, seção 3.2.3 e RF-SYNC.1–7. Resumo de referência rápida:

| Endpoint | Uso no Synapse |
|---|---|
| `files.create` | Upload de arquivo novo (via `ICloudProvider.UploadAsync`) |
| `files.update` | Atualização de conteúdo existente (via `UpdateAsync`) |
| `files.get` | Metadados de um arquivo específico (via `GetMetadataAsync`) |
| `files.delete` | Exclusão remota, quando configurada para propagar (RF-SYNC) |
| `changes.list` | Detecção incremental de mudanças remotas (via `GetChangesAsync`) |
| `changes.getStartPageToken` | Obtenção do cursor inicial (via `GetStartPageTokenAsync`) |

Não repetido aqui: parâmetros de cota, campos restritos via `fields`, e política de retry — ver SRS.

---

## 5. Rastreabilidade

| Elemento de API | RF-x relacionado | Documento de origem |
|---|---|---|
| `ICloudProvider` | RF-SYNC.2, RF-SYNC.6 | PRD (interface citada), SAD (ADR-001, ADR-006) |
| `ISyncIndexStore` | RF-SYNC.3, RF-SYNC.4 | SRS seção 3.3 (esquema de dados) |
| `IConflictResolver` | RF-CONFLICT.1–4 | SRS seção 3.7 (máquina de estados) |
| `IRuleEngine` | RF-RULES.1–5 | Backlog EP-04 |
| `IVaultWatcher` | RF-SYNC.1 | SRS seção 3.8 (limitação do FileSystemWatcher) |
| `IFileSystem` | RF-SYNC.3, RF-CONFLICT.1–2 | Plano de Testes seção 4 ("IFileSystem em memória") — lacuna fechada por esta revisão |
| IPC Bandeja↔Serviço | RF-UX.1, RF-UX.2, RF-UX.4 | Nenhum — lacuna fechada por este documento |

---

*Esta especificação deve ser tratada como o contrato formal entre módulos: qualquer mudança de assinatura aqui é uma mudança que potencialmente quebra testes unitários já escritos contra essas interfaces (a própria razão de existirem) — mudar com intenção, não incidentalmente.*

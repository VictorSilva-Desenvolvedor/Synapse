# Visão de Produto — Synapse

*Versão 2.1 · 26/08/2026 · Nome confirmado pelo usuário*

> **Nome do projeto:** **Synapse** — confirmado em 26/08/2026. Namespace sugerido: `Synapse.Core`, `Synapse.Sync`, `Synapse.Rules`. **Atenção:** há colisão de nome com dois projetos conhecidos — o *Matrix Synapse* (homeserver open-source de referência do protocolo Matrix) e o *Azure Synapse Analytics* (produto de dados da Microsoft). Isso reduz a achabilidade em buscas/GitHub, especialmente por ser um projeto .NET (risco de confusão com Azure). Decisão consciente do usuário, mantida apesar do risco — ver linha correspondente em Riscos e Mitigações.

---

## O que mudou em relação ao rascunho original

Antes de entrar nas seções, um resumo das alterações de fundo — o rascunho original já estava bem estruturado, então o trabalho aqui foi de correção técnica, validação de mercado e preenchimento de lacunas de produto:

- **Correção terminológica:** não existe uma "API do Google One". O Google One é o plano de armazenamento (o que dá o espaço em disco); a integração é feita com a **Google Drive API**, que é gratuita para uso padrão e tem cotas públicas e documentadas. Isso muda como o pitch deve ser escrito para não soar tecnicamente impreciso a um revisor técnico.
- **Validação de mercado com dados atuais (ago/2026):** o Obsidian Sync custa **US$ 4/mês (anual) ou US$ 5/mês (mensal)** por usuário. Mais importante: o **Remotely Save**, principal plugin gratuito de sync de terceiros, **tornou pagas as integrações com Google Drive, OneDrive e Box em janeiro/2026**, limitando o plano gratuito a 5GB. Isso é uma validação de mercado forte e recente para o problema que o projeto ataca — e deveria estar explícito na visão, não só implícito.
- **Seção nova: Panorama Competitivo.** O rascunho original citava só o Obsidian Sync. Faltava posicionar o produto contra Syncthing, Self-hosted LiveSync, Google Drive Sync (plugin comunitário gratuito concorrente direto) e o próprio Google Drive Desktop puro.
- **Arquitetura técnica mais concreta.** "Motor em C#" e "resolução inteligente de conflitos" eram afirmações de alto nível sem mecanismo. Adicionei um desenho de componentes, a estratégia de detecção de mudanças (Changes API em vez de polling ingênuo), o algoritmo de merge de 3 vias para Markdown/frontmatter, e o tratamento de cotas.
- **Correção de uma promessa exagerada:** "privacidade total" e "à prova de balas" eram fortes demais. Dados sincronizados pelo Google Drive residem nos servidores do Google — são local-first e livres de vendor lock-in de formato, mas não são criptografados de ponta a ponta por padrão. Reposicionei isso como diferencial real (propriedade dos dados, formato aberto) e como item de roadmap (criptografia opcional).
- **Seções novas de produto que estavam ausentes:** Riscos e Mitigações, Métricas de Sucesso, Roadmap faseado (MVP → V1 → V2) e uma nota de sustentabilidade/monetização — itens padrão de uma visão de produto que o rascunho não cobria.

---

## 1. Declaração de Visão (Elevator Pitch)

Para trabalhadores do conhecimento, estudantes e desenvolvedores que constroem seu Segundo Cérebro no Obsidian gratuito e precisam acessá-lo de qualquer dispositivo sem pagar por sincronização, o **Synapse** é um serviço de sincronização e automação em C# que transforma um cofre (vault) local do Obsidian em um sistema conectado, resiliente e auditável, usando o armazenamento do Google Drive que o usuário já possui via Google One.

Diferente do Obsidian Sync (assinatura paga) e do Remotely Save (que descontinuou o plano gratuito para Google Drive/OneDrive/Box em janeiro de 2026), o Synapse entrega sincronização bidirecional, resolução de conflitos com merge de 3 vias ciente de Markdown/frontmatter, e um motor de automação de notas — tudo sem taxa de assinatura própria, rodando sobre a cota gratuita da Google Drive API.

## 2. O Problema

O Obsidian gratuito é uma das melhores ferramentas para construir um Segundo Cérebro (PKM), por manter os dados localmente em Markdown puro. Mas usuários enfrentam três gargalos reais e, em 2026, dois deles pioraram:

- **Sincronização paga ou instável.** O Obsidian Sync custa US$ 4–5/mês. As alternativas gratuitas históricas têm limitações conhecidas: o **Git + plugin Obsidian Git** é instável em mobile (reimplementação de git em JS) e tem limite prático de tamanho de cofre no celular; o **iCloud** funciona só no ecossistema Apple e tem relatos de corrupção/duplicação no Windows; o **Syncthing** é P2P e sólido, mas não roda em iPad e não oferece backup em nuvem central (se todos os dispositivos falharem ao mesmo tempo, não há cópia externa).
- **O mercado de plugins gratuitos está encolhendo.** Em janeiro de 2026, o **Remotely Save** — que era a ponte gratuita mais popular entre Obsidian e Google Drive/OneDrive/Box — passou a cobrar por essas integrações, deixando o plano gratuito limitado a 5GB. Isso deixou um vácuo real: existe um plugin comunitário alternativo ("Google Drive Sync") que ainda é gratuito, mas depende de manutenção voluntária e não tem motor de automação nenhum. Ou seja, o próprio mercado acabou de confirmar a tese do produto.
- **Falta de automação externa.** Organizar arquivos, processar metadados em lote e fazer backups versionados exige trabalho manual pesado — nenhuma das opções gratuitas acima (Syncthing, iCloud, Git puro) tem um motor de regras embutido.

## 3. Panorama Competitivo

| Solução | Custo | Ponto forte | Limitação relevante |
|---|---|---|---|
| **Obsidian Sync** (oficial) | US$ 4–5/mês | E2E encryption, histórico de versões, suporte oficial | Assinatura recorrente; sem automação |
| **Remotely Save** (após jan/2026) | Grátis até 5GB; GDrive/OneDrive/Box pagos | Multi-provedor (S3, WebDAV etc.) | Perdeu a integração gratuita com Google Drive |
| **Google Drive Sync** (plugin comunitário) | Grátis | Usa API oficial do Google, escopo não sensível | Mantido por voluntários; sem motor de regras/automação; sem resolução de conflito avançada |
| **Syncthing** | Grátis | P2P real, sem servidor central, robusto | Sem iPad; sem backup em nuvem central; configuração mais técnica |
| **Self-hosted LiveSync** | Grátis (mas exige servidor) | Resolução de conflito madura (merge de 3 vias + fallback por timestamp, via CouchDB) | Exige hospedar e manter um servidor CouchDB — barreira técnica alta |
| **Google Drive Desktop (puro)** | Grátis | Simples | Não entende Markdown; cria arquivos "conflicted copy" duplicados; zero automação |

**Onde o Synapse se encaixa:** é o único ponto do quadro que combina *zero servidor para manter* (ao contrário do LiveSync) + *cobertura de nuvem real* (ao contrário do Syncthing puro) + *motor de automação nativo* (ausente em todos os concorrentes gratuitos) + *resolução de conflito no nível do LiveSync* sem exigir CouchDB.

## 4. A Solução

Um serviço em background (Windows Service / .NET Worker Service, com uma bandeja de sistema para status e controles) que atua como ponte inteligente entre o cofre local do Obsidian e o Google Drive.

### 4.1 Componentes

- **Watcher local:** `FileSystemWatcher` com debounce (para não disparar eventos duplicados em salvamentos rápidos do Obsidian) e uma fila de eventos persistida em disco (para sobreviver a quedas do serviço).
- **Índice de sincronização local:** um banco leve (SQLite/LiteDB) guardando hash de conteúdo, `mtime` e o `revisionId`/`md5Checksum` do Drive por arquivo. Isso evita re-verificar o cofre inteiro a cada ciclo e é a base para detectar conflitos reais (mudança nos dois lados desde a última sincronização) versus mudanças triviais.
- **Cliente Google Drive:** usa o escopo `drive.file` (não sensível), que **dispensa o processo de verificação de app do Google** e o limite de 100 usuários de teste — adequado para uso pessoal/pequenos grupos sem fricção de aprovação. Detecção de mudanças remotas via `changes.list` com `pageToken` (não via listagem completa recorrente), o que também economiza cota: uma chamada `list` custa 100 unidades contra as 1.000.000 de unidades/minuto por projeto e 325.000/minuto por usuário disponíveis — folga confortável mesmo para cofres grandes.
- **Motor de resolução de conflitos:** merge de 3 vias para texto (semelhante ao usado pelo Self-hosted LiveSync com `diff-match-patch`): se as mudanças estão em partes diferentes do arquivo, combina automaticamente; se colidem no mesmo trecho, grava as duas versões em uma pasta `_conflitos/` (nunca sobrescreve silenciosamente) e sinaliza para revisão manual. Tratamento especial para blocos de **frontmatter YAML** (comum no Obsidian), mesclando chaves não conflitantes em vez de tratar o bloco inteiro como texto opaco.
- **Motor de Regras (automação):** um arquivo de configuração (YAML/JSON) versionado no próprio cofre, definindo regras como criação de nota diária, auto-tageamento por palavra-chave/pasta de origem, e reorganização de diretórios por status (ex.: mover para `Arquivo/` quando a nota atinge um frontmatter `status: concluído`).
- **Backoff e resiliência de rede:** retry exponencial nos erros 403/429 de cota, fila offline quando não há internet, e log estruturado para diagnóstico.

### 4.2 Nota de arquitetura importante (correção do rascunho original)

Se o plano é distribuir o Synapse para outros usuários (não só uso pessoal), cada instalação deveria usar **suas próprias credenciais OAuth** (modelo "traga sua própria chave de API", como o rclone faz para Google Drive), em vez de um único client ID compartilhado — isso evita que a cota de um projeto único seja dividida entre todos os usuários do software e evita depender de aprovação de verificação do Google para escopos sensíveis. Para uso pessoal (só você), isso não é um problema hoje.

## 5. Público-Alvo

- Usuários de Obsidian que não quiseram pagar pelo Obsidian Sync **e** que, com a mudança do Remotely Save em janeiro de 2026, perderam sua ponte gratuita com o Google Drive — um público real e recém-criado pelo mercado.
- Pessoas que já pagam por armazenamento no Google One e querem aproveitar esse espaço em vez de contratar mais um serviço.
- Entusiastas de produtividade e PKM que queiram um sistema local-first, com dados em texto puro, sem depender só de um único plugin comunitário mantido por voluntários.
- Desenvolvedores e usuários técnicos confortáveis rodando um serviço em background — o público inicial não é o usuário leigo (isso é um recorte deliberado de escopo, ver Riscos).

## 6. Diferenciais e Proposta de Valor

- **Custo zero de assinatura própria**, apoiado no armazenamento que o usuário já paga (Google One) e na cota gratuita da Google Drive API.
- **Propriedade e portabilidade dos dados (local-first):** tudo é Markdown puro, no disco do usuário e no Drive dele — sem banco de dados proprietário fechado. (Nota: isso é propriedade e portabilidade, não sigilo automático — os arquivos passam pelos servidores do Google como qualquer arquivo do Drive; criptografia ponta a ponta é item de roadmap, não garantia atual.)
- **Resolução de conflito no nível dos melhores concorrentes self-hosted**, sem exigir que o usuário hospede um servidor CouchDB.
- **Único com motor de automação nativo** entre as opções gratuitas do mercado — nem Syncthing, nem Google Drive Sync, nem Google Drive Desktop têm isso.
- **Serviço multithread em C#/.NET**, com tratamento de rede, filas persistentes e backoff — mais robusto do que scripts caseiros em Python/Bash que a maioria das soluções "faça você mesmo" hoje usa.

## 7. Riscos e Mitigações

| Risco | Mitigação |
|---|---|
| Google alterar termos/cota da Drive API ou descontinuar escopo `drive.file` | Abstrair o provedor de nuvem por trás de uma interface (`ICloudProvider`), permitindo trocar por OneDrive/Dropbox/WebDAV no futuro |
| Usuário não técnico ter dificuldade para instalar um serviço Windows | Escopo inicial explicitamente técnico (devs/power users); instalador simplificado fica para uma fase posterior |
| Cofres muito grandes (milhares de arquivos) esbarrarem em cota | Uso de `changes.list` incremental em vez de varredura completa; cache local de hashes evita re-upload desnecessário |
| Conflito mal resolvido causar perda de conteúdo | Nunca sobrescrever: toda colisão real vira arquivo em `_conflitos/`, nunca é apagada automaticamente |
| Concorrência (Obsidian Sync baixar de preço, ou Google lançar sync nativo) | Diferenciação pelo motor de automação, que nenhuma solução de sync pura oferece |
| Nome "Synapse" colide com Matrix Synapse (homeserver) e Azure Synapse Analytics, prejudicando SEO/achabilidade | Risco aceito conscientemente; se o projeto crescer para distribuição pública, considerar um nome composto (ex.: "Synapse for Obsidian") só para listagens públicas (GitHub, marketplaces), mantendo "Synapse" internamente |

## 8. Métricas de Sucesso

- **Métrica norte:** % de ciclos de sincronização concluídos sem intervenção manual do usuário.
- Tempo médio entre uma alteração local e sua propagação para o Drive (meta inicial: sob 30s em rede estável).
- Taxa de conflitos reais (colisão no mesmo trecho) por 1.000 sincronizações — indicador de qualidade do merge de 3 vias.
- Zero casos de perda de dado silenciosa (toda perda potencial deve virar arquivo em `_conflitos/`, nunca desaparecer).

## 9. Roadmap Faseado

- **MVP:** watcher local + sync bidirecional básico via `drive.file` + índice local de hashes + fila offline.
- **V1:** motor de resolução de conflitos com merge de 3 vias e tratamento de frontmatter; pasta `_conflitos/`; bandeja de sistema com status.
- **V2:** Motor de Regras completo (notas diárias, auto-tageamento, reorganização por status) configurável via YAML no próprio cofre.
- **V3 (Visão de Futuro original, mantida):** assistente passivo que analisa as notas sincronizadas e sugere conexões entre ideias antigas (linkagem inteligente) e gera relatórios de produtividade — possivelmente com embeddings locais ou um provedor de LLM plugável, mantendo o local-first como padrão e qualquer chamada externa como opt-in explícito.
- **V4 (aberto):** criptografia opcional ponta a ponta antes do upload, e abstração multi-provedor (OneDrive/Dropbox/WebDAV) via `ICloudProvider`.

## 10. Sustentabilidade

Como o produto se apoia em armazenamento e cota que o próprio usuário já possui, não há custo de infraestrutura a repassar — o que sustenta a proposta de "custo zero". Caminhos de sustentabilidade do projeto em si (não do usuário): open-source com doações voluntárias, ou um nível "Pro" futuro cobrindo apenas recursos que geram custo real para quem os mantém (ex.: automações com LLM em nuvem), nunca a sincronização básica em si — para não repetir o movimento que tornou o Remotely Save pago.

---

## Fontes consultadas (pesquisa de mercado, ago/2026)

- [Obsidian pricing 2026 (eesel)](https://www.eesel.ai/blog/obsidian-pricing) — preço atual do Obsidian Sync
- [Como sincronizar o Obsidian de graça (Stephan Miller)](https://www.stephanmiller.com/sync-obsidian-vault-across-devices/) — Git, Syncthing, iCloud e suas limitações
- [Remotely Save ficou pago para Google Drive (note.com)](https://note.com/sa222_co/n/n6d05efa93f3c?hl=en) — mudança de monetização de jan/2026
- [Remotely Save – docs de setup do Google Drive](https://github.com/remotely-save/remotely-save/blob/master/docs/remote_services/googledrive/README.md) — uso do escopo `drive.file`
- [Google Drive API — usage limits](https://developers.google.com/workspace/drive/api/guides/limits) — cotas por projeto/usuário e custo de cada operação
- [Google OAuth 100-user limit (Unipile)](https://www.unipile.com/google-oauth-100-user-limit/) — cap de app não verificado
- [Self-hosted LiveSync — resolução de conflitos (DeepWiki)](https://deepwiki.com/vrtmrz/obsidian-livesync/4.2-conflict-resolution) — algoritmo de merge de 3 vias que inspirou a seção 4.1

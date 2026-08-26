# PRD — Documento de Requisitos do Produto: Synapse

*Versão 1.0 · 26/08/2026*
*Baseado em: `Visão de Produto - Synapse.md` (v2.1)*

---

## 1. Sumário Executivo

O **Synapse** é um serviço em C#/.NET, rodando em background, que sincroniza um cofre (vault) do Obsidian gratuito com o Google Drive do usuário (aproveitando o espaço já contratado no Google One), somando um motor de automação de notas. Este PRD detalha os requisitos funcionais e não-funcionais necessários para construir o produto descrito na Visão de Produto, organizados em fases entregáveis (MVP → V1 → V2).

Este documento assume que o leitor já tem acesso à Visão de Produto para contexto de mercado, concorrência e diferenciais — aqui o foco é **o que construir**, não **por que construir**.

## 2. Objetivos do Produto

- **O1.** Permitir sincronização bidirecional confiável entre um cofre Obsidian local e uma pasta no Google Drive, sem exigir assinatura paga.
- **O2.** Nunca perder dados do usuário silenciosamente — todo conflito não resolvível automaticamente deve ser preservado, nunca sobrescrito.
- **O3.** Rodar como serviço de background estável, sobrevivendo a quedas de rede, reinícios do computador e picos de uso de cota da API.
- **O4.** Oferecer automação básica de notas (motor de regras) que nenhum concorrente gratuito atual oferece.
- **O5.** Manter o escopo de credenciais e permissões o mais restrito possível (uso do escopo `drive.file`), evitando fricção de verificação do Google.

## 3. Fora de Escopo (Não-Objetivos da v1)

Para manter o MVP e a V1 entregáveis, os itens abaixo são **explicitamente adiados**, não esquecidos:

- Suporte nativo a mobile (iOS/Android) — o serviço é um processo de background desktop (Windows primeiro).
- Criptografia ponta a ponta dos arquivos antes do upload (item de roadmap V4 na Visão de Produto).
- Suporte a múltiplos provedores de nuvem simultâneos (OneDrive, Dropbox, WebDAV) — a abstração `ICloudProvider` é prevista na arquitetura, mas só o Google Drive é implementado na v1.
- Suporte a múltiplos cofres sincronizando ao mesmo tempo na mesma instância do serviço.
- Interface gráfica completa de configuração — a v1 usa bandeja de sistema + arquivo de configuração (YAML/JSON), não uma GUI rica.
- Linkagem inteligente por IA e relatórios de produtividade (V3 da Visão de Produto) — fora do PRD atual, será um PRD próprio quando chegar a vez.
- Modelo "traga sua própria credencial OAuth" para distribuição multiusuário — só necessário se/quando o projeto for distribuído; a v1 assume uso pessoal/pequeno grupo.

## 4. Personas e Casos de Uso

| Persona | Necessidade principal | Cenário típico |
|---|---|---|
| **Dev/power user solo** (perfil primário) | Sincronizar o cofre entre PC de casa e notebook de trabalho sem pagar assinatura | Edita notas nos dois computadores em dias diferentes; espera que a versão mais recente esteja sempre disponível |
| **Ex-usuário do Remotely Save** | Recuperar sync gratuito com Google Drive perdido em jan/2026 | Já tem um cofre grande e um fluxo de trabalho estabelecido; quer migração de baixo atrito |
| **Estudante/entusiasta de PKM** | Automatizar tarefas repetitivas (nota diária, tags) | Quer que o sistema crie a nota do dia automaticamente e organize por status |

### Casos de uso críticos (para os quais a v1 precisa responder bem)

1. Usuário edita uma nota localmente enquanto está offline; ao reconectar, a alteração sobe sem perda.
2. Usuário edita a mesma nota em dois dispositivos antes de sincronizar; o sistema resolve automaticamente quando possível e nunca apaga conteúdo quando não é possível.
3. Usuário reinicia o computador; o serviço volta a rodar sozinho e retoma a sincronização do ponto onde parou.
4. Usuário atinge um pico de atividade (ex.: importa 500 notas de uma vez); o sistema não estoura a cota da API nem trava.

## 5. Requisitos Funcionais

IDs no formato `RF-<módulo>.<número>`. Prioridade em MoSCoW (Must/Should/Could).

### 5.1 Módulo de Autenticação e Conexão (AUTH)

| ID | Requisito | Prioridade | Critério de aceite |
|---|---|---|---|
| RF-AUTH.1 | O sistema deve autenticar com o Google Drive via OAuth 2.0, solicitando exclusivamente o escopo `drive.file` (não sensível). | Must | Fluxo de consentimento do Google não exibe aviso de "app não verificado" nem exige verificação; token obtido e persistido localmente de forma segura (não em texto plano). |
| RF-AUTH.2 | O sistema deve permitir escolher/criar a pasta no Google Drive que será a raiz da sincronização. | Must | Usuário seleciona ou nomeia uma pasta na primeira configuração; a escolha é persistida. |
| RF-AUTH.3 | O sistema deve renovar o token de acesso automaticamente antes de expirar, sem exigir novo login manual. | Must | Refresh token usado com sucesso em testes de expiração simulada; nenhuma interrupção de sincronização por token vencido. |
| RF-AUTH.4 | O sistema deve permitir desconectar/revogar o acesso e reconectar com outra conta. | Should | Opção disponível na bandeja de sistema; ao desconectar, nenhuma sincronização adicional ocorre até novo login. |

### 5.2 Módulo de Sincronização (SYNC)

| ID | Requisito | Prioridade | Critério de aceite |
|---|---|---|---|
| RF-SYNC.1 | O sistema deve monitorar o cofre local via `FileSystemWatcher` com debounce, detectando criação, edição, exclusão e renomeação de arquivos `.md` e anexos. | Must | Uma edição salva no Obsidian dispara no máximo 1 evento de sincronização por arquivo dentro de uma janela de debounce configurável (padrão: 2s). |
| RF-SYNC.2 | O sistema deve detectar mudanças remotas no Google Drive via `changes.list` com `pageToken` persistido, evitando listagens completas recorrentes. | Must | Nenhuma chamada de listagem completa do Drive ocorre em operação normal (fora da primeira sincronização); consumo de cota medido e dentro do esperado (ver RNF-3). |
| RF-SYNC.3 | O sistema deve manter um índice local (SQLite/LiteDB) com hash de conteúdo, `mtime` e identificador de revisão do Drive por arquivo. | Must | Índice sobrevive a reinício do serviço; é a fonte de verdade para decidir se um arquivo mudou desde a última sincronização. |
| RF-SYNC.4 | O sistema deve enfileirar eventos de sincronização em disco, sobrevivendo a quedas do processo antes de serem processados. | Must | Ao matar o processo com eventos pendentes e reiniciá-lo, os eventos são processados normalmente, sem perda. |
| RF-SYNC.5 | O sistema deve funcionar offline, enfileirando alterações locais e sincronizando assim que a conectividade voltar. | Must | Alterações feitas com a rede desligada aparecem no Drive em até 30s após a reconexão (ver RNF-1). |
| RF-SYNC.6 | O sistema deve aplicar retry com backoff exponencial em erros 403/429 (cota excedida) e 5xx da API do Drive. | Must | Em teste simulando erro 429, o sistema tenta novamente com atraso crescente e não trava nem descarta o evento. |
| RF-SYNC.7 | O sistema deve ignorar arquivos e pastas configuráveis via lista de exclusão (ex.: `.obsidian/workspace.json`, arquivos temporários). | Should | Arquivo listado em exclusão nunca é enviado ao Drive nem gera evento de sincronização. |

### 5.3 Módulo de Resolução de Conflitos (CONFLICT)

| ID | Requisito | Prioridade | Critério de aceite |
|---|---|---|---|
| RF-CONFLICT.1 | O sistema deve detectar conflito real quando o mesmo arquivo foi alterado localmente e remotamente desde a última sincronização bem-sucedida. | Must | Alteração simultânea simulada em dois "dispositivos" é corretamente identificada como conflito (não como sobrescrita simples). |
| RF-CONFLICT.2 | Para arquivos de texto (`.md`), o sistema deve tentar merge automático de 3 vias quando as alterações estão em trechos diferentes do arquivo. | Must | Duas edições em seções distintas da mesma nota resultam em um único arquivo com as duas alterações combinadas, sem intervenção manual. |
| RF-CONFLICT.3 | O sistema deve tratar o bloco de frontmatter YAML separadamente do corpo do texto, mesclando chaves não conflitantes. | Should | Alteração de uma tag no frontmatter local e de outra chave no frontmatter remoto resulta em merge de ambas, sem sobrescrever uma pela outra. |
| RF-CONFLICT.4 | Quando o merge automático não é possível (mesmo trecho alterado nos dois lados), o sistema **nunca deve sobrescrever silenciosamente**: ambas as versões devem ser preservadas em uma pasta `_conflitos/`. | Must | Em nenhum cenário de teste um conteúdo é perdido sem deixar rastro recuperável. |
| RF-CONFLICT.5 | O sistema deve registrar todo conflito não resolvido automaticamente em log, com caminho dos arquivos gerados em `_conflitos/`. | Should | Log contém timestamp, arquivo original e caminhos das duas versões preservadas. |

### 5.4 Módulo de Motor de Regras / Automação (RULES)

| ID | Requisito | Prioridade | Critério de aceite |
|---|---|---|---|
| RF-RULES.1 | O sistema deve ler um arquivo de configuração de regras (YAML/JSON) versionado dentro do próprio cofre. | Must | Alterar o arquivo de regras e salvar aplica o novo comportamento na próxima execução, sem exigir reinstalação. |
| RF-RULES.2 | O sistema deve suportar uma regra de criação automática de nota diária em caminho/template configuráveis. | Should | Nota do dia é criada automaticamente na primeira execução do dia, seguindo o template definido. |
| RF-RULES.3 | O sistema deve suportar auto-tageamento por palavra-chave ou pasta de origem, aplicado ao frontmatter. | Should | Nota criada em pasta configurada recebe a(s) tag(s) definida(s) automaticamente no frontmatter. |
| RF-RULES.4 | O sistema deve suportar reorganização de nota entre pastas com base em valor de campo no frontmatter (ex.: `status: concluído` → mover para `Arquivo/`). | Could | Alterar o campo `status` de uma nota move o arquivo para a pasta correspondente na próxima varredura. |
| RF-RULES.5 | Toda ação do motor de regras deve ser registrada em log, e nenhuma regra deve apagar conteúdo — apenas mover/criar/editar metadados. | Must | Log mostra cada ação de regra aplicada; nenhum teste de regra resulta em exclusão de conteúdo do usuário. |

### 5.5 Módulo de Interface e Operação (UX/OPS)

| ID | Requisito | Prioridade | Critério de aceite |
|---|---|---|---|
| RF-UX.1 | O sistema deve expor um ícone na bandeja do sistema mostrando o status atual (sincronizado / sincronizando / erro / offline). | Must | Ícone muda de estado visivelmente em cada uma das quatro situações, testado manualmente. |
| RF-UX.2 | O sistema deve permitir pausar/retomar a sincronização manualmente pela bandeja. | Should | Ao pausar, nenhum novo evento é processado até retomar. |
| RF-UX.3 | O sistema deve rodar como Windows Service ou processo de inicialização automática, sobrevivendo a reinícios do computador sem ação manual do usuário. | Must | Após reiniciar o computador, o serviço está rodando e sincronizando sem o usuário abrir nada manualmente. |
| RF-UX.4 | O sistema deve gerar logs estruturados e acessíveis para diagnóstico local. | Should | Logs em arquivo, com nível configurável (info/warning/error), rotacionados para não crescer indefinidamente. |

## 6. Requisitos Não-Funcionais

| ID | Categoria | Requisito |
|---|---|---|
| RNF-1 | Performance | Tempo entre uma alteração local salva e sua propagação para o Drive deve ficar abaixo de 30 segundos em rede estável (métrica norte já definida na Visão de Produto). |
| RNF-2 | Confiabilidade | Zero perda de dado silenciosa: toda perda potencial deve resultar em arquivo preservado em `_conflitos/`, nunca em exclusão sem rastro (mapeado para RF-CONFLICT.4). |
| RNF-3 | Uso de cota | Consumo de cota da Google Drive API deve operar folgado dentro dos limites documentados (325.000 unidades/minuto por usuário; chamadas de `list` custam 100 unidades) mesmo em cofres grandes, graças ao uso de `changes.list` incremental em vez de varredura completa. |
| RNF-4 | Segurança | Tokens OAuth e credenciais nunca são armazenados em texto plano; escopo de acesso limitado a `drive.file`. |
| RNF-5 | Resiliência de rede | O serviço deve se recuperar de qualquer interrupção de rede sem intervenção manual, retomando de onde parou. |
| RNF-6 | Extensibilidade | O acesso ao provedor de nuvem deve passar por uma interface (`ICloudProvider`), mesmo que só o Google Drive esteja implementado na v1, para permitir troca de provedor no futuro sem reescrever o motor de sync. |
| RNF-7 | Compatibilidade | v1 roda em Windows (10/11) com .NET (versão LTS mais recente disponível no início da implementação). |
| RNF-8 | Observabilidade | Todo erro tratado deve ser logado com contexto suficiente para diagnóstico sem precisar reproduzir o problema. |

## 7. Fluxos de Usuário (User Flows)

**Fluxo 1 — Onboarding inicial**
1. Usuário instala o serviço e aponta para a pasta do cofre Obsidian local.
2. Sistema solicita login OAuth (escopo `drive.file`).
3. Usuário escolhe/cria a pasta de destino no Google Drive.
4. Sistema realiza a primeira sincronização completa (única vez em que uma varredura total é aceitável) e passa a operar via `changes.list` a partir daí.

**Fluxo 2 — Edição normal (caminho feliz)**
1. Usuário edita e salva uma nota no Obsidian.
2. `FileSystemWatcher` detecta a mudança após o debounce.
3. Sistema calcula hash, compara com o índice local, e faz upload da diferença.
4. Ícone da bandeja mostra "sincronizando" e depois "sincronizado".

**Fluxo 3 — Conflito**
1. Sistema detecta que o mesmo arquivo mudou local e remotamente desde a última sincronização.
2. Tenta merge de 3 vias (texto) e merge de chaves (frontmatter).
3. Se resolvido automaticamente: aplica o merge, sincroniza, loga a resolução.
4. Se não resolvido: grava as duas versões em `_conflitos/`, loga o evento, ícone da bandeja sinaliza atenção necessária.

**Fluxo 4 — Perda de conectividade**
1. Sistema perde conexão com a internet.
2. Continua monitorando o cofre local normalmente; eventos vão para a fila persistida em disco.
3. Ícone da bandeja mostra "offline".
4. Conexão retorna: fila é processada em ordem, com backoff se a API responder com erro de cota.

## 8. Fases de Entrega

| Fase | Escopo (requisitos) | Critério de saída da fase |
|---|---|---|
| **MVP** | RF-AUTH.1–3, RF-SYNC.1–6, RF-UX.1, RF-UX.3 | Sincronização bidirecional básica funcionando de forma confiável entre dois dispositivos, sobrevivendo a reinício e queda de rede. Conflitos simples ainda podem exigir intervenção manual grosseira (ex.: última escrita vence, com aviso). |
| **V1** | RF-CONFLICT.1–5, RF-AUTH.4, RF-SYNC.7, RF-UX.2, RF-UX.4 | Resolução de conflito com merge de 3 vias e proteção total contra perda silenciosa (RNF-2 atingido). |
| **V2** | RF-RULES.1–5 | Motor de regras completo e configurável, sem exigir código para customizar. |
| **V3 / V4** | Fora deste PRD | Ver seção "Visão de Futuro" da Visão de Produto (linkagem inteligente, criptografia E2E, multi-provedor). Requer PRD próprio quando priorizado. |

## 9. Métricas de Sucesso (herdadas e detalhadas da Visão de Produto)

- **Métrica norte:** % de ciclos de sincronização concluídos sem intervenção manual do usuário — meta MVP: ≥ 95%.
- Tempo médio de propagação de alteração local → Drive: meta ≤ 30s em rede estável (RNF-1).
- Taxa de conflitos reais (colisão no mesmo trecho) por 1.000 sincronizações — acompanhar como indicador de qualidade do merge, sem meta numérica fixa no MVP (linha de base a ser estabelecida em uso real).
- Casos de perda de dado silenciosa: meta = 0, sempre (RNF-2, critério de bloqueio de release, não apenas meta).

## 10. Riscos e Dependências

Herdados da Visão de Produto (seção 7), com adição dos riscos específicos de execução:

| Risco | Impacto | Mitigação |
|---|---|---|
| Complexidade do merge de 3 vias ser subestimada | Atraso na fase V1 | MVP entrega sync básico sem essa complexidade; V1 isola o problema em um módulo dedicado (CONFLICT) testável isoladamente |
| `FileSystemWatcher` do .NET perder eventos sob carga alta (limitação conhecida da API do Windows) | Arquivos não sincronizados silenciosamente | Reconciliação periódica de segurança (varredura leve comparando índice local com Drive) como rede de proteção, mesmo usando `changes.list` como caminho principal |
| Nome "Synapse" colidir com Matrix Synapse / Azure Synapse Analytics | Dificuldade de achabilidade/documentação | Já registrado na Visão de Produto; sem impacto técnico neste PRD |
| Escopo `drive.file` não dar acesso a arquivos criados fora do app (ex.: se o usuário já tinha uma pasta populada manualmente no Drive) | Primeira sincronização pode não enxergar arquivos pré-existentes | Validar esse comportamento cedo (spike técnico) antes de fechar RF-AUTH.2; se confirmado, ajustar fluxo de onboarding para recriar a pasta pelo app |

## 11. Perguntas em Aberto

- O merge automático de 3 vias será implementado com biblioteca própria em C# ou via `diff-match-patch` portado/wrapado? (Precisa de spike técnico antes do início da fase V1.)
- O comportamento do escopo `drive.file` com uma pasta pré-existente populada manualmente precisa ser validado — ver risco acima.
- Qual será o mecanismo de instalação do Windows Service (instalador MSI, `sc create` manual, ou publicação self-contained)? Ainda não decidido, não bloqueia o MVP mas deveria ser decidido antes do fim dele.

---

*Este PRD deve ser tratado como vivo: atualizar as tabelas de requisitos conforme decisões técnicas forem tomadas durante a implementação, mantendo os IDs (RF-x, RNF-x) estáveis para rastreabilidade entre requisito, código e teste.*

# ADR — Registro de Decisões Arquiteturais: Synapse

*Versão 1.4 · 26/08/2026*
*Formato: adaptado do template de Michael Nygard, com um campo extra obrigatório de conformidade com a restrição de custo zero*
*Este arquivo é o **registro vivo** de decisões arquiteturais do Synapse a partir de agora. As 8 decisões que já estavam listadas na seção 9 de `SAD - Synapse.md` foram trazidas para cá; o SAD passa a apontar para este arquivo em vez de manter uma cópia própria (evita duas fontes divergentes).*

---

## Regra permanente do projeto

> **Toda escolha de produto, serviço, biblioteca, ferramenta ou dependência do Synapse deve ser gratuita, sem exigir assinatura recorrente e sem exigir cartão de crédito ou qualquer dado de pagamento — mesmo quando o cartão for pedido só para "verificação" e não gere cobrança.**

Essa regra vale para toda decisão passada (auditadas abaixo) e futura (checklist obrigatório na seção final). Ela formaliza e reforça algo que já era um diferencial central desde a Visão de Produto ("custo zero de assinaturas específicas"), mas agora vira **critério explícito de aceite de qualquer ADR**, não apenas uma aspiração de produto.

---

## Formato de cada ADR

- **Status** — Aceita / Proposta / Substituída por ADR-xxx
- **Contexto** — o problema que motivou a decisão
- **Decisão** — o que foi decidido
- **Consequências** — trade-offs assumidos
- **Alternativas consideradas** — o que foi descartado e por quê
- **Conformidade com a restrição de custo zero** — auditoria explícita: gratuito? sem assinatura? sem cartão? fonte/justificativa

---

## ADR-001 — Arquitetura Hexagonal (Ports & Adapters)

- **Status:** Aceita
- **Contexto:** o núcleo de sincronização precisa ser testável sem rede real (DoD do Backlog exige teste unitário para toda lógica nova) e extensível para outros provedores de nuvem no futuro (V4 da Visão de Produto).
- **Decisão:** isolar toda infraestrutura (Google Drive, SQLite, sistema de arquivos, bandeja) atrás de interfaces definidas em `Synapse.Core`, nunca referenciadas em sentido inverso.
- **Consequências:** mais um nível de indireção do que uma chamada direta ao SDK do Google — aceito pelo ganho em testabilidade e extensibilidade.
- **Alternativas consideradas:** camadas simples chamando o SDK diretamente — mais simples de escrever, mas acopla o núcleo à infraestrutura.
- **Conformidade com custo zero:** ✅ N/A direta — é um padrão de código, não um produto/serviço contratado. Nenhum custo envolvido.

## ADR-002 — SQLite como armazenamento local

- **Status:** Aceita
- **Contexto:** escolher entre SQLite e LiteDB para o índice local.
- **Decisão:** SQLite via `Microsoft.Data.Sqlite`.
- **Consequências:** exige um driver relacional leve; em troca, ganha-se ferramentas de inspeção amplamente conhecidas.
- **Alternativas consideradas:** LiteDB — mais simples de embutir, descartado por ecossistema de inspeção menor (não por custo — os dois são gratuitos).
- **Conformidade com custo zero:** ✅ SQLite é domínio público; `Microsoft.Data.Sqlite` é gratuito, open source (MIT), sem conta, sem cartão, sem limite de uso. LiteDB (a alternativa) também seria compatível — a escolha entre as duas não foi motivada por custo, ambas passam no critério.

## ADR-003 — Escopo OAuth `drive.file`

- **Status:** Aceita
- **Contexto:** o escopo `drive` completo é sensível e exige verificação do Google; era preciso confirmar que o caminho gratuito (criar um projeto no Google Cloud Console e gerar credenciais OAuth próprias) realmente não pede cartão de crédito, já que isso violaria a regra permanente do projeto.
- **Decisão:** usar exclusivamente `drive.file`. **Verificação adicional feita nesta revisão (26/08/2026):** criar um projeto no Google Cloud Console, ativar a Google Drive API e gerar um Client ID OAuth (tipo "Desktop app") **não exige cartão de crédito nem conta de faturamento (billing account)** — isso é uma etapa totalmente separada do "Google Cloud Free Trial" (o programa promocional de US$ 300/90 dias, esse sim pede cartão, só para verificação de identidade, sem cobrança). Ferramentas open source consolidadas que fazem exatamente isso (ex.: Cyberduck, rclone) documentam o mesmo fluxo sem menção a cobrança/cartão em nenhuma etapa.
- **Consequências:** confirma que o caminho de menor privilégio (`drive.file`) é também o caminho de custo zero — sem essa verificação, correríamos risco de descobrir a exigência de cartão só na implementação.
- **Alternativas consideradas:** escopo `drive` completo — já rejeitado antes por ampliar demais o acesso; permanece rejeitado.
- **Conformidade com custo zero:** ✅ Confirmado por pesquisa nesta revisão — nenhuma etapa de criação de projeto/credencial OAuth exige cartão. Uso da API dentro da cota gratuita documentada não gera cobrança. **Risco residual:** o Google pode alterar essa política no futuro; se isso acontecer, esta ADR precisa ser revisitada antes de qualquer release nova (ver seção de riscos ao fim).

## ADR-004 — `changes.list` incremental em vez de varredura completa recorrente

- **Status:** Aceita
- **Contexto:** detectar mudanças remotas sem estourar cota gratuita.
- **Decisão:** usar `changes.list` com `pageToken` persistido, campos restritos via `fields`.
- **Consequências:** exige guardar/tratar o `pageToken`; em troca, custo de cota ordens de grandeza menor.
- **Alternativas consideradas:** varredura completa periódica — descartada por custo de cota (embora ainda gratuita dentro do limite, arrisca esgotar a cota diária em cofres grandes, o que forçaria considerar um plano pago — herda a mesma preocupação de custo zero).
- **Conformidade com custo zero:** ✅ Mesmo endpoint gratuito do ADR-003; a escolha por `changes.list` existe justamente para manter uso bem abaixo do teto gratuito, reduzindo ainda mais qualquer risco de precisar de um plano pago no futuro.

## ADR-005 — Merge de 3 vias local, sem servidor de sincronização

- **Status:** Aceita
- **Contexto:** o Self-hosted LiveSync resolve conflitos de forma madura, mas depende de hospedar um servidor CouchDB.
- **Decisão:** implementar o merge de 3 vias como lógica pura local em `Synapse.Conflict`, sem servidor externo.
- **Consequências:** qualidade do merge depende inteiramente da lógica local; em troca, mantém "zero servidor para manter".
- **Alternativas consideradas:** CouchDB próprio (auto-hospedado) — gratuito se auto-hospedado em hardware do próprio usuário, mas exigiria manter um servidor rodando (VPS pago na prática, para disponibilidade fora da rede local) — rejeitado tanto pela complexidade quanto pelo risco de empurrar o usuário para um custo de hospedagem. CouchDB gerenciado (ex.: IBM Cloudant) — rejeitado de imediato: exige cartão/assinatura.
- **Conformidade com custo zero:** ✅ Esta decisão é a que mais diretamente **é reforçada** pela regra permanente — a alternativa gerenciada violaria a regra abertamente, e mesmo a auto-hospedada tende a empurrar o usuário para um VPS pago. A escolha por lógica 100% local elimina esse risco por completo.

## ADR-006 — Worker Service / Windows Service como modelo de hospedagem

- **Status:** Aceita
- **Contexto:** o produto precisa rodar continuamente em background, sobrevivendo a reinício do computador.
- **Decisão:** hospedar como .NET Worker Service registrável como Windows Service, com bandeja de sistema.
- **Consequências:** instalação exige privilégio administrativo — aceitável para o público técnico da v1.
- **Alternativas consideradas:** aplicativo com janela sempre aberta — rejeitado por não bater com o modelo de uso esperado (não por custo).
- **Conformidade com custo zero:** ✅ Windows Service é um recurso do próprio sistema operacional, sem custo adicional imposto pelo Synapse (o Windows em si já é uma licença que o usuário possui independentemente do projeto — não é uma dependência nova introduzida por esta decisão).

## ADR-007 — Canal único de eventos com consumidor serializado

- **Status:** Aceita
- **Contexto:** múltiplas fontes de evento precisam escrever no mesmo índice sem condição de corrida.
- **Decisão:** `Channel<SyncEvent>` único (`System.Threading.Channels`, parte do próprio .NET) com um único consumidor assíncrono.
- **Consequências:** processamento sequencial, não paralelo — aceitável pois o gargalo real é rede, não CPU.
- **Alternativas consideradas:** filas paralelas por tipo de evento com locks — rejeitada por complexidade, não por custo.
- **Conformidade com custo zero:** ✅ `System.Threading.Channels` é parte do runtime .NET, sem nenhuma dependência ou serviço externo.

## ADR-008 — DPAPI para proteção de credenciais

- **Status:** Aceita
- **Contexto:** o `refresh_token` OAuth não pode ficar em texto plano em disco.
- **Decisão:** `System.Security.Cryptography.ProtectedData` (DPAPI do Windows).
- **Consequências:** amarra a proteção à conta do Windows do usuário — aceitável.
- **Alternativas consideradas:** criptografia simétrica própria com gestão de chave manual — rejeitada por risco técnico, não por custo. Um cofre de segredos em nuvem (ex.: Azure Key Vault) também foi descartado mentalmente aqui — exigiria assinatura, violando a regra permanente.
- **Conformidade com custo zero:** ✅ DPAPI é parte do Windows, sem custo adicional. A alternativa de nuvem (Key Vault) teria sido rejeitada por essa regra mesmo que fosse tecnicamente atraente — vale registrar isso explicitamente para não ser reconsiderada sem essa ressalva no futuro.

## ADR-009 — Restrição permanente: gratuito, sem assinatura, sem cartão

- **Status:** Aceita (política transversal, aplicável retroativamente e para o futuro)
- **Contexto:** até aqui, "custo zero" era um argumento de posicionamento de produto (Visão de Produto, diferenciais). Não havia, porém, um critério formal e obrigatório de auditoria em cada decisão arquitetural — o risco é alguma decisão futura (ex.: adicionar telemetria, um serviço de crash-report, um provedor de nuvem alternativo em V4) introduzir sem perceber uma dependência que pede cartão "só para verificação" ou um nível gratuito com pegadinha de expiração.
- **Decisão:** toda ADR nova a partir de agora deve preencher obrigatoriamente o campo "Conformidade com custo zero" (ver formato no topo deste documento) antes de ser aceita. Nenhuma decisão que introduza necessidade de cartão de crédito ou assinatura recorrente pode ser aprovada, mesmo que o valor cobrado seja zero ou simbólico (ex.: autorização de US$ 1 "só para verificar identidade").
- **Consequências:** algumas ferramentas/serviços tecnicamente superiores podem ficar fora de cogitação só por pedirem cartão (ex.: CouchDB gerenciado no ADR-005, Azure Key Vault no ADR-008) — trade-off aceito conscientemente, é o próprio ponto da regra.
- **Alternativas consideradas:** tratar isso apenas como diretriz informal (como estava até agora) — rejeitada porque diretriz informal não é auditável nem à prova de esquecimento em decisões futuras tomadas sob pressão de prazo.
- **Conformidade com custo zero:** ✅ Trivial — esta ADR *é* a regra de conformidade.

## ADR-010 — Named Pipe como transporte de IPC entre Bandeja e Serviço

- **Status:** Aceita
- **Contexto:** o Windows isola serviços (Session 0) da sessão interativa do usuário — o ícone de bandeja (RF-UX.1) precisa rodar como processo separado (`Synapse.Tray`) e precisa de um canal para conversar com o Windows Service (`Synapse.Host`). Essa necessidade só ficou explícita ao escrever `API - Synapse.md` — nenhum documento anterior havia especificado esse canal.
- **Decisão:** usar Named Pipe local (`System.IO.Pipes`, `\\.\pipe\synapse-ipc`) com mensagens JSON, em vez de HTTP loopback ou gRPC.
- **Consequências:** protocolo mais simples de implementar e depurar do que gRPC; menor superfície de ataque que HTTP loopback (não abre porta de rede, nem que seja só em `localhost`); em troca, não há ferramentas de inspeção HTTP genéricas (como um navegador ou Postman) para depurar manualmente — aceitável dado o volume baixo de mensagens.
- **Alternativas consideradas:** HTTP loopback (`http://localhost:<porta>`) — rejeitado por abrir uma porta TCP local desnecessariamente para um caso de uso 1-para-N simples; gRPC — rejeitado por complexidade desproporcional ao problema (KISS).
- **Conformidade com custo zero:** ✅ `System.IO.Pipes` é parte do runtime .NET. As alternativas consideradas (HTTP loopback via Kestrel embutido, gRPC via `Grpc.Net`) também seriam gratuitas — a decisão aqui não foi motivada por custo, mas por simplicidade e superfície de segurança; registrado para deixar claro que não há decisão de custo pendente nesta ADR.

## ADR-011 — GitHub Actions como CI, dentro do plano gratuito

- **Status:** Aceita
- **Contexto:** o `CONTRIBUTING - Synapse.md` exige CI verde (build + testes) como condição para mesclar qualquer PR em `main`. O repositório já está hospedado no GitHub (`github.com/VictorSilva-Desenvolvedor/Synapse`), confirmado no `.git/config`.
- **Decisão:** usar GitHub Actions como plataforma de CI, dentro do plano gratuito.
- **Consequências:** zero configuração de infraestrutura externa (o runner já vem com o GitHub); em troca, repositórios **privados** têm cota mensal gratuita limitada de minutos de execução (repositórios públicos têm minutos ilimitados no plano gratuito) — se o repositório permanecer privado e o uso de CI crescer muito (ex.: matriz de testes em vários SOs), essa cota pode um dia ser atingida. Não é um problema hoje (volume de um projeto solo é baixo), mas é um ponto de atenção registrado.
- **Alternativas consideradas:** outros serviços de CI de terceiros (ex.: CircleCI, Travis CI) — rejeitados por não trazerem vantagem sobre o que já vem integrado ao GitHub, e por alguns exigirem cartão de crédito para planos gratuitos de repositórios privados (checado como parte do critério de custo zero, não usado por não passar no checklist com a mesma folga que o GitHub Actions).
- **Conformidade com custo zero:** ✅ com ressalva registrada — GitHub Actions não exige cartão de crédito para o plano gratuito, mas repositório privado tem teto mensal de minutos (não é uma assinatura, é uma cota que reseta; ultrapassar exigiria decisão consciente de pagar ou tornar o repo público, nunca cobrança automática sem aviso). Se essa cota vier a ser um problema real, revisar esta ADR antes de aceitar qualquer plano pago.

## ADR-012 — Shouldly em vez de FluentAssertions como biblioteca de asserção

- **Status:** Aceita
- **Contexto:** ao montar o `Plano de Testes - Synapse.md`, a escolha "óbvia" de biblioteca de asserção para .NET seria FluentAssertions, a mais popular do ecossistema. Verificação: **a partir da versão 8 (2025), FluentAssertions passou a exigir licença comercial paga (Xceed) para uso comercial**, permanecendo gratuita apenas para uso não comercial — não passaria no checklist do ADR-009 se usada na versão atual.
- **Decisão:** usar **Shouldly** (MIT) como biblioteca de asserção.
- **Consequências:** sintaxe ligeiramente diferente (`valor.ShouldBe(esperado)` em vez de `valor.Should().Be(esperado)`) — sem impacto real de produtividade, é só uma convenção a seguir desde o primeiro teste escrito.
- **Alternativas consideradas:** FluentAssertions v8+ — rejeitada, paga para uso comercial. FluentAssertions v7 fixada (ainda Apache 2.0) — rejeitada por ficar presa a uma versão sem atualizações de segurança/manutenção no longo prazo. **AwesomeAssertions** (fork Apache 2.0 do FluentAssertions v7, API idêntica) — alternativa válida e também gratuita, preterida só porque Shouldly é um projeto MIT estabelecido por conta própria, não uma resposta reativa a uma mudança de licença de terceiro (menor risco de repetir o mesmo problema no futuro).
- **Conformidade com custo zero:** ✅ Shouldly é MIT, gratuita, sem cota nem cartão. **Esta ADR existe justamente porque o checklist do ADR-009 pegou uma dependência que quase entraria sem passar por ele** — é o registro de o processo funcionando como pretendido, não só uma decisão a mais.

## ADR-013 — Publicação self-contained + script `sc.exe` como instalador do Windows Service

- **Status:** Aceita
- **Contexto:** o PRD (seção 11, "Perguntas em Aberto") deixou em aberto o mecanismo de instalação do Windows Service — instalador MSI, `sc create` manual, ou publicação self-contained — marcado como TECH-04 no Backlog, "Must, precisa ser resolvida antes do fim do MVP". O público-alvo da v1 é explicitamente técnico (SRS 2.3: "usuário técnico único... confortável instalando e operando um serviço de background"), e ADR-006 já aceitou que a instalação exige privilégio administrativo.
- **Decisão:** publicar `Synapse.Host` como executável **self-contained de arquivo único** (`dotnet publish -r win-x64 --self-contained true -p:PublishSingleFile=true`) e registrar/remover o serviço através de um script PowerShell (`install.ps1`/`uninstall.ps1`) que chama `sc.exe create`/`sc.exe delete` — sem instalador MSI (sem WiX Toolset ou equivalente) na v1.
- **Consequências:** nenhuma dependência de runtime .NET precisa estar pré-instalada na máquina de destino (self-contained resolve isso); o script fica pequeno, legível e auditável por um usuário técnico antes de rodar como administrador — coerente com o público-alvo; em troca, não há assistente gráfico de instalação nem entrada automática em "Programas e Recursos" do Windows (typicamente entregue por um MSI) — aceitável para a v1, já que uma instalação amigável para usuário não técnico já está fora de escopo (Visão de Produto, seção 7 — Riscos; PRD, seção 3).
- **Alternativas consideradas:** instalador MSI via WiX Toolset — tecnicamente superior em polimento (GUI, desinstalação padrão do Windows, versionamento de upgrade), mas adiciona um toolchain de build inteiro (compilação de `.wxs`, licenciamento de UI) para um público que já opera confortavelmente via linha de comando; descartado por desproporção de complexidade (KISS, mesmo raciocínio de ADR-005/ADR-007/ADR-010), não por custo — WiX é gratuito e open source, permanece candidato natural se a v2+ mirar um público não técnico (já sinalizado como fora de escopo). `sc create` totalmente manual (sem script) — descartado por não ser repetível nem versionável; o script PowerShell resolve isso sem o peso de um instalador completo.
- **Conformidade com custo zero:** ✅ `dotnet publish` (SDK já usado no projeto), `sc.exe` e PowerShell são nativos do Windows — nenhuma ferramenta nova, sem conta, sem cartão. A alternativa descartada (WiX) também seria gratuita — a decisão aqui não foi motivada por custo, mas por simplicidade proporcional ao público-alvo da v1.

---

## Auditoria consolidada

| ADR | Envolve produto/serviço externo? | Gratuito? | Sem assinatura? | Sem cartão? |
|---|---|---|---|---|
| ADR-001 (Hexagonal) | Não (padrão de código) | — | — | — |
| ADR-002 (SQLite) | Sim (biblioteca) | ✅ | ✅ | ✅ |
| ADR-003 (`drive.file`) | Sim (Google Drive API) | ✅ | ✅ | ✅ (verificado nesta revisão) |
| ADR-004 (`changes.list`) | Sim (mesmo serviço do ADR-003) | ✅ | ✅ | ✅ |
| ADR-005 (merge local) | Não (evita explicitamente um serviço externo) | ✅ | ✅ | ✅ |
| ADR-006 (Windows Service) | Sim (recurso do SO já licenciado pelo usuário) | ✅ | ✅ | ✅ |
| ADR-007 (Channel .NET) | Sim (runtime .NET) | ✅ | ✅ | ✅ |
| ADR-008 (DPAPI) | Sim (recurso do SO) | ✅ | ✅ | ✅ |
| ADR-010 (Named Pipe) | Sim (runtime .NET) | ✅ | ✅ | ✅ |
| ADR-011 (GitHub Actions) | Sim (serviço de CI) | ✅ | ✅ (cota, não assinatura) | ✅ |
| ADR-012 (Shouldly) | Sim (biblioteca de teste) | ✅ | ✅ | ✅ (FluentAssertions v8 teria falhado aqui) |
| ADR-013 (instalador `sc.exe`) | Sim (recursos do SO: `dotnet publish`, `sc.exe`, PowerShell) | ✅ | ✅ | ✅ |

Nenhuma não-conformidade encontrada na auditoria retroativa.

---

## Checklist obrigatório para toda ADR futura

Antes de aceitar qualquer nova ADR (ex.: ao decidir sobre V3/V4 — provedor de IA para linkagem inteligente, multi-provedor de nuvem, etc.), responder:

1. O produto/serviço/biblioteca é gratuito para o uso previsto, sem prazo de teste que expira?
2. Ele não exige assinatura recorrente, mesmo em plano "grátis para sempre com limite"?
3. Ele não exige cartão de crédito ou dado de pagamento em nenhuma etapa de configuração — nem para "verificação"?
4. Se a resposta a qualquer pergunta acima for não, a alternativa gratuita/sem cartão foi genuinamente esgotada antes de aceitar a exceção?

Se qualquer resposta for "não" sem justificativa forte documentada, a ADR não deve ser aceita nesse formato — precisa voltar para uma alternativa compatível ou ser escalada como uma exceção explícita e consciente (o que, até hoje, nunca ocorreu neste projeto).

---

*Fontes consultadas para a verificação do ADR-003 (26/08/2026): [Google Cloud Signup FAQs](https://cloud.google.com/signup-faqs) (cartão exigido apenas no "Free Trial" promocional, não na criação de projeto/credenciais), [Building Cloud-Native Apps for Free in 2026 (Medium)](https://lalatenduswain.medium.com/building-cloud-native-apps-for-free-in-2026-the-complete-developers-guide-to-google-cloud-s-3d93b77c4adb), [tutorial de OAuth Client ID para Google Drive do Cyberduck](https://docs.cyberduck.io/tutorials/custom_oauth_client_id/) (ferramenta open source real usando exatamente este fluxo, sem etapa de cobrança).*

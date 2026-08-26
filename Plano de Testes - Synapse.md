# Plano de Testes — Synapse

*Versão 1.0 · 26/08/2026*
*Baseado em: `SRS - Synapse.md`, `SAD - Synapse.md`, `API - Synapse.md`, `Backlog - Synapse.md` (DoD)*

---

## 1. Introdução

Este plano define como o Synapse é testado: em que níveis, com quais ferramentas, e com quais **agentes de teste** — dublês de infraestrutura e atores simulados que substituem o Google Drive real, o disco real e múltiplos dispositivos reais, para que a suíte rode rápida, determinística e sem gastar cota da API ou exigir conta do Google no CI.

## 2. Objetivos

- Garantir que toda lógica nova tenha teste unitário antes de ser aceita (DoD do Backlog).
- Validar os quatro casos de uso críticos do PRD (seção 4) de forma automatizada sempre que possível.
- Detectar regressão em `ThreeWayMerger` e `RuleEngine` — os dois componentes com maior risco de bug silencioso (lógica de merge e regras que nunca podem apagar conteúdo, RF-CONFLICT.4 e RF-RULES.5).
- Nunca depender de uma conta Google real ou de rede real para rodar a suíte no CI (nem para não vazar credencial, nem para não gastar cota, nem para manter o CI rápido e determinístico).

## 3. Escopo

**Dentro do escopo:** `Synapse.Core`, `Synapse.Sync`, `Synapse.Conflict`, `Synapse.Rules`, `Synapse.Data` — tudo que é lógica de domínio ou integração testável sem depender de infraestrutura real do Windows.

**Fora do escopo de automação** (testado manualmente, ver seção 8): registro/instalação do Windows Service, comportamento real da bandeja do sistema, e o primeiro handshake OAuth de verdade contra a conta Google do usuário — depender de UI do Windows ou de uma conta Google real não é compatível com CI determinístico e gratuito.

## 4. Estratégia por Nível de Teste

| Nível | O que valida | Roda contra |
|---|---|---|
| **Unitário** | Lógica pura: merge de 3 vias, motor de regras, cálculo de hash/debounce | Nenhuma infraestrutura — funções puras e classes com dublês |
| **Integração** | Componentes de `Synapse.Sync`/`Data` conversando entre si | SQLite real **em arquivo temporário** (não é infraestrutura externa — é o próprio banco do produto) + `FakeCloudProvider` |
| **Contrato** | Garantir que `GoogleDriveProvider` (real) e `FakeCloudProvider` (teste) implementam `ICloudProvider` com o mesmo comportamento observável | Suíte de testes compartilhada, rodada contra os dois — a real só localmente/manual (precisa de credencial), nunca no CI |
| **Sistema (simulado)** | Os 4 casos de uso críticos do PRD, ponta a ponta, mas com nuvem e disco simulados | `FakeCloudProvider` + `IVaultWatcher` fake + `IFileSystem` em memória |
| **Desempenho** | Orçamento de latência (RNF-1) e uso de cota (RNF-3) | Benchmarks locais, não no CI a cada PR (rodar sob demanda) |
| **Manual/exploratório** | Instalação do serviço, bandeja, primeiro OAuth real | Checklist de release (seção 8), não automatizado |

## 5. Ferramentas de Teste

Toda ferramenta abaixo já passou pelo checklist de custo zero do **ADR-009** antes de entrar nesta lista — uma delas só passou depois de uma correção de rota registrada como **ADR-012** (ver nota).

| Ferramenta | Papel | Licença | Nota |
|---|---|---|---|
| **xUnit** | Framework de testes | Apache 2.0, gratuita | Já previsto desde o SAD |
| **Shouldly** | Biblioteca de asserções (`resultado.ShouldBe(esperado)`) | MIT, gratuita | Escolhida **no lugar de FluentAssertions** — ver ADR-012 abaixo |
| **NSubstitute** | Mocking para os poucos casos onde um dublê programável é melhor que um fake escrito à mão | BSD-3-Clause, gratuita | Preferir fakes escritos à mão (seção 6) sempre que possível; usar mock só quando o comportamento a verificar é a *interação* em si, não o estado resultante |
| **coverlet** | Medição de cobertura de código | MIT, gratuita | Integra direto com `dotnet test` |
| **ReportGenerator** | Relatório de cobertura em HTML, legível localmente | Apache 2.0, gratuita | Rodado localmente ou como artefato do CI |
| **Bogus** | Geração de dados de teste realistas (nomes de nota, conteúdo, datas) | MIT, gratuita | Evita testes com dados artificiais demais (`"a"`, `"b"`) que escondem bugs de edge case |
| **BenchmarkDotNet** | Testes de desempenho (RNF-1) | MIT, gratuita | Rodado sob demanda, não a cada PR (é lento por natureza) |
| **`TimeProvider`** (nativo do .NET) | Abstração de tempo para testar debounce (RF-SYNC.1) e expiração de token (RF-AUTH.3) sem `Thread.Sleep` real | Parte do runtime .NET, gratuita | Não é um pacote NuGet — já vem no .NET; usar `FakeTimeProvider` (pacote `Microsoft.Extensions.TimeProvider.Testing`, também MIT/gratuito) nos testes |
| **Stryker.NET** (V1+, não MVP) | Teste de mutação — verifica se os testes realmente pegam bug, não só rodam | MIT/Apache 2.0, gratuita | Nice-to-have (`TECH`), não bloqueante para o MVP |
| **GitHub Actions** | Execução da suíte no CI | Gratuita (ADR-011, com ressalva de cota em repo privado) | Já decidido |

### Nota — ADR-012 (registrada em `ADR - Synapse.md`)

Ao escolher a biblioteca de asserções, quase se recomendaria **FluentAssertions** por ser a mais popular do ecossistema .NET — mas a partir da **versão 8 (lançada em 2025), FluentAssertions exige licença comercial paga (Xceed) para uso comercial**, permanecendo gratuita só para uso não comercial. Isso não passaria no checklist do ADR-009. A alternativa escolhida foi **Shouldly** (MIT, mantida independentemente, sem drama de relicenciamento) — existe também **AwesomeAssertions** (fork Apache 2.0 do FluentAssertions v7, API idêntica) como opção se a sintaxe `.Should()` for preferida; Shouldly foi escolhida por ser um projeto MIT estabelecido por conta própria, não uma resposta reativa a uma mudança de licença de terceiro. Esse é exatamente o tipo de armadilha que o checklist do ADR-009 existe para pegar — vale como exemplo documentado de por que auditar cada dependência, mesmo as "óbvias".

---

## 6. Agentes de Teste

### 6.1 Dublês de Infraestrutura (Fakes)

Implementações alternativas das portas hexagonais (`API - Synapse.md`), usadas nos testes em vez das implementações reais — vivem em `Synapse.Tests` (ou um projeto `Synapse.TestDoubles` compartilhado, se crescerem o suficiente):

| Dublê | Substitui | Comportamento |
|---|---|---|
| `FakeCloudProvider` | `GoogleDriveProvider` (`ICloudProvider`) | Mantém arquivos em um `Dictionary<string, CloudFile>` em memória; simula `changes.list` devolvendo só o que mudou desde o `pageToken` passado; **configurável para lançar exceções sob demanda** (ver 6.3) |
| `InMemorySyncIndexStore` | Repositório SQLite (`ISyncIndexStore`) | Mesmas garantias de contrato (ex.: `PeekNext` não remove o item), só que em memória — mais rápido para testes unitários; os testes de **integração** usam o SQLite real (arquivo temporário), não este fake |
| `FakeVaultWatcher` | `FileSystemWatcher` real (`IVaultWatcher`) | Permite o teste "disparar" um `VaultChangeEvent` chamando um método diretamente, sem tocar disco |
| `FakeTimeProvider` | Relógio do sistema | Permite avançar o tempo manualmente nos testes (`Advance(TimeSpan)`) para testar debounce (RF-SYNC.1) e expiração de token (RF-AUTH.3) sem esperar de verdade |

### 6.2 Agentes Simulados Multi-Dispositivo

Para os cenários de conflito (RF-CONFLICT.1–4) e de sincronização entre dois computadores, os testes de integração/sistema simulam **dois agentes independentes**, cada um com sua própria instância de `SyncQueueProcessor` + `InMemorySyncIndexStore`, compartilhando o **mesmo** `FakeCloudProvider` (representando o único ponto de encontro real entre eles — o Google Drive, exatamente como na Visão de Implantação do SAD):

```
Agente A (dispositivo 1)  ──┐
                             ├──►  FakeCloudProvider (compartilhado)
Agente B (dispositivo 2)  ──┘
```

Um teste típico: "Agente A edita a nota X e sincroniza; Agente B, sem saber da mudança de A, edita a mesma nota X em um trecho diferente e sincroniza; ambos devem convergir para o mesmo conteúdo mesclado" — isso testa RF-CONFLICT.2 de ponta a ponta sem precisar de dois computadores reais nem de rede real.

### 6.3 Simulador de Falhas

O `FakeCloudProvider` (6.1) aceita configuração de falha programada — ex.: `fake.FalharProximaChamadaCom(new CloudQuotaExceededException())` — usada para testar RF-SYNC.6 (backoff exponencial) de forma determinística, sem precisar realmente estourar a cota da API real para provocar o erro.

---

## 7. Casos de Teste Críticos

Mapeados aos "Casos de uso críticos" do PRD (seção 4) e aos RF-x correspondentes.

| ID | Cenário | RF-x | Nível | Agentes usados |
|---|---|---|---|---|
| TC-01 | Edição offline sincroniza sem perda ao reconectar | RF-SYNC.5 | Sistema simulado | `FakeCloudProvider` (modo offline) |
| TC-02 | Mesma nota editada em dois dispositivos antes de sincronizar; merge automático quando não há sobreposição | RF-CONFLICT.1, RF-CONFLICT.2 | Sistema simulado | Agente A + Agente B (6.2) |
| TC-03 | Mesma nota editada no mesmo trecho nos dois dispositivos; nenhuma versão é perdida | RF-CONFLICT.4 | Sistema simulado | Agente A + Agente B (6.2) |
| TC-04 | Reinício do processo com itens pendentes na fila; nenhum item é perdido nem duplicado | RF-SYNC.4 | Integração | SQLite real (arquivo temporário) |
| TC-05 | Importação em massa (500 notas de uma vez) não trava nem estoura orçamento de chamadas | RF-SYNC.2, RNF-3 | Desempenho | `FakeCloudProvider` contando chamadas |
| TC-06 | Erro 429 da API dispara backoff exponencial, não derruba o processamento | RF-SYNC.6 | Unitário | `FakeCloudProvider` (6.3) + `FakeTimeProvider` |
| TC-07 | Token expirado é renovado automaticamente antes de falhar uma operação | RF-AUTH.3 | Unitário | `FakeTimeProvider` |
| TC-08 | Regra de automação nunca produz uma ação de exclusão | RF-RULES.5 | Unitário | Nenhum (o próprio tipo `RuleAction` já impede isso — teste confirma que nenhum caminho de código tenta contornar) |
| TC-09 | Merge de frontmatter combina chaves não conflitantes | RF-CONFLICT.3 | Unitário | Nenhum (função pura) |
| TC-10 | Debounce agrupa múltiplos eventos rápidos no mesmo arquivo em um só | RF-SYNC.1 | Unitário | `FakeVaultWatcher` + `FakeTimeProvider` |

---

## 8. Critérios de Entrada e Saída

**Entrada** (quando uma história pode ser considerada "em teste"): código implementado, DoD do Backlog cumprida em código, build local passando.

**Saída** (quando uma fase — MVP/V1/V2 — pode ser considerada testada e pronta para release):

1. Todos os TC-x marcados **Must** nesta fase estão passando no CI.
2. Cobertura de linha ≥ 80% em `Synapse.Core`, `Synapse.Conflict` e `Synapse.Rules` (os módulos de lógica pura — meta mais alta aqui porque são os mais fáceis de cobrir e os de maior risco).
3. Cobertura de linha ≥ 60% em `Synapse.Sync`/`Data` (menor, porque parte do código ali é I/O fino, de menor valor marginal para testar linha a linha).
4. **Checklist manual da seção 9** executado ao menos uma vez na fase (instalação real do serviço, primeiro OAuth real).

## 9. Checklist Manual (fora do CI)

Executado manualmente antes de cada release, não automatizado (seção 3):

- [ ] Instalação do Windows Service em uma máquina limpa (ou VM) funciona sem erro.
- [ ] Ícone de bandeja aparece e reflete o estado real (RF-UX.1).
- [ ] Primeiro fluxo OAuth real completa e não pede escopo além de `drive.file` (confirmação visual na tela de consentimento do Google).
- [ ] Reiniciar o computador e o serviço volta a rodar sozinho (RF-UX.3).

## 10. Ambiente e Execução

- Local: `dotnet test` (todos os níveis exceto o manual da seção 9).
- CI (GitHub Actions, ADR-011): roda em todo PR — unitário + integração + contrato (só contra o fake). Testes de desempenho (BenchmarkDotNet) e o contrato contra o `GoogleDriveProvider` real **não** rodam a cada PR — rodados manualmente sob demanda, já que o segundo exigiria credencial real armazenada no CI (evitado deliberadamente).

## 11. Riscos de Teste

| Risco | Mitigação |
|---|---|
| `FakeCloudProvider` divergir do comportamento real do Google Drive com o tempo | Suíte de testes de contrato (seção 4) compartilhada entre fake e real, rodada manualmente contra a real antes de cada release |
| Cobertura alta sem qualidade real de teste (testes que rodam mas não verificam nada) | Stryker.NET (mutação) como prática recomendada a partir da V1, não só medir cobertura de linha |
| Testes de concorrência (canal único, SAD seção 4) serem difíceis de tornar determinísticos | Usar `FakeTimeProvider` e execução síncrona controlada nos testes, evitando `Task.Delay` real dentro da suíte |

## 12. Rastreabilidade

Ver tabela da seção 7 (TC-x ↔ RF-x). Para os módulos sem caso de teste crítico listado individualmente (ex.: cada método de `ISyncIndexStore`), a exigência geral da DoD do Backlog — teste unitário para toda lógica nova — continua se aplicando linha a linha, não só nos cenários de ponta a ponta desta tabela.

---

*Este plano evolui junto com o código: todo TC-x novo nasce de uma história do Backlog ou de um bug encontrado (que vira um TC-x de regressão permanente, nunca é corrigido "silenciosamente" sem deixar um teste que teria pego o bug).*

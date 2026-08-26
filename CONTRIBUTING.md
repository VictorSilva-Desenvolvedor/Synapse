# Guia de Contribuição — Synapse

*Versão 1.0 · 26/08/2026*
*Repositório: [github.com/VictorSilva-Desenvolvedor/Synapse](https://github.com/VictorSilva-Desenvolvedor/Synapse) · branch padrão: `main`*

---

## 1. Propósito

Este guia define o fluxo de trabalho com git/GitHub para o Synapse — hoje um projeto de um único mantenedor, mas documentado como se aceitasse contribuições externas desde já, para que o histórico fique limpo e rastreável independente de quantas pessoas trabalham nele. Segue as mesmas regras de rastreabilidade já usadas nos outros documentos (`RF-x`, `US-x`, `TECH-x`, `ADR-x`).

## 2. Regra de autoria — sem coautoria, nunca

**Nenhum commit ou Pull Request deste repositório inclui trailer de coautoria** (`Co-authored-by:` ou equivalente), **em nenhuma hipótese** — nem para pareamento entre pessoas, nem para ferramentas de IA usadas como apoio na escrita do código. A autoria do commit reflete exclusivamente quem efetivamente revisou e assumiu a responsabilidade pelo código, registrado apenas nos metadados padrão de autor/committer do git (`user.name`/`user.email`).

Se qualquer ferramenta (assistente de IA, editor, bot de CI) adicionar esse tipo de trailer automaticamente, ele deve ser removido antes do commit ser criado — não é uma preferência estética, é regra do projeto.

## 3. Fluxo de branches

- `main` é protegida: **nenhum commit direto nela**, sempre via Pull Request.
- Toda branch de trabalho nasce de `main` atualizada.
- Convenção de nome, amarrada aos IDs já usados no Backlog/PRD/SRS — facilita saber o que uma branch faz só pelo nome:

| Tipo de branch | Convenção | Exemplo |
|---|---|---|
| Implementação de história do backlog | `feature/<US-id>-<slug-curto>` | `feature/us-sync.1-watcher-local` |
| Item técnico sem RF-x direto | `feature/<TECH-id>-<slug-curto>` | `feature/tech-02-escrita-atomica` |
| Correção de bug | `fix/<slug-curto>` | `fix/debounce-race-condition` |
| Documentação | `docs/<slug-curto>` | `docs/atualiza-srs-merge` |
| Manutenção/infra (sem lógica de produto) | `chore/<slug-curto>` | `chore/configura-github-actions` |

## 4. Padrão de commit

Formato inspirado em Conventional Commits, adaptado para referenciar sempre o ID do Backlog quando existir:

```
<tipo>(<escopo>): <descrição curta no imperativo>

<corpo opcional explicando o porquê, não só o quê>

Refs: US-SYNC.1 (RF-SYNC.1)
```

- **Tipos aceitos:** `feat`, `fix`, `docs`, `test`, `refactor`, `chore`.
- **Escopo:** o projeto/módulo afetado (`core`, `sync`, `conflict`, `rules`, `data`, `host`, `tray`).
- **Refs:** quando o commit implementa ou avança uma história do Backlog, referenciar o `US-x`/`TECH-x` (e o `RF-x` entre parênteses, se aplicável) — mantém o commit rastreável até a Visão de Produto, sem precisar abrir os documentos para saber por que aquele código existe.
- **Nunca** incluir trailer de coautoria (seção 2).
- Commits pequenos e coesos: preferir vários commits pequenos dentro da mesma branch a um único commit gigante — facilita revisão e, no squash da seção 6, não importa o tanto de commits intermediários existirem.

## 5. Pull Requests

Mesmo em contribuição solo, todo trabalho passa por PR antes de chegar a `main` — funciona como uma instância de autorrevisão antes de integrar, e deixa histórico público do que foi decidido em cada mudança.

**Todo PR deve conter:**

1. Título no mesmo formato do commit (`tipo(escopo): descrição`).
2. Referência ao(s) `US-x`/`TECH-x` do Backlog que ele implementa (ou "sem item de backlog" + justificativa, para mudanças fora do backlog planejado).
3. Checklist de saída, baseado na Definition of Done já definida em `Backlog - Synapse.md`:
   - [ ] Sem trecho placeholder/`TODO` pendente
   - [ ] Teste unitário cobrindo toda lógica nova (quando aplicável)
   - [ ] Critério de aceite da história verificado
   - [ ] `SRS`/`PRD`/`API` atualizados, se o comportamento especificado neles mudou
   - [ ] Build e testes passando localmente

## 6. Critérios para aceitar e mesclar um PR

Um PR só pode ser mesclado em `main` quando, **todos** ao mesmo tempo:

1. O CI (GitHub Actions — build + suíte de testes) está verde. Nenhum PR é mesclado com CI vermelho, mesmo que "seja só um teste flaky" — investigar antes, não ignorar.
2. O checklist de DoD da seção 5 está integralmente marcado.
3. Não há conflito não resolvido com `main`.
4. Nenhuma dependência nova foi adicionada sem passar pelo checklist de custo zero do **ADR-009** (`ADR - Synapse.md`) — gratuita, sem assinatura, sem cartão.

**Estratégia de merge: Squash and Merge**, sempre. Os commits intermediários da branch (que podem ser vários, por design — seção 4) viram um único commit em `main`, com a mensagem final revisada para seguir o padrão da seção 4 e manter a referência `Refs: US-x`. Isso mantém o histórico de `main` limpo — um commit por PR, fácil de ler no `git log` e fácil de reverter se precisar.

## 7. Depois do merge: sempre remover a branch

Toda branch é apagada (remota e local) imediatamente após o merge — sem exceção, mesmo que "talvez eu reaproveite depois". Se o trabalho não terminou, ele continua em uma branch nova quando for retomado.

- No GitHub: ativar a opção **"Automatically delete head branches"** nas configurações do repositório (Settings → General → Pull Requests) — remove a branch remota sozinho a cada merge, sem depender de lembrar manualmente.
- Localmente, depois de puxar a `main` atualizada:

```bash
git checkout main
git pull origin main
git branch -d feature/us-sync.1-watcher-local
```

## 8. Integração Contínua (CI)

CI via **GitHub Actions**, dentro do plano gratuito (ver **ADR-011** em `ADR - Synapse.md`) — roda em todo PR: restauração de pacotes, build da solution, execução de `Synapse.Tests`. Um PR com CI vermelho não é elegível para merge (seção 6).

## 9. Ambiente de desenvolvimento

- .NET SDK — versão LTS mais recente disponível no início da implementação (ver `SRS - Synapse.md`, seção 2.4; a versão exata deve ser fixada em `global.json` assim que a implementação começar).
- Rodar os testes localmente antes de abrir o PR: `dotnet test`.
- Nenhuma ferramenta paga é necessária para desenvolver, testar ou rodar o Synapse — coerente com a regra permanente de custo zero (`ADR-009`).

---

*Este guia deve ser atualizado junto com o Backlog/SAD/ADR sempre que o fluxo de trabalho mudar — ex.: se o projeto ganhar contribuidores externos de verdade, vale revisar a seção 6 para incluir exigência de aprovação de revisão (code review) antes do merge, algo que hoje não se aplica a um mantenedor único.*

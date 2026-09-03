# Guia de Contribuição — Synapse

*Versão 2.0 · 27/08/2026*
*Repositório: [github.com/VictorSilva-Desenvolvedor/Synapse](https://github.com/VictorSilva-Desenvolvedor/Synapse) · branch padrão: `main`*

---

## 1. Propósito

Este guia define o fluxo de trabalho com git/GitHub para o Synapse — hoje um projeto de mantenedor único, sem contribuições externas. O fluxo real usado desde 27/08/2026 é **commit direto em `main`**, sem branches nem Pull Requests: a etapa de PR se mostrou uma formalidade sem valor de revisão quando não há mais de uma pessoa revisando, e foi abandonada em favor de manter o histórico simples. Continua seguindo as mesmas regras de rastreabilidade usadas nos outros documentos (`RF-x`, `US-x`, `TECH-x`, `ADR-x`).

*(Nos primeiros commits do projeto, entre 26/08/2026, foi usado fluxo de PR + squash merge — ver histórico no GitHub. A seção 10 explica quando isso volta a fazer sentido.)*

## 2. Regra de autoria — sem coautoria, nunca

**Nenhum commit deste repositório inclui trailer de coautoria** (`Co-authored-by:` ou equivalente), **em nenhuma hipótese** — nem para pareamento entre pessoas, nem para ferramentas de IA usadas como apoio na escrita do código. A autoria do commit reflete exclusivamente quem efetivamente revisou e assumiu a responsabilidade pelo código, registrado apenas nos metadados padrão de autor/committer do git (`user.name`/`user.email`).

Se qualquer ferramenta (assistente de IA, editor, bot de CI) adicionar esse tipo de trailer automaticamente, ele deve ser removido antes do commit ser criado — não é uma preferência estética, é regra do projeto.

## 3. Fluxo de trabalho: commit direto em `main`

- Todo trabalho é commitado diretamente em `main`. Não se cria branch de feature, fix, docs ou chore para o dia a dia.
- Isso é seguro porque o projeto não tem outros colaboradores simultâneos além de sessões próprias de IA operando sob supervisão — ver seção 10 para o gatilho de voltar a usar branches/PR.
- Antes de commitar, `main` local deve estar atualizada (`git pull origin main`) para evitar divergência, especialmente quando mais de uma sessão de trabalho pode estar ativa no mesmo repositório.

## 4. Padrão de commit

Formato inspirado em Conventional Commits, adaptado para referenciar sempre o ID do Backlog quando existir:

```
<tipo>(<escopo>): <descrição curta no imperativo>

<corpo opcional explicando o porquê, não só o quê>

Refs: US-SYNC.1 (RF-SYNC.1)
```

- **Tipos aceitos:** `feat`, `fix`, `docs`, `test`, `refactor`, `chore`.
- **Escopo:** o projeto/módulo afetado (`core`, `sync`, `conflict`, `rules`, `data`, `host`, `tray`, `brain`, `agent`, `remote`).
- **Refs:** quando o commit implementa ou avança uma história do Backlog, referenciar o `US-x`/`TECH-x` (e o `RF-x` entre parênteses, se aplicável) — mantém o commit rastreável até a motivação de produto, sem precisar sair do histórico para saber por que aquele código existe.
- **Nunca** incluir trailer de coautoria (seção 2).
- Commits pequenos e coesos são preferíveis, mas — diferente do fluxo antigo por PR — cada commit já vai para `main` como está, então cada um deve deixar o repositório num estado consistente (build e testes passando).

## 5. Checklist antes de cada commit

Sem PR para servir de gate, o checklist da Definition of Done abaixo é verificado **antes de commitar**, não depois:

- [ ] Sem trecho placeholder/`TODO` pendente.
- [ ] Teste unitário cobrindo toda lógica nova (quando aplicável).
- [ ] Critério de aceite da história verificado.
- [ ] Especificação interna atualizada, se o comportamento descrito nela mudou.
- [ ] Build e testes passando localmente (`dotnet test`).
- [ ] Nenhuma dependência nova adicionada sem passar pelo critério de custo zero — gratuita, sem assinatura, sem cartão.

## 6. Critérios para um commit ir para `main`

Um commit só é enviado a `main` quando, **todos** ao mesmo tempo:

1. O checklist da seção 5 está integralmente cumprido.
2. O build e a suíte de testes passam localmente antes do push.
3. O CI (GitHub Actions, disparado no push) fica verde. Se ficar vermelho, o próximo commit corrige o problema antes de qualquer outra mudança — nunca se ignora CI vermelho, mesmo alegando "é só um teste flaky".

## 7. Depois de identificar um problema em `main`

Como não há branch para descartar, um commit problemático é corrigido com um novo commit de `fix` (ou revertido com `git revert`, quando a correção direta não é trivial) — nunca com `git push --force` sobre `main`.

## 8. Integração Contínua (CI)

CI via **GitHub Actions**, dentro do plano gratuito — roda em todo push em `main`: restauração de pacotes, build da solution, execução de `Synapse.Tests`. Um push que deixa o CI vermelho exige correção imediata (seção 6.3), antes de qualquer outra mudança.

## 9. Ambiente de desenvolvimento

- .NET SDK — a versão exata é fixada em `global.json`.
- Rodar os testes localmente antes de cada commit: `dotnet test`.
- Nenhuma ferramenta paga é necessária para desenvolver, testar ou rodar o Synapse — coerente com a regra permanente de custo zero.

## 10. Quando voltar a usar branches e Pull Requests

Este fluxo simplificado vale enquanto o Synapse for mantido só pelo Victor (com ou sem apoio de assistentes de IA sob sua supervisão direta). Se o projeto ganhar um colaborador externo de verdade — alguém que não seja o próprio Victor decidindo o que entra no código — os commits diretos em `main` devem parar imediatamente e o projeto volta ao fluxo de branch por convenção (`feature/`, `fix/`, `docs/`, `chore/`) + Pull Request obrigatório + Squash and Merge + exigência de aprovação de revisão (code review) antes do merge, e esta seção 10 deve ser removida do guia nesse momento.

---

*Este guia deve ser atualizado junto com o Backlog/SAD/ADR sempre que o fluxo de trabalho mudar.*

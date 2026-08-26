# Backlog do Produto — Synapse

*Versão 1.0 · 26/08/2026*
*Baseado em: `Visão de Produto - Synapse.md` (v2.1), `PRD - Synapse.md` (v1.0) e `SRS - Synapse.md` (v1.0)*

---

## 1. Como usar este documento

Este backlog traduz os requisitos já aprovados no PRD/SRS em itens executáveis (épicos → histórias de usuário → tarefas técnicas), prontos para virar trabalho real. Toda história que implementa um requisito mantém o ID original (`RF-x`/`RNF-x`) para rastreabilidade completa: Visão → PRD → SRS → Backlog → código → teste.

---

## 2. Regras do Backlog

### 2.1 Formato padrão de história

Toda história de usuário segue o formato:

> **Como** `<persona>`, **quero** `<capacidade>`, **para** `<benefício>`.

Itens puramente técnicos (sem persona de usuário final, ex.: setup de projeto) usam o formato:

> **Como** desenvolvedor, **quero** `<capacidade técnica>`, **para** `<benefício técnico>`.

### 2.2 Definition of Ready (DoR) — critérios para um item entrar em execução

Um item só pode ser puxado para "Em andamento" quando:

1. Tem critério de aceite claro e testável (herdado do PRD ou escrito na própria história).
2. Está vinculado a um `RF-x`/`RNF-x` do PRD/SRS **ou** justificado como item de fundação técnica sem requisito formal correspondente.
3. Não tem dependência bloqueante em aberto (ver coluna "Depende de" em cada tabela).
4. Tem estimativa de tamanho definida (ver 2.4).

### 2.3 Definition of Done (DoD) — critérios para considerar um item concluído

Aplicável a **toda** história que envolve lógica de negócio nova (alinhado à prática do projeto: nunca entregar lógica nova sem teste):

1. Código implementado sem trechos placeholder/`TODO` pendente — bloco completo e funcional.
2. Teste unitário cobrindo a lógica nova, rodando e passando.
3. Critério de aceite da história verificado manualmente ou por teste automatizado.
4. Se o comportamento implementado difere do que está escrito no SRS/PRD, os documentos são atualizados antes de fechar o item (documentação nunca fica desatualizada em relação ao código).
5. Sem regressão conhecida introduzida nos testes já existentes.

Itens de documentação/configuração (sem lógica de negócio) usam uma DoD reduzida: critério de aceite cumprido + revisão de que não há inconsistência com os outros documentos do projeto.

### 2.4 Regras de estimativa

Estimativa por tamanho relativo (T-shirt), não por horas — adequado a um projeto solo sem necessidade de previsão de capacidade de equipe:

| Tamanho | Significado |
|---|---|
| **PP** | Poucas linhas, sem lógica nova relevante (config, texto, ajuste pontual) |
| **P** | Lógica simples, testável isoladamente em poucas horas |
| **M** | Requer desenho de solução (ex.: um componente novo com algumas interações) |
| **G** | Múltiplos componentes interagindo, ou lógica de maior risco (ex.: merge de 3 vias) |
| **GG** | Deve ser quebrado em itens menores antes de entrar em "Pronto para execução" — não é um tamanho de execução válido, é um sinal de que falta refinamento |

### 2.5 Regras de priorização

1. A ordem entre fases é fixa e já decidida no PRD: **Fundação → MVP → V1 → V2 → (V3/V4 fora deste backlog)**. Não pular fase.
2. Dentro da mesma fase, prioridade segue dependência técnica real (ex.: autenticação antes de sincronização, sincronização básica antes de conflito).
3. Nenhum item de V1 entra em execução antes de todos os itens **Must** do MVP estarem com DoD cumprida — evita construir resolução de conflito sobre um motor de sync que ainda não é confiável.
4. Itens marcados **Should**/**Could** (herdados da priorização MoSCoW do PRD) podem ser adiados dentro da própria fase sem bloquear o avanço para a fase seguinte, desde que os **Must** da fase estejam concluídos.

### 2.6 Regras de refinamento e mudança

- Qualquer item novo que surja durante a implementação e não tenha `RF-x` correspondente deve ser adicionado à seção 6 (Backlog Técnico) com justificativa, não inserido silenciosamente em uma história existente.
- Se a implementação revelar que um requisito do PRD/SRS está incorreto ou incompleto, a correção é feita no PRD/SRS **primeiro**, e a história é ajustada em seguida — o backlog nunca diverge silenciosamente da especificação.

---

## 3. Épicos

| Épico | Descrição | Módulo relacionado (SRS) |
|---|---|---|
| **EP-00 — Fundação do Projeto** | Estrutura de solução .NET, projetos/namespaces, CI básico, logging | Apêndice B do SRS |
| **EP-01 — Autenticação e Conexão** | OAuth com Google Drive, gestão de token | RF-AUTH |
| **EP-02 — Motor de Sincronização** | Watcher local, índice, fila, detecção remota, backoff | RF-SYNC |
| **EP-03 — Resolução de Conflitos** | Merge de 3 vias, frontmatter, preservação garantida | RF-CONFLICT |
| **EP-04 — Motor de Regras** | Automação configurável sobre notas | RF-RULES |
| **EP-05 — Interface e Operação** | Bandeja de sistema, execução como serviço, logs | RF-UX |

---

## 4. Backlog — Fase MVP

**Meta da fase:** sincronização bidirecional básica funcionando de forma confiável, sobrevivendo a reinício e queda de rede (ver PRD, seção 8).

| ID | Épico | História | RF-x | Prior. | Tam. | Depende de |
|---|---|---|---|---|---|---|
| US-00.1 | EP-00 | Como desenvolvedor, quero a solução .NET estruturada em `Synapse.Core/.Sync/.Data/.Host/.Tests`, para ter uma base modular e testável desde o início. | — | Must | P | — |
| US-00.2 | EP-00 | Como desenvolvedor, quero um projeto de testes (`Synapse.Tests`) configurado com xUnit, para garantir que toda lógica nova nasça testável. | — | Must | PP | US-00.1 |
| US-00.3 | EP-00 | Como desenvolvedor, quero logging estruturado configurado desde o início, para não precisar retrofitar diagnóstico depois. | RNF-8 | Should | P | US-00.1 |
| US-AUTH.1 | EP-01 | Como usuário, quero autenticar com minha conta Google via OAuth2 com escopo `drive.file`, para conectar meu cofre sem dar acesso total ao meu Drive. | RF-AUTH.1 | Must | G | US-00.1 |
| US-AUTH.2 | EP-01 | Como usuário, quero escolher a pasta do Google Drive onde meu cofre será sincronizado, para controlar onde meus dados ficam. | RF-AUTH.2 | Must | P | US-AUTH.1 |
| US-AUTH.3 | EP-01 | Como usuário, quero que meu login não expire sem aviso, para não ter minha sincronização interrompida sem explicação. | RF-AUTH.3 | Must | M | US-AUTH.1 |
| US-SYNC.1 | EP-02 | Como usuário, quero que alterações no meu cofre local sejam detectadas automaticamente, para não precisar disparar a sincronização manualmente. | RF-SYNC.1 | Must | M | US-00.1 |
| US-SYNC.3 | EP-02 | Como desenvolvedor, quero um índice local de hashes/metadados por arquivo, para decidir com precisão o que precisa sincronizar. | RF-SYNC.3 | Must | M | US-00.1 |
| US-SYNC.4 | EP-02 | Como usuário, quero que alterações fiquem numa fila persistida em disco, para não perder nada se o serviço cair no meio do processo. | RF-SYNC.4 | Must | M | US-SYNC.3 |
| US-SYNC.2 | EP-02 | Como usuário, quero que mudanças feitas em outro dispositivo apareçam no meu cofre local, para ter sincronização de verdade nos dois sentidos. | RF-SYNC.2 | Must | G | US-AUTH.2, US-SYNC.3 |
| US-SYNC.5 | EP-02 | Como usuário, quero continuar editando minhas notas offline, para não depender de internet o tempo todo. | RF-SYNC.5 | Must | M | US-SYNC.4 |
| US-SYNC.6 | EP-02 | Como usuário, quero que erros temporários da API do Google não travem minha sincronização, para o serviço se recuperar sozinho. | RF-SYNC.6 | Must | M | US-SYNC.2 |
| US-UX.1 | EP-05 | Como usuário, quero ver o status da sincronização na bandeja do sistema, para saber se está tudo certo sem abrir logs. | RF-UX.1 | Must | P | US-SYNC.1 |
| US-UX.3 | EP-05 | Como usuário, quero que o serviço volte a rodar sozinho depois de reiniciar o computador, para não precisar lembrar de abrir nada. | RF-UX.3 | Must | M | US-00.1 |

---

## 5. Backlog — Fase V1

**Meta da fase:** resolução de conflito robusta (zero perda silenciosa) e melhorias operacionais. Só inicia após todos os itens Must do MVP estarem concluídos (regra 2.5.3).

| ID | Épico | História | RF-x | Prior. | Tam. | Depende de |
|---|---|---|---|---|---|---|
| US-CONFLICT.1 | EP-03 | Como usuário, quero que o sistema perceba quando editei uma nota nos dois dispositivos ao mesmo tempo, para não ter uma versão apagando a outra sem eu saber. | RF-CONFLICT.1 | Must | M | MVP completo |
| US-CONFLICT.2 | EP-03 | Como usuário, quero que edições em partes diferentes da mesma nota sejam combinadas automaticamente, para não precisar resolver conflitos triviais na mão. | RF-CONFLICT.2 | Must | G | US-CONFLICT.1 |
| US-CONFLICT.3 | EP-03 | Como usuário, quero que tags e metadados alterados nos dois lados sejam combinados de forma inteligente, para não perder organização por causa de um conflito. | RF-CONFLICT.3 | Should | M | US-CONFLICT.2 |
| US-CONFLICT.4 | EP-03 | Como usuário, quero que, quando o sistema não conseguir combinar as duas versões, nenhuma delas seja apagada, para nunca perder conteúdo por causa de um conflito. | RF-CONFLICT.4 | Must | M | US-CONFLICT.2 |
| US-CONFLICT.5 | EP-03 | Como usuário, quero um registro de todo conflito não resolvido automaticamente, para conseguir auditar o que aconteceu. | RF-CONFLICT.5 | Should | PP | US-CONFLICT.4 |
| US-AUTH.4 | EP-01 | Como usuário, quero poder desconectar e reconectar minha conta Google, para trocar de conta sem reinstalar o serviço. | RF-AUTH.4 | Should | P | US-AUTH.1 |
| US-SYNC.7 | EP-02 | Como usuário, quero excluir arquivos/pastas específicos da sincronização, para não subir arquivos temporários ou de configuração do Obsidian. | RF-SYNC.7 | Should | P | US-SYNC.1 |
| US-UX.2 | EP-05 | Como usuário, quero poder pausar a sincronização manualmente, para trabalhar offline de propósito sem o serviço brigar comigo. | RF-UX.2 | Should | PP | US-UX.1 |
| US-UX.4 | EP-05 | Como usuário, quero acessar logs organizados quando algo der errado, para diagnosticar problemas sozinho. | RF-UX.4 | Should | P | US-00.3 |

---

## 6. Backlog — Fase V2

**Meta da fase:** motor de automação completo. Só inicia após V1 concluída.

| ID | Épico | História | RF-x | Prior. | Tam. | Depende de |
|---|---|---|---|---|---|---|
| US-RULES.1 | EP-04 | Como usuário, quero definir regras de automação em um arquivo dentro do meu próprio cofre, para customizar o comportamento sem mexer em código. | RF-RULES.1 | Must | M | MVP + V1 completos |
| US-RULES.2 | EP-04 | Como usuário, quero que uma nota diária seja criada automaticamente, para não ter que lembrar de criá-la todo dia. | RF-RULES.2 | Should | P | US-RULES.1 |
| US-RULES.3 | EP-04 | Como usuário, quero que notas novas em certas pastas recebam tags automaticamente, para manter meu cofre organizado sem esforço manual. | RF-RULES.3 | Should | P | US-RULES.1 |
| US-RULES.4 | EP-04 | Como usuário, quero que notas mudem de pasta automaticamente com base no status delas, para meu cofre se organizar sozinho. | RF-RULES.4 | Could | M | US-RULES.1 |
| US-RULES.5 | EP-04 | Como usuário, quero ter certeza de que nenhuma regra de automação jamais apaga conteúdo meu, para confiar no motor de regras sem medo. | RF-RULES.5 | Must | P | US-RULES.1 |

---

## 7. Backlog Técnico (itens sem RF-x direto)

Itens que surgem da necessidade de engenharia, não de um requisito de produto — seguem a regra 2.6.

| ID | Item | Justificativa | Prior. | Tam. | Fase sugerida |
|---|---|---|---|---|---|
| TECH-01 | Reconciliação periódica de segurança (varredura leve comparando índice x disco) | Mitiga limitação conhecida do `FileSystemWatcher` sob carga (ver SRS, seção 3.8) | Should | M | MVP |
| TECH-02 | Escrita atômica local (arquivo temporário + rename) | Evita corrupção em caso de queda durante gravação (ver SRS, tratamento de erros) | Must | P | MVP |
| TECH-03 | Rotina de recriação do índice SQLite em caso de corrupção | Ver SRS, seção 3.8 — evita que um índice corrompido derrube o serviço permanentemente | Should | M | V1 |
| TECH-04 | Empacotamento/instalador do Windows Service | Decisão em aberto no PRD (seção 11); precisa ser resolvida antes do fim do MVP | Must | M | MVP |
| TECH-05 | Spike técnico: comportamento do escopo `drive.file` com pasta pré-existente populada manualmente | Risco identificado no PRD (seção 10); validar antes de fechar US-AUTH.2 | Must | PP | MVP (antes de US-AUTH.2) |
| TECH-06 | ~~Spike técnico: escolha da biblioteca/algoritmo de diff para o merge de 3 vias~~ — **Resolvido, ver ADR-015 (DiffPlex) e ADR-016 (YamlDotNet)** | Pergunta em aberto no PRD (seção 11) | Must | PP | V1 (antes de US-CONFLICT.2) |

---

## 8. Fora deste Backlog

Reflete a seção "Fora de Escopo" do PRD — não incluído aqui, sem data prevista: suporte mobile nativo, criptografia E2E, multi-provedor de nuvem ativo, GUI rica, múltiplos cofres simultâneos, linkagem inteligente por IA, modelo de credencial própria por usuário para distribuição multiusuário. Quando qualquer um desses for priorizado, deve ganhar seu próprio ciclo de Visão/PRD/SRS antes de entrar neste backlog.

---

*Este backlog é vivo: histórias podem ser divididas, reestimadas ou reordenadas dentro das regras da seção 2, mas os IDs (`US-x`, `TECH-x`) não devem ser reaproveitados para itens diferentes depois de criados.*

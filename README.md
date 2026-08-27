# 🧠 Synapse — Sincronização Inteligente & Segundo Cérebro para Obsidian

<p align="center">
  <img src="https://raw.githubusercontent.com/VictorSilva-Desenvolvedor/Synapse/main/docs/assets/synapse-banner.png" alt="Synapse Banner" width="750" onerror="this.style.display='none'" />
</p>

<p align="center">
  <strong>Serviço em segundo plano para Windows (.NET 8) que sincroniza cofres do Obsidian com repositórios privados gratuitos no GitHub, com resolução inteligente de conflitos de 3 vias, automação por regras, criptografia Zero-Knowledge e IA integrada do Google Gemini para o seu Segundo Cérebro.</strong>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?style=flat&logo=dotnet" alt=".NET 8" />
  <img src="https://img.shields.io/badge/Obsidian-Compatible-7C3AED?style=flat&logo=obsidian" alt="Obsidian" />
  <img src="https://img.shields.io/badge/GitHub-Private%20Repo-181717?style=flat&logo=github" alt="GitHub" />
  <img src="https://img.shields.io/badge/Google%20Gemini-AI%20Brain-4285F4?style=flat&logo=google" alt="Google Gemini" />
  <img src="https://img.shields.io/badge/Custo-Zero%20Permanente-10B981?style=flat" alt="Custo Zero" />
  <img src="https://img.shields.io/badge/License-MIT-blue.svg" alt="License" />
</p>

---

## 🌟 Principais Recursos

### 1. 🔄 Sincronização Bidirecional & Confiabilidade Local-First
- **Custo Zero Permanente**: Usa repositórios privados 100% gratuitos do GitHub com autenticação segura via Token (PAT) protegido por **DPAPI** do Windows.
- **Resolução de Conflitos de 3 Vias**: Algoritmo de merge automático em nível de linha com **DiffPlex** e merge chave a chave de Frontmatter YAML com **YamlDotNet**.
- **Comparador Visual de Conflitos (3-Way Diff Viewer)**: Interface gráfica no Tray para resolver conflitos bloco a bloco com 1 clique.
- **Política Zero Perda de Dados (RNF-2)**: Versões conflitantes não resolvidas automaticamente são salvas de forma segura na pasta `_conflitos/`.

### 2. 🧠 Synapse Brain Engine (IA & Segundo Cérebro)
- **Captura Rápida Flutuante (`Ctrl + Shift + S`)**: Janela moderna para despejar ideias, notas rápidas, links e tarefas de qualquer lugar do Windows.
- **Integração Nativa com Google Gemini (Free Tier)**: Processa textos brutos com o modelo `gemini-1.5-flash` e gera automaticamente:
  - Títulos concisos e descritivos.
  - Frontmatter YAML com tags temáticas e resumos.
  - Categorização automática (`Conceito`, `Ideia`, `Referencia`, `Projeto`, `Tarefa`).
- **Auto-Linking & Grafo de Conhecimento**: Conecta automaticamente termos de notas existentes no cofre através de wikilinks `[[...]]` e gera a seção *"## Conexões & Notas Relacionadas"*.
- **Suporte Alternativo Offline**: Suporte híbrido a **Ollama Local** (`http://localhost:11434`) para operação 100% desconectada da internet.

### 3. ⚙️ Automação com Motor de Regras (`.synapse/regras.yaml`)
- **Recarga em Tempo Real (Hot-Reload)**: Edite suas regras diretamente dentro do Obsidian e o serviço atualiza instantaneamente sem necessidade de reiniciar.
- **Ações Automáticas**:
  - Geração automática de Notas Diárias com placeholders dinâmicos (`{{date}}`, `{{time}}`, `{{data:FORMATO}}`).
  - Auto-tagging por termos ou pastas no frontmatter.
  - Reorganização automática de notas por status.

### 4. 🔒 Privacidade & Segurança
- **Criptografia Opcional Zero-Knowledge**: Criptografa o conteúdo das suas notas com **AES-256-GCM** e derivação de chave **PBKDF2** (100.000 iterações com SHA-256) antes do envio ao GitHub.
- **Lista de Exclusão Configurável (`.synapseignore`)**: Padrões glob estilo `.gitignore` com proteção embutida contra anexos pesados (> 50MB).
- **Suporte a Múltiplos Cofres (Multi-Vault)**: Sincronize múltiplos cofres de forma isolada em repositórios diferentes.

### 5. 🖥️ Operação em Segundo Plano & Extensibilidade
- **Serviço do Windows (Session 0)**: Roda de forma contínua com inicialização automática no boot do sistema.
- **Bandeja do Sistema (`Synapse.Tray`)**: Controle completo de estado, pausa/retomada, visualizador de logs em tempo real do Serilog e diagnóstico.
- **Plugin Oficial para Obsidian**: Plugin em TypeScript que exibe o status de sincronização em tempo real na barra de status do Obsidian via Named Pipe IPC (`\\.\pipe\synapse-ipc`).

---

## 🏗️ Arquitetura do Projeto

O Synapse adota **Arquitetura Hexagonal (Ports & Adapters)** em .NET 8:

```
Synapse/
├── src/
│   ├── Synapse.Core/         # Portas e modelos puros de domínio
│   ├── Synapse.Brain/        # Motor de IA, Smart Capture, Auto-linker e provedores Gemini/Ollama
│   ├── Synapse.Conflict/     # Merge 3-vias (DiffPlex), Frontmatter (YamlDotNet) e DiffCalculator
│   ├── Synapse.Data/         # Persistência SQLite local (SyncedFiles, SyncQueue, Conflicts)
│   ├── Synapse.Rules/        # Motor de regras dinâmico e executor não-destrutivo
│   ├── Synapse.Sync/         # GitHubProvider, DPAPI, Crypto AES-GCM, Watcher e .synapseignore
│   ├── Synapse.Host/         # Windows Service (Worker Service) com IpcServer e Serilog
│   └── Synapse.Tray/         # Aplicação WinForms da bandeja, Onboarding, Diagnóstico e Quick Capture
├── plugins/
│   └── obsidian-synapse/     # Plugin oficial em TypeScript para o Obsidian
├── packaging/
│   ├── inno/                 # Script do instalador gráfico Inno Setup (Synapse-Setup.exe)
│   └── winget/               # Manifestos de publicação para o catálogo do winget
├── scripts/                  # Scripts de build, publish, install, uninstall e sign
└── tests/
    └── Synapse.Tests/        # 203 testes unitários e de integração (100% de sucesso)
```

---

## 🚀 Como Começar

### Pré-requisitos
- Windows 10 ou 11 (64-bit).
- [.NET 8.0 SDK / Runtime](https://dotnet.microsoft.com/download/dotnet/8.0).
- Conta no [GitHub](https://github.com/) e um Personal Access Token (PAT) com escopo `repo`.
- *(Opcional)* Chave de API gratuita do [Google AI Studio](https://aistudio.google.com/) para o Brain Engine.

### Instalação Rápida
```powershell
# 1. Clone o repositório
git clone https://github.com/VictorSilva-Desenvolvedor/Synapse.git
cd Synapse

# 2. Compile e execute os testes
dotnet test

# 3. Inicie o aplicativo da bandeja
dotnet run --project src/Synapse.Tray/Synapse.Tray.csproj
```

Na primeira execução, o **Assistente de Onboarding** será exibido automaticamente para configurar o seu token do GitHub, cofre do Obsidian e a API Key do Gemini.

---

## 🧪 Executando os Testes Automatizados

```powershell
dotnet test --configuration Release
```
```
Passed!  - Failed: 0, Passed: 203, Skipped: 0, Total: 203, Duration: 5 s - Synapse.Tests.dll (net8.0)
```

---

## 📄 Licença
Distribuído sob a licença **MIT**. Consulte `LICENSE` para mais informações.
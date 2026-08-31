; Script do Inno Setup para o instalador do Synapse (V3.1, ADR-013)
#define MyAppName "Synapse"
#define MyAppVersion "1.1.1"
#define MyAppPublisher "Victor Silva"
#define MyAppURL "https://github.com/VictorSilva-Desenvolvedor/Synapse"
#define MyAppExeName "Synapse.Tray.exe"
#define MyServiceExeName "Synapse.Host.exe"

[Setup]
AppId={{C789A5F1-8B23-4E8F-B99C-E73F28109F2A}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=..\..\LICENSE
OutputDir=..\..\dist\Installer
OutputBaseFilename=Synapse-Setup-{#MyAppVersion}
Compression=lzma2/ultra
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "Iniciar Synapse junto com o Windows"; GroupDescription: "Inicialização:"; Flags: checkedonce

[Files]
Source: "..\..\dist\Synapse\Tray\*"; DestDir: "{app}\Tray"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\..\dist\Synapse\Service\*"; DestDir: "{app}\Service"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\..\dist\Synapse\install.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\dist\Synapse\uninstall.ps1"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\Tray\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\Tray\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "SynapseTray"; ValueData: """{app}\Tray\{#MyAppExeName}"""; Tasks: autostart; Flags: uninsdeletevalue

[Run]
; Registra o Windows Service como Administrador
Filename: "powershell.exe"; Parameters: "-ExecutionPolicy Bypass -File ""{app}\install.ps1"""; StatusMsg: "Configurando o serviço do Windows..."; Flags: runhidden
; Inicia o aplicativo da bandeja
Filename: "{app}\Tray\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Remove o serviço do Windows antes de desinstalar os arquivos
Filename: "powershell.exe"; Parameters: "-ExecutionPolicy Bypass -File ""{app}\uninstall.ps1"""; StatusMsg: "Removendo serviço do Windows..."; RunOnceId: "SynapseUninstallService"; Flags: runhidden

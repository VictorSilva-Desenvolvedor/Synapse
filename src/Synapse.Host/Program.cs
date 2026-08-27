using Serilog;
using Synapse.Conflict;
using Synapse.Core.Ports;
using Synapse.Data;
using Synapse.Host;
using Synapse.Host.Ipc;
using Synapse.Rules;
using Synapse.Sync;
using Synapse.Sync.Auth;
using Synapse.Sync.GitHub;
using Synapse.Sync.Reconciliation;

var builder = Host.CreateApplicationBuilder(args);

// Configuração do Windows Service
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "Synapse";
});

// Configuração do Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .CreateLogger();

builder.Logging.ClearProviders();
builder.Services.AddSerilog();

// Configurações do Synapse
var configManager = new Synapse.Sync.Config.SynapseConfigManager();
var savedConfig = configManager.LoadAsync().GetAwaiter().GetResult();

var synapseSection = builder.Configuration.GetSection("Synapse");
var vaultPath = !string.IsNullOrWhiteSpace(savedConfig.VaultPath)
    ? savedConfig.VaultPath
    : (synapseSection["VaultPath"] ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "SynapseVault"));

var dbPath = !string.IsNullOrWhiteSpace(savedConfig.DatabasePath)
    ? savedConfig.DatabasePath
    : (synapseSection["DatabasePath"] ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Synapse", "synapse.db"));

var gitHubConfig = new GitHubClientConfig
{
    Owner = !string.IsNullOrWhiteSpace(savedConfig.Owner) ? savedConfig.Owner : (synapseSection.GetSection("GitHub")["Owner"] ?? string.Empty),
    Repository = !string.IsNullOrWhiteSpace(savedConfig.Repository) ? savedConfig.Repository : (synapseSection.GetSection("GitHub")["Repository"] ?? "Synapse-Vault"),
    Branch = !string.IsNullOrWhiteSpace(savedConfig.Branch) ? savedConfig.Branch : (synapseSection.GetSection("GitHub")["Branch"] ?? "main")
};

builder.Services.AddSingleton(gitHubConfig);

// Registro das Portas e Adaptadores Hexagonais
builder.Services.AddSingleton<ISyncIndexStore>(SqliteSyncIndexStore.ForFile(dbPath));
builder.Services.AddSingleton<IFileSystem, LocalFileSystem>();
builder.Services.AddSingleton<IConflictResolver, ConflictResolver>();
builder.Services.AddSingleton<ITokenStore, DpapiTokenStore>();
builder.Services.AddSingleton<IRuleEngine>(sp =>
    new RuleEngine(sp.GetRequiredService<IFileSystem>(), vaultPath, sp.GetRequiredService<TimeProvider>()));

builder.Services.AddSingleton<HttpClient>();
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddSingleton<GitHubAuthManager>();
builder.Services.AddSingleton<ICloudProvider, GitHubProvider>();
builder.Services.AddSingleton<IVaultWatcher, FileWatcherService>();

// Registro do Worker Principal
builder.Services.AddHostedService<Worker>();

var host = builder.Build();

try
{
    host.Run();
}
finally
{
    Log.CloseAndFlush();
}

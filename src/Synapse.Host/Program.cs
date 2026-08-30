using Serilog;
using Serilog.Settings.Configuration;
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
// ConfigurationReaderOptions com assemblies explícitos é necessário porque o Host é
// publicado como single-file (PublishSingleFile=true) — nesse modo o AssemblyFinder
// do Serilog.Settings.Configuration não consegue localizar os assemblies dos sinks
// (Console/File) via reflection normal, e a leitura da seção "Serilog" do
// appsettings.json falha com "No Serilog:Using configuration section is defined".
var serilogReaderOptions = new ConfigurationReaderOptions(
    typeof(Serilog.ConsoleLoggerConfigurationExtensions).Assembly,
    typeof(Serilog.FileLoggerConfigurationExtensions).Assembly);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration, serilogReaderOptions)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .CreateLogger();

builder.Logging.ClearProviders();
builder.Services.AddSerilog();

// Configurações do Synapse
var configManager = new Synapse.Sync.Config.SynapseConfigManager();
var savedConfig = configManager.LoadAsync().GetAwaiter().GetResult();

var hostPaths = SynapseHostPaths.Resolve(savedConfig, builder.Configuration);
var vaultPath = hostPaths.VaultPath;
var dbPath = hostPaths.DatabasePath;

var synapseSection = builder.Configuration.GetSection("Synapse");
var gitHubConfig = new GitHubClientConfig
{
    Owner = !string.IsNullOrWhiteSpace(savedConfig.Owner) ? savedConfig.Owner : (!string.IsNullOrWhiteSpace(synapseSection.GetSection("GitHub")["Owner"]) ? synapseSection.GetSection("GitHub")["Owner"]! : string.Empty),
    Repository = !string.IsNullOrWhiteSpace(savedConfig.Repository) ? savedConfig.Repository : (!string.IsNullOrWhiteSpace(synapseSection.GetSection("GitHub")["Repository"]) ? synapseSection.GetSection("GitHub")["Repository"]! : "Synapse-Vault"),
    Branch = !string.IsNullOrWhiteSpace(savedConfig.Branch) ? savedConfig.Branch : (!string.IsNullOrWhiteSpace(synapseSection.GetSection("GitHub")["Branch"]) ? synapseSection.GetSection("GitHub")["Branch"]! : "main")
};

builder.Services.AddSingleton(hostPaths);
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
builder.Services.AddSingleton<RecentSelfWriteTracker>();

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

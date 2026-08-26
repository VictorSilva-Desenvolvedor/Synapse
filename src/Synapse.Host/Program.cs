using Serilog;
using Synapse.Host;

var builder = Host.CreateApplicationBuilder(args);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .CreateLogger();

builder.Logging.ClearProviders();
builder.Services.AddSerilog();

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

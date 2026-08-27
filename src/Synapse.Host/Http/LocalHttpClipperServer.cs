using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Synapse.Brain.Services;
using Synapse.Sync.Config;

namespace Synapse.Host.Http;

/// <summary>
/// Micro-servidor HTTP local para captura de artigos da web via navegador ou bookmarklet (V5.4).
/// Escuta exclusivamente em loopback (127.0.0.1:57412).
/// </summary>
public sealed class LocalHttpClipperServer : IDisposable
{
    public const int DefaultPort = 57412;
    private readonly HttpListener _listener;
    private readonly WebClipperService _clipperService;
    private readonly SynapseConfigManager _configManager;
    private readonly ILogger<LocalHttpClipperServer>? _logger;
    private CancellationTokenSource? _cts;
    private Task? _listenerTask;

    public LocalHttpClipperServer(
        WebClipperService clipperService,
        SynapseConfigManager configManager,
        ILogger<LocalHttpClipperServer>? logger = null)
    {
        _clipperService = clipperService;
        _configManager = configManager;
        _logger = logger;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{DefaultPort}/");
    }

    public void Start()
    {
        try
        {
            _listener.Start();
            _cts = new CancellationTokenSource();
            _listenerTask = Task.Run(() => ListenLoopAsync(_cts.Token));
            _logger?.LogInformation("Servidor Local de Web Clipper iniciado em http://127.0.0.1:{Port}/", DefaultPort);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Não foi possível iniciar o servidor HTTP do Web Clipper na porta {Port}", DefaultPort);
        }
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleRequestAsync(context, ct), ct);
            }
            catch (HttpListenerException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Erro no loop do Web Clipper HTTP");
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken ct)
    {
        var req = context.Request;
        var res = context.Response;

        // Headers CORS
        res.AddHeader("Access-Control-Allow-Origin", "*");
        res.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        res.AddHeader("Access-Control-Allow-Headers", "Content-Type");

        if (req.HttpMethod == "OPTIONS")
        {
            res.StatusCode = (int)HttpStatusCode.OK;
            res.Close();
            return;
        }

        try
        {
            if (req.HttpMethod == "GET" && req.Url?.AbsolutePath == "/health")
            {
                await WriteJsonResponseAsync(res, new { status = "ok", service = "Synapse Web Clipper" });
                return;
            }

            if (req.HttpMethod == "POST" && req.Url?.AbsolutePath == "/clip")
            {
                using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
                var body = await reader.ReadToEndAsync(ct);
                using var doc = JsonDocument.Parse(body);

                var url = doc.RootElement.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                var title = doc.RootElement.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                var content = doc.RootElement.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";

                var config = await _configManager.LoadAsync();
                if (string.IsNullOrEmpty(config.VaultPath))
                {
                    res.StatusCode = (int)HttpStatusCode.BadRequest;
                    await WriteJsonResponseAsync(res, new { error = "Cofre não configurado no Synapse." });
                    return;
                }

                var relativePath = await _clipperService.ClipWebPageAsync(url, title, content, config.VaultPath, ct);
                await WriteJsonResponseAsync(res, new { success = true, file = relativePath });
                return;
            }

            res.StatusCode = (int)HttpStatusCode.NotFound;
            res.Close();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Erro ao processar requisição do Web Clipper");
            res.StatusCode = (int)HttpStatusCode.InternalServerError;
            await WriteJsonResponseAsync(res, new { error = ex.Message });
        }
    }

    private static async Task WriteJsonResponseAsync(HttpListenerResponse res, object data)
    {
        var json = JsonSerializer.Serialize(data);
        var bytes = Encoding.UTF8.GetBytes(json);
        res.ContentType = "application/json; charset=utf-8";
        res.ContentLength64 = bytes.Length;
        await res.OutputStream.WriteAsync(bytes);
        res.Close();
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try { _listener.Stop(); } catch { }
        try { _listener.Close(); } catch { }
    }
}

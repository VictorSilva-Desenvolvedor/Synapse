using System.Net;
using Synapse.Core.Ports;

namespace Synapse.Sync.GitHub;

/// <summary>
/// Mapeia respostas de erro da GitHub REST API v3 e de rede para as exceções canônicas de domínio
/// definidas em Synapse.Core.Ports (RF-SYNC.6, RNF-6, RNF-8).
/// </summary>
public static class GitHubExceptionMapper
{
    public static Exception Map(HttpResponseMessage response, string? responseBody = null)
    {
        var statusCode = response.StatusCode;

        if (statusCode == HttpStatusCode.Unauthorized) // 401
        {
            return new CloudAuthExpiredException("GitHub API: autenticação inválida ou token revogado (401).");
        }

        if (statusCode == HttpStatusCode.NotFound) // 404
        {
            return new CloudNotFoundException($"GitHub API: recurso não encontrado (404). {responseBody}");
        }

        if (statusCode == HttpStatusCode.Forbidden || (int)statusCode == 429) // 403 / 429
        {
            var isRateLimit = response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) &&
                              remaining.FirstOrDefault() == "0";

            if (isRateLimit || (responseBody?.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ?? false))
            {
                return new CloudQuotaExceededException("GitHub API: limite de requisições por hora excedido.");
            }

            return new CloudQuotaExceededException($"GitHub API: acesso negado ou limite de taxa atingido ({(int)statusCode}). {responseBody}");
        }

        if ((int)statusCode >= 500 && (int)statusCode <= 599) // 5xx
        {
            return new CloudTransientException($"GitHub API: erro transitório do servidor ({statusCode}). {responseBody}");
        }

        return new InvalidOperationException($"GitHub API: erro inesperado ({statusCode}): {responseBody}");
    }

    public static Exception Map(Exception exception)
    {
        if (exception is HttpRequestException or TimeoutException or IOException or TaskCanceledException)
        {
            return new CloudTransientException("Erro transitório de rede ou comunicação com a GitHub API.", exception);
        }

        return exception;
    }
}

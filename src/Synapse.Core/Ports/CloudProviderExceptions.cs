namespace Synapse.Core.Ports;

/// <summary>Token expirado/revogado (401). Tratamento esperado: dispara renovação (RF-AUTH.3); se falhar, estado AuthRequired.</summary>
public sealed class CloudAuthExpiredException : Exception
{
    public CloudAuthExpiredException() { }
    public CloudAuthExpiredException(string message) : base(message) { }
    public CloudAuthExpiredException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>Cota excedida (403/429). Tratamento esperado: backoff exponencial com jitter (RF-SYNC.6).</summary>
public sealed class CloudQuotaExceededException : Exception
{
    public CloudQuotaExceededException() { }
    public CloudQuotaExceededException(string message) : base(message) { }
    public CloudQuotaExceededException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>Erro 5xx / timeout de rede. Tratamento esperado: backoff exponencial com jitter (RF-SYNC.6).</summary>
public sealed class CloudTransientException : Exception
{
    public CloudTransientException() { }
    public CloudTransientException(string message) : base(message) { }
    public CloudTransientException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>Arquivo remoto não existe mais (404). Tratamento esperado: tratado como exclusão remota.</summary>
public sealed class CloudNotFoundException : Exception
{
    public CloudNotFoundException() { }
    public CloudNotFoundException(string message) : base(message) { }
    public CloudNotFoundException(string message, Exception innerException) : base(message, innerException) { }
}

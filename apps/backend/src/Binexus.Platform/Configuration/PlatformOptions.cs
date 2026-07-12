using System.ComponentModel.DataAnnotations;

namespace Binexus.Platform.Configuration;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    [Required]
    public string ConnectionString { get; init; } = string.Empty;
}

public sealed class CorsOptions
{
    public const string SectionName = "Cors";

    [Required]
    [MinLength(1)]
    public string[] AllowedOrigins { get; init; } = [];
}

public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    public int MaxRequestBodyBytes { get; init; } = 1_048_576;

    public string[] TrustedProxies { get; init; } = [];

    /// <summary>CIDR notation, e.g. 10.0.0.0/8</summary>
    public string[] TrustedNetworks { get; init; } = [];
}

public sealed class OutboxWorkerOptions
{
    public const string SectionName = "OutboxWorker";

    public TimeSpan LockDuration { get; init; } = TimeSpan.FromMinutes(2);

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);

    public int MaxAttemptsTransient { get; init; } = 10;
}

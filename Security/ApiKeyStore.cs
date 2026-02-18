namespace OCRServer.Security;

/// <summary>
/// Loads API keys once at startup and keeps per-client settings in memory.
/// </summary>
public sealed class ApiKeyStore
{
    private readonly Dictionary<string, ApiKeyClient> _byKey;

    public ApiKeyStore(IConfiguration configuration, ILogger<ApiKeyStore> logger)
    {
        _byKey = new Dictionary<string, ApiKeyClient>(StringComparer.Ordinal);

        var apiKeysSection = configuration.GetSection("ApiKeys");
        foreach (var clientSection in apiKeysSection.GetChildren())
        {
            var name = clientSection.Key;
            var key = clientSection["Key"];

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(key))
            {
                logger.LogWarning("ApiKeys entry missing Name/Key; section={Section}", clientSection.Path);
                continue;
            }

            int rpm = TryParseInt(clientSection["RequestsPerMinute"], fallback: 30);
            int maxConcurrent = TryParseInt(clientSection["MaxConcurrent"], fallback: 1);

            if (rpm <= 0) rpm = 1;
            if (maxConcurrent <= 0) maxConcurrent = 1;

            if (_byKey.ContainsKey(key))
                throw new InvalidOperationException("Duplicate API key configured under ApiKeys. Keys must be unique.");

            _byKey[key] = new ApiKeyClient(
                Name: name,
                Key: key,
                RequestsPerMinute: rpm,
                MaxConcurrent: maxConcurrent,
                Concurrency: new SemaphoreSlim(maxConcurrent, maxConcurrent));
        }

        logger.LogInformation("Loaded {Count} API key client(s).", _byKey.Count);
    }

    public bool TryResolve(string presentedKey, out ApiKeyClient client)
        => _byKey.TryGetValue(presentedKey, out client!);

    private static int TryParseInt(string? value, int fallback)
        => int.TryParse(value, out var v) ? v : fallback;
}

public sealed record ApiKeyClient(
    string Name,
    string Key,
    int RequestsPerMinute,
    int MaxConcurrent,
    SemaphoreSlim Concurrency);

public static class HttpContextItemKeys
{
    public const string OcrClient = "ocr.client";
    public const string OcrFileSizeBytes = "ocr.fileSizeBytes";
    public const string OcrPageCount = "ocr.pageCount";
    public const string OcrRejectedReason = "ocr.rejectedReason";
}


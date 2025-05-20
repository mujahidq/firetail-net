using Microsoft.OpenApi;

namespace Firetail;

public class FiretailOptions
{
    public string? ApiDocPath { get; set; }
    public string? FiretailAPIKey { get; set; }
    public string? FiretailAPIHost { get; set; }
    public int LogMaxItems { get; set; } = 1000;
    public int LogMaxSize { get; set; } = 950_000;
    public int LogMaxTimeMs { get; set; } = 5 * 1000;
    public string BasePath { get; set; } = string.Empty;
    public OpenApiSpecVersion SchemaVersion { get; internal set; }
    public string[] SensitiveHeaders { get; set; } = [];
}

using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Models.Interfaces;
using System.Diagnostics;

namespace Firetail;

internal class FiretailContext
{
    public string? MatchedPath { get; internal set; }
    public OpenApiOperation? Operation { get; internal set; }
    public Dictionary<string, string> PathParameters { get; internal set; } = [];
    public List<LogEntry> Observations { get; internal set; } = [];
    public Stopwatch LatencyTimer { get; } = Stopwatch.StartNew();
    public int OriginalStatusCode { get; internal set; } = 500;
    public string? OriginalResponseBody { get; internal set; }
    public bool ResponseSanitised { get; internal set; } 
    public string? RequestBody { get; internal set; }
    public bool ResponseValidated { get; internal set; }
    public bool ResponseModified { get; internal set; }
    public bool Intercepted { get; internal set; }
    public string? Method { get; internal set; }
    public IOpenApiPathItem? Match { get; internal set; }
    public string[] SensitiveHeaders { get; set; } = [];
}
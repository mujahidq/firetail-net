using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.VisualBasic;
using System.Net.NetworkInformation;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Firetail;

internal static class FiretailLog
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    public const string VERSION = "1.0.0-alpha";
    public const string LOG_ACTION_BLOCKED = "blocked";
    public const string LOG_ACTION_MODIFIED = "modified";
    public const string LOG_ACTION_INFORMED = "informed";

    private static string macAddress = NetworkInterface
       .GetAllNetworkInterfaces()
       .Where(nic =>
           nic.OperationalStatus == OperationalStatus.Up &&
           nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
       .Select(nic => nic.GetPhysicalAddress().ToString())
       .FirstOrDefault() ?? string.Empty;

    public static string CreateLogEntry(HttpContext httpContext, FiretailContext firetailContext, FiretailOptions firetailOptions)
    {
        string logAction = LOG_ACTION_INFORMED;
        if (firetailContext.ResponseSanitised && firetailContext.ResponseModified)
        {
            logAction = LOG_ACTION_MODIFIED;
        }
        if (firetailContext.Intercepted)
        {
            logAction = LOG_ACTION_BLOCKED;
        }

        var executionTime = firetailContext.LatencyTimer.Elapsed;

        var req = httpContext.Request;
        var res = httpContext.Response;
        var netName = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
        var netVersion = Environment.Version.ToString();

        var ip = req.Headers["x-forwarded-for"].FirstOrDefault() ?? req.HttpContext.Connection.RemoteIpAddress?.ToString();

        var (requestHeaders, responseHeaders) = HeaderSanitizer.GetSanitizedHeaders(httpContext, firetailContext);

        var observations = firetailContext.Observations.Select(o => new ObservationEntity
        {
            Type = o.Type ?? string.Empty,
            Title = o.Title ?? string.Empty,
            Status = o.Status,
            Details = JsonSerializer.Serialize(o.Details, _jsonOptions)
        }).ToList();

        var logEntry = new LogEntryEntity
        {
            Version = VERSION,
            LogAction = logAction,
            DateCreated = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ExecutionTime = executionTime.Milliseconds,
            Request = new RequestEntity
            {
                HttpProtocol = $"HTTP/{req.Protocol.Split('/').Last()}",
                Uri = httpContext.Request.GetDisplayUrl(),
                Resource = firetailContext.MatchedPath ?? string.Empty,
                Headers = requestHeaders,
                Method = firetailContext.Method?.ToUpper() ?? string.Empty,
                Body = string.IsNullOrEmpty(firetailContext.RequestBody) ? string.Empty : firetailContext.RequestBody,
                Ip = ip,
            },
            Response = new ResponseEntity
            {
                StatusCode = res.StatusCode,
                OriginalStatusCode = firetailContext.OriginalStatusCode,
                Body = string.Empty,
                OriginalBody = firetailContext.OriginalResponseBody ?? string.Empty,
                Headers = responseHeaders
            },
            Metadata = new MetadataEntity
            {
                LibraryVersion = FiretailVersion.Version,
                SoftwareVersion = $"{netName} {netVersion}",
                Hostname = req.Host.Host,
                LocalPort = req.Host.Port?.ToString() ?? "defaultPort",
                LocalIP = ip,
                MacAddress = macAddress
            },
            Observations = observations
        };

        return JsonSerializer.Serialize(logEntry, _jsonOptions);
    }
}



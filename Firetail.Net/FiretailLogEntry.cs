using System.Text.Json.Serialization;

namespace Firetail;
internal static class LoggerPrefixes
{
    public static readonly string Response = "[FireTail Response Validation Setup]";
    public static readonly string Request = "[FireTail Request Validation Setup]";

}
internal record ErrorDetails
{
   [JsonPropertyName("message")]
    public string? Message { get; init; }
    [JsonPropertyName("accept")]
    public string? Accept { get; init; }
    [JsonPropertyName("contenttype")]
    public string? ContentType { get; init; }
    [JsonPropertyName("status")]
    public int Status { get; init; }
}

internal record LogEntry
{
    public int Status { get; init; }
    public string? Type { get; init; }
    public string? Title { get; init; }
    public List<ErrorDetails>? Details { get; init; }
}
    internal class LogEntryEntity
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0-alpha";
    [JsonPropertyName("logAction")]
    public string LogAction { get; set; } = default!;

    [JsonPropertyName("dateCreated")]
    public long DateCreated { get; set; }

    [JsonPropertyName("executionTime")]
    public double ExecutionTime { get; set; }

    [JsonPropertyName("request")]
    public RequestEntity Request { get; set; } = new();

    [JsonPropertyName("response")]
    public ResponseEntity Response { get; set; } = new();

    [JsonPropertyName("metadata")]
    public MetadataEntity Metadata { get; set; } = new();

    [JsonPropertyName("oauth")]
    public OAuthEntity? OAuth { get; set; } = new();

    [JsonPropertyName("observations")]
    public List<ObservationEntity> Observations { get; set; } = new();
}

internal class OAuthEntity
{
    [JsonPropertyName("subject")]
    public string? Subject { get; set; } = string.Empty;

        [JsonPropertyName("sub")]
    public string? Sub { get; set; } = string.Empty;
}
internal class RequestEntity
{
    [JsonPropertyName("httpProtocol")]
    public string HttpProtocol { get; set; } = default!;

    [JsonPropertyName("uri")]
    public string Uri { get; set; } = default!;

    [JsonPropertyName("resource")]
    public string Resource { get; set; } = default!;

    [JsonPropertyName("headers")]
    public Dictionary<string, List<string>> Headers { get; set; } = new();

    [JsonPropertyName("method")]
    public string Method { get; set; } = default!;

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("ip")]
    public string? Ip { get; set; } = default!;
}

internal class ResponseEntity
{
    [JsonPropertyName("statusCode")]
    public int StatusCode { get; set; }

    [JsonPropertyName("originalStatusCode")]
    public int OriginalStatusCode { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("originalBody")]
    public string? OriginalBody { get; set; }

    [JsonPropertyName("headers")]
    public Dictionary<string, List<string>> Headers { get; set; } = new();

    [JsonPropertyName("originalHeaders")]
    public Dictionary<string, List<string>>? OriginalHeaders { get; set; } = new();
}

internal class MetadataEntity
{
    [JsonPropertyName("libraryVersion")]
    public string? LibraryVersion { get; set; }

    [JsonPropertyName("softwareVersion")]
    public string? SoftwareVersion { get; set; }

    [JsonPropertyName("localIP")]
    public string? LocalIP { get; set; }

    [JsonPropertyName("localPort")]
    public string? LocalPort { get; set; }

    [JsonPropertyName("hostname")]
    public string? Hostname { get; set; }

    [JsonPropertyName("macaddress")]
    public string? MacAddress { get; set; }
}

internal class ObservationEntity
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = default!;

    [JsonPropertyName("details")]
    public string? Details { get; set; }

    [JsonPropertyName("status")]
    public int? Status { get; set; }
}
using Microsoft.OpenApi.Models.Interfaces;
using Microsoft.OpenApi.Models;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Firetail;

internal static class Extensions
{
    /// <summary>
    /// Serializes the LogEntry object to a camelCase JSON string.
    /// </summary>
    /// <param name="logEntry">The LogEntry instance.</param>
    /// <param name="indented">Whether to pretty-print the JSON output.</param>
    /// <returns>Serialized JSON string in camelCase format.</returns>
    public static string ToJson(this LogEntry logEntry, bool indented = false)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = indented
        };

        return JsonSerializer.Serialize(logEntry, options);
    }

    public static string GetBasePath(this OpenApiDocument schema)
    {
        var basePath = string.Empty;
        if (schema.Servers?.FirstOrDefault()?.Url is { } serverUrl &&
            Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.LocalPath))
        {
            basePath = uri.LocalPath == "/" ? "" : uri.LocalPath;
        }

        return basePath;
    }


    public static (IOpenApiPathItem? match, Dictionary<string, string> properties, string matchedPath) MatchPath(this OpenApiDocument spec, string requestPath, string basePath)
    {
        requestPath = requestPath.TrimEnd('/');

        foreach (var path in spec.Paths)
        {
            var fullPathPattern = "^/?(?:" + Regex.Escape(basePath?.TrimStart('/') ?? "") + "/)?" +
         Regex.Replace(path.Key.TrimStart('/'), @"\{([^/{}]+)\}", @"(?<$1>[^/]+)") +
         "$";
            var match = Regex.Match(requestPath, fullPathPattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                // Extract matched parameters using named groups
                var parameters = match.Groups
                                      .Cast<Group>()
                                      .Where(g => g.Success && g.Name != "0")
                                      .ToDictionary(g => g.Name, g => g.Value);

                return (path.Value, parameters, path.Key.Replace("/", "."));
            }
        }
        return (null, [], string.Empty);
    }

    public static OpenApiOperation? GetOperation(this IOpenApiPathItem path, string method)
    {
        OperationType? operationType = method switch
        {
            "get" => OperationType.Get,
            "post" => OperationType.Post,
            "put" => OperationType.Put,
            "patch" => OperationType.Patch,
            "delete" => OperationType.Delete,
            _ => null
        };

        if (operationType.HasValue &&
            path.Operations.TryGetValue(operationType.Value, out var operation))
        {
            return operation;
        }

        return null;
    }
}

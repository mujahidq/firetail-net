using Microsoft.AspNetCore.Http;
using Microsoft.OpenApi.Models;

namespace Firetail;

internal static class HeaderSanitizer
{
    private const string CLEAN = "*****";

    /// <summary>
    /// These headers typically contain sensitive information and should not be logged.
    /// </summary>
    private static readonly HashSet<string> DefaultHiddenRequestHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "authorization",
        "proxy-authorization"
    };

    private static readonly HashSet<string> DefaultHiddenResponseHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "www-authenticate",
        "proxy-authenticate"
    };

    /// <summary>
    /// Gets sanitized headers for both request and response.
    /// </summary>
    /// <param name="context">The Firetail context containing request, response, and schema information.</param>
    /// <returns>A tuple containing sanitized request and response headers.</returns>
    public static (Dictionary<string, List<string>> RequestHeaders, Dictionary<string, List<string>> ResponseHeaders) GetSanitizedHeaders(HttpContext httpContext, FiretailContext firetailContext)
    {
        var hiddenRequestHeaders = new HashSet<string>(DefaultHiddenRequestHeaders, StringComparer.OrdinalIgnoreCase);
        var hiddenResponseHeaders = new HashSet<string>(DefaultHiddenResponseHeaders, StringComparer.OrdinalIgnoreCase);

        // Add headers from schema
        var schema = firetailContext.Match;
        var headersFromSchema = schema?.Extensions
            .Where(x => x.Key == "x-ft-sensitive-headers")
            .SelectMany(x => x.Value as IEnumerable<string> ?? Array.Empty<string>());

        if (headersFromSchema?.Any() == true)
        {
            hiddenRequestHeaders.UnionWith(headersFromSchema);
            hiddenResponseHeaders.UnionWith(headersFromSchema);
        }

        // Sanitize request headers
        var requestHeaders = new Dictionary<string, List<string>>();
        foreach (var header in httpContext.Request.Headers)
        {
            var clean = hiddenRequestHeaders.Contains(header.Key.ToLowerInvariant()) 
                ? CLEAN 
                : header.Value.ToString();

            requestHeaders[header.Key] = new List<string> { clean };
        }

        // Sanitize response headers
        var responseHeaders = new Dictionary<string, List<string>>();
        foreach (var header in httpContext.Response.Headers)
        {
            var clean = hiddenResponseHeaders.Contains(header.Key.ToLowerInvariant()) 
                ? CLEAN 
                : header.Value.ToString();

            responseHeaders[header.Key] = new List<string> { clean };
        }

        return (requestHeaders, responseHeaders);
    }
} 
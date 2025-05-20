using Microsoft.AspNetCore.Http;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Models.Interfaces;
using System.Net;
using System.Text.Json;

namespace Firetail;

internal class FiretailResponseValidator
{
    public static List<LogEntry> Validate(HttpContext httpContext, string responseBody, FiretailContext firetailContext)
    {
        var (accept, acceptedTypes) = GetAcceptHeaderInfo(httpContext);
        var responseObject = TryParseJson(responseBody);
        var contentType = GetResponseContentType(httpContext);

        firetailContext.OriginalStatusCode = httpContext.Response.StatusCode;
        firetailContext.OriginalResponseBody = responseBody;
        firetailContext.ResponseSanitised = true;

        return ValidateResponse(firetailContext, responseObject, accept, acceptedTypes, contentType);
    }

    private static List<LogEntry> ValidateResponse(FiretailContext firetailContext, (JsonElement? jsonElement, Exception? exception) responseObject, string? accept, List<string> acceptedTypes, string contentType)
    {
        if (firetailContext?.Operation == null) return [];

        var errors = new List<LogEntry>();

        if (string.IsNullOrEmpty(accept))
        {
            errors.Add(LogMissingAcceptHeader());
        }
        else if (!acceptedTypes.Contains(contentType))
        {
            errors.Add(LogIncorrectContentType(accept, contentType));
            return errors;
        }

        var expectedContent = GetExpectedContentType(firetailContext, contentType);
        if (expectedContent == null) return errors;

        if (responseObject.exception != null)
        {
            errors.Add(LogSanitizationFailure(responseObject.exception, accept, contentType));
            return errors;
        }

        var validationErrors = ValidateResponseBody(responseObject.jsonElement!.Value, firetailContext.MatchedPath!, expectedContent.Schema!);
        if (validationErrors.Any())
        {
            errors.Add(LogValidationFailure(validationErrors));
        }

        return errors;
    }

    private static OpenApiMediaType? GetExpectedContentType(FiretailContext firetailContext, string contentType)
    {
        if (firetailContext.Operation!.Responses != null && (firetailContext.Operation.Responses.TryGetValue(firetailContext.OriginalStatusCode.ToString(), out var response) ||
            firetailContext.Operation.Responses.TryGetValue("default", out response)))
        {
            response.Content.TryGetValue(contentType, out var expectedContent);
            return expectedContent;
        }
        return null;
    }

    private static string GetResponseContentType(HttpContext httpContext) =>
        httpContext.Response.ContentType?.Split(';')[0] ?? "application/json";

    private static List<ErrorDetails> ValidateResponseBody(JsonElement jsonElement, string path, IOpenApiSchema schema)
    {
        var errors = new List<ErrorDetails>();
        ValidateSchema(schema, jsonElement, path, errors);
        return errors;
    }

    private static LogEntry LogMissingAcceptHeader() => new()
    {
        Type = "firetail.request.accept.header.missing",
        Title = $"{LoggerPrefixes.Response} The Accept header is missing from the request"
    };

    private static LogEntry LogIncorrectContentType(string accept, string? contentType) => new()
    {
        Status = (int)HttpStatusCode.NotAcceptable,
        Type = "firetail.incorrect.response.content.type",
        Title = "Incorrect response format",
        Details =
        [
            new()
            {
                Message = $"{LoggerPrefixes.Response} Response content type {contentType} not found in Accept header: {accept}",
                Accept = accept,
                ContentType = contentType ?? string.Empty
            }
        ]
    };

    private static LogEntry LogValidationFailure(List<ErrorDetails> validationErrors) => new()
    {
        Status = StatusCodes.Status500InternalServerError,
        Type = "firetail.response.validation.failed",
        Title = $"{LoggerPrefixes.Response} Failed to validate response",
        Details = validationErrors
    };

    private static LogEntry LogSanitizationFailure(Exception exception, string? accept, string? contentType) => new LogEntry
    {
        Status = StatusCodes.Status500InternalServerError,
        Type = "firetail.response.sanitisation.failed",
        Title = $"{LoggerPrefixes.Response} Failed to sanitise response",
        Details =
    [
        new ErrorDetails
        {
            Message = exception!.Message,
            Accept = accept ?? string.Empty,
            ContentType = contentType ?? string.Empty
        }
    ]
    };


    private static (string? RawAcceptHeader, List<string> ContentTypes) GetAcceptHeaderInfo(HttpContext httpContext)
    {
        var acceptHeader = httpContext.Request.Headers.TryGetValue("Accept", out var values) ? values.ToString() : string.Empty;
        if (acceptHeader == "*/*") acceptHeader = "application/json";

        var contentTypes = acceptHeader.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        return (acceptHeader, contentTypes);
    }

    private static (JsonElement? jsonElement, Exception? exception) TryParseJson(string result)
    {
        try
        {
            var jsonElement = JsonDocument.Parse(result).RootElement;
            return (jsonElement, null);
        }
        catch (Exception ex)
        {
            return (null, ex);
        }
    }

    private static void ValidateSchema(IOpenApiSchema schema, JsonElement element, string path, List<ErrorDetails> errors)
    {
        switch (schema.Type)
        {
            case JsonSchemaType.Object:
                ValidateObject(schema, element, path, errors);
                break;
            case JsonSchemaType.Array:
                ValidateArray(schema, element, path, errors);
                break;
            default:
                ValidatePrimitive(schema, element, path, errors);
                break;
        }
    }

    private static void ValidateObject(IOpenApiSchema schema, JsonElement element, string path, List<ErrorDetails> errors)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new() { Message = $"{LoggerPrefixes.Response} {path} should be an Object." });
            return;
        }

        foreach (var requiredProperty in schema.Required)
        {
            if (!element.TryGetProperty(requiredProperty, out _))
            {
                errors.Add(new() { Message = $"{LoggerPrefixes.Response} {path}.{requiredProperty} is required but missing." });
            }
        }

        foreach (var property in schema.Properties)
        {
            if (element.TryGetProperty(property.Key, out var propertyElement))
            {
                ValidateSchema(property.Value, propertyElement, $"{path}.{property.Key}", errors);
            }
        }
    }

    private static void ValidateArray(IOpenApiSchema schema, JsonElement element, string path, List<ErrorDetails> errors)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            errors.Add(new() { Message = $"{LoggerPrefixes.Response} {path} should be an array." });
            return;
        }

        foreach (var item in element.EnumerateArray())
        {
            ValidateSchema(schema.Items, item, $"{path}[]", errors);
        }
    }

    private static void ValidatePrimitive(IOpenApiSchema schema, JsonElement element, string path, List<ErrorDetails> errors)
    {
        var expectedType = schema.Type;
        var actualType = element.ValueKind switch
        {
            JsonValueKind.String => JsonSchemaType.String,
            JsonValueKind.Number => JsonSchemaType.Number,
            JsonValueKind.True or JsonValueKind.False => JsonSchemaType.Boolean,
            JsonValueKind.Null => JsonSchemaType.Null,
            _ => JsonSchemaType.Object
        };

        bool isNumericCompatible = (expectedType == JsonSchemaType.Number && actualType == JsonSchemaType.Integer) || (expectedType == JsonSchemaType.Integer && actualType == JsonSchemaType.Number);

        if (expectedType != actualType && !isNumericCompatible)
        {
            errors.Add(new() { Message = $"{LoggerPrefixes.Response} {path} expected '{expectedType}', but got '{actualType}'." });
        }
    }
}

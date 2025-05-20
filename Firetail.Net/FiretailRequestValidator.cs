using Firetail;
using Microsoft.AspNetCore.Http;
using Microsoft.OpenApi.Models.Interfaces;
using Microsoft.OpenApi.Models;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text;
internal class FiretailRequestValidator
{
    public static List<ErrorDetails> Validate(HttpRequest request, string requestBody, FiretailContext firetailContext)
    {
        var errors = new List<ErrorDetails>();
        var operation = firetailContext.Operation!;

        if (operation.Parameters!.Any())
        {
            ValidateParameters(operation.Parameters!, firetailContext, request, errors);
        }

        ValidateContentType(operation, request, errors);

        if (operation.RequestBody != null && request.ContentLength > 0)
        {
            ValidateRequestBody(operation, request, requestBody, errors);
        }
        return errors;
    }

    private static void ValidateParameters(IList<IOpenApiParameter> parameters, FiretailContext firetailContext, HttpRequest request, List<ErrorDetails> errors)
    {
        foreach (var param in parameters)
        {
            string? value = param.In switch
            {
                ParameterLocation.Path => firetailContext.PathParameters.TryGetValue(param.Name, out var pathValue) ? pathValue : null,
                ParameterLocation.Query => request.Query[param.Name].FirstOrDefault(),
                ParameterLocation.Header => request.Headers.TryGetValue(param.Name, out var headerValue) ? headerValue.ToString() : null,
                ParameterLocation.Cookie => request.Cookies.TryGetValue(param.Name, out var cookieValue) ? cookieValue : null,
                _ => null
            };

            if (value == null)
            {
                if (param.Required)
                {
                    errors.Add(new ErrorDetails
                    {
                        Status = StatusCodes.Status400BadRequest,
                        Message = $"{LoggerPrefixes.Request} Missing {param.In} parameter: {param.Name}"
                    });
                }
                continue;
            }

            ValidateParameterSchema(value, param.Schema, param.Name, param.In.ToString()!, errors);
        }
    }

    private static void ValidateParameterSchema(string value, IOpenApiSchema schema, string paramName, string paramLocation, List<ErrorDetails> errors)
    {
        if (!ValidateParameterType(value, schema.Type!.Value.ToString()))
        {
            errors.Add(new ErrorDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Message = $"{LoggerPrefixes.Request} Invalid {paramLocation} parameter type for '{paramName}'. Expected {schema.Type}."
            });
            return;
        }

        if (schema.Enum != null && schema.Enum.Any())
        {
            var allowedValues = schema.Enum.Select(e => e.ToString()).ToList();
            if (!allowedValues.Contains(value))
            {
                errors.Add(new ErrorDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Message = $"{LoggerPrefixes.Request} {paramLocation} parameter '{paramName}' must be one of: {string.Join(", ", allowedValues)}"
                });
            }
        }

        if (!string.IsNullOrEmpty(schema.Pattern) && !Regex.IsMatch(value, schema.Pattern))
        {
            errors.Add(new ErrorDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Message = $"{LoggerPrefixes.Request} {paramLocation} parameter '{paramName}' does not match required pattern: {schema.Pattern}"
            });
        }

        if (schema.Minimum.HasValue && double.TryParse(value, out var numericValue) && numericValue < Convert.ToDouble(schema.Minimum.Value))
        {
            errors.Add(new ErrorDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Message = $"{LoggerPrefixes.Request} {paramLocation} parameter '{paramName}' must be greater than or equal to {schema.Minimum}"
            });
        }

        if (schema.Maximum.HasValue && double.TryParse(value, out numericValue) && numericValue > Convert.ToDouble(schema.Maximum.Value))
        {
            errors.Add(new ErrorDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Message = $"{LoggerPrefixes.Request} {paramLocation} parameter '{paramName}' must be less than or equal to {schema.Maximum}"
            });
        }

        if (!string.IsNullOrEmpty(schema.Format) && !ValidateFormat(value, schema.Format))
        {
            errors.Add(new ErrorDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Message = $"{LoggerPrefixes.Request} {paramLocation} parameter '{paramName}' must be a valid {schema.Format}"
            });
        }
    }

    private static void ValidateContentType(OpenApiOperation operation, HttpRequest request, List<ErrorDetails> errors)
    {
        if (operation.RequestBody != null)
        {
            var expectedContentTypes = operation.RequestBody.Content.Keys;
            if (string.IsNullOrEmpty(request.ContentType) || !expectedContentTypes.Any(ct => request.ContentType.Contains(ct)))
            {
                errors.Add(new ErrorDetails
                {
                    Status = StatusCodes.Status415UnsupportedMediaType,
                    Message = $"{LoggerPrefixes.Request} Invalid Content-Type. Expected one of: {string.Join(", ", expectedContentTypes)}"
                });
            }
        }
    }

    private static bool ContainsInvalidUnicode(string input)
    {
        try
        {
            var encoder = Encoding.GetEncoding(
                "ASCII",
                new EncoderExceptionFallback(), 
                DecoderFallback.ExceptionFallback
            );
            encoder.GetBytes(input);
            return false;
        }
        catch (EncoderFallbackException)
        {
            return true; 
        }
    }

    private static void ValidateRequestBody(OpenApiOperation operation, HttpRequest request, string requestBody, List<ErrorDetails> errors)
    {
        var contentType = request.ContentType?.Split(';')[0];
        if (contentType == null || !operation.RequestBody!.Content.TryGetValue(contentType, out var mediaType))
        {
            errors.Add(new ErrorDetails
            {
                Status = StatusCodes.Status415UnsupportedMediaType,
                Message = $"{LoggerPrefixes.Request} Unsupported content type: {contentType}"
            });
            return;
        }
        
        JsonElement bodyJson;
        try
        {
            bodyJson = JsonDocument.Parse(requestBody).RootElement;
        }
        catch (JsonException)
        {
            errors.Add(new ErrorDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Message = $"{LoggerPrefixes.Request} Malformed JSON in request body."
            });
            return;
        }

        if (ContainsInvalidUnicode(requestBody))
        {
            errors.Add(new ErrorDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Message = $"{LoggerPrefixes.Request} Unicode handling error in request body."
            });
            return;
        }

        ValidateObjectSchema(mediaType.Schema, bodyJson, "body", errors);
    }

    private static void ValidateObjectSchema(IOpenApiSchema? schema, JsonElement element, string path, List<ErrorDetails> errors)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new ErrorDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Message = $"{LoggerPrefixes.Request} Expected object at {path}, but got {element.ValueKind}"
            });
            return;
        }

        if (schema != null)
        {
            foreach (var requiredProperty in schema.Required)
            {
                if (!element.TryGetProperty(requiredProperty, out _))
                {
                    errors.Add(new ErrorDetails
                    {
                        Status = StatusCodes.Status400BadRequest,
                        Message = $"{LoggerPrefixes.Request} Missing required property: {path}.{requiredProperty}"
                    });
                }
            }

            foreach (var property in schema.Properties)
            {
                var propertyName = property.Key;
                var propertySchema = property.Value;

                if (element.TryGetProperty(propertyName, out var propertyValue))
                {
                    ValidatePropertyType(propertySchema, propertyValue, $"{path}.{propertyName}", errors);
                }
            }

            if (schema.AdditionalPropertiesAllowed == false)
            {
                foreach (var prop in element.EnumerateObject())
                {
                    if (!schema.Properties.ContainsKey(prop.Name))
                    {
                        errors.Add(new ErrorDetails
                        {
                            Status = StatusCodes.Status400BadRequest,
                            Message = $"{LoggerPrefixes.Request} Unexpected property '{prop.Name}' found in {path}"
                        });
                    }
                }
            }
        }
    }

    private static void ValidatePropertyType(IOpenApiSchema schema, JsonElement element, string path, List<ErrorDetails> errors)
    {
        var expectedType = schema.Type;
        var actualType = element.ValueKind switch
        {
            JsonValueKind.String => "string",
            JsonValueKind.Number => schema.Format == "int32" || schema.Format == "int64" ? "integer" : "number",
            JsonValueKind.True or JsonValueKind.False => "boolean",
            JsonValueKind.Array => "array",
            JsonValueKind.Object => "object",
            _ => "unknown"
        };

        if (expectedType.HasValue && !string.Equals(expectedType.Value.ToString(), actualType, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(new ErrorDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Message = $"{LoggerPrefixes.Request} Invalid type for {path}. Expected '{expectedType}', got '{actualType}'"
            });
            return;
        }

        if (schema.Enum != null && schema.Enum.Any())
        {
            var allowedValues = schema.Enum.Select(e => e.ToString()).ToList();
            if (!allowedValues.Contains(element.ToString()))
            {
                errors.Add(new ErrorDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Message = $"{LoggerPrefixes.Request} Invalid value for {path}. Expected one of: {string.Join(", ", allowedValues)}"
                });
            }
        }

        if (!string.IsNullOrEmpty(schema.Pattern)) {

            var message = $"{LoggerPrefixes.Request} Value of {path} does not match";
            if (element.ValueKind == JsonValueKind.String && !Regex.IsMatch(element.GetString()!, schema.Pattern))
            {
                message += $" pattern {schema.Pattern}";
            }
            errors.Add(new ErrorDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Message = message
            });
        }

        if (!string.IsNullOrEmpty(schema.Format))
        {
            var message = $"{LoggerPrefixes.Request} Value of {path} must be a valid {schema.Format}";
            if (element.ValueKind == JsonValueKind.String && !ValidateFormat(element.GetString()!, schema.Format))
            {
                errors.Add(new ErrorDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Message = message
                });
            }
        }
    }

    private static bool ValidateFormat(string value, string format)
    {
        return format switch
        {
            "email" => Regex.IsMatch(value, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"),
            "uuid" => Guid.TryParse(value, out _),
            "date-time" => DateTime.TryParse(value, out _),
            _ => true
        };
    }

    private static bool ValidateParameterType(string value, string schemaType)
    {
        return schemaType switch
        {
            "Integer" => int.TryParse(value, out _),
            "Number" => double.TryParse(value, out _),
            "Boolean" => bool.TryParse(value, out _),
            "String" => true,
            _ => false
        };
    }
}

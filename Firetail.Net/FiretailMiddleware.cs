using Microsoft.AspNetCore.Http;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.Http.Extensions;

namespace Firetail;
internal class FiretailMiddleware(
    RequestDelegate next,
    OpenApiDocument spec,
    FiretailLoggingService loggingService,
    FiretailOptions firetailOptions)
{
    private readonly RequestDelegate _next = next;
    private readonly OpenApiDocument _spec = spec;
    private readonly FiretailLoggingService _loggingService = loggingService;
    private readonly FiretailOptions _firetailOptions = firetailOptions;

    public async Task InvokeAsync(HttpContext httpContext)
    {
        var cancellationToken = httpContext.RequestAborted;
        var firetailContext = new FiretailContext { Observations = [], SensitiveHeaders = _firetailOptions.SensitiveHeaders };

        try
        {
            if (!TryMatchRequest(httpContext, firetailContext, out var errorResponse))
            {
                firetailContext.Intercepted = true;
                firetailContext.Observations.Add(errorResponse);
                await RespondWithErrorAsync(httpContext, firetailContext, errorResponse, cancellationToken);
                return;
            }

            var methodWithBody = (httpContext.Request.Method == HttpMethods.Post || httpContext.Request.Method == HttpMethods.Put);
            var requestBody = string.Empty;

            if (methodWithBody)
            {
                httpContext.Request.EnableBuffering();
                using var reader = new StreamReader(httpContext.Request.Body, leaveOpen: true);
                requestBody = await reader.ReadToEndAsync(cancellationToken);
                firetailContext.RequestBody = requestBody;
                httpContext.Request.Body.Position = 0;
            }

            if (!ValidateRequest(httpContext, firetailContext, requestBody, out errorResponse))
            {
                firetailContext.Observations.Add(errorResponse);
                await RespondWithErrorAsync(httpContext, firetailContext, errorResponse, cancellationToken);
                return;
            }

            await CaptureAndValidateResponseAsync(httpContext, firetailContext, cancellationToken);
        }
        catch (Exception ex)
        {
            var errorResponse = CreateError(StatusCodes.Status500InternalServerError, "firetail.middleware.error", "Failed to handle request",
                new ErrorDetails { Message = ex.Message });
            firetailContext.Observations.Add(errorResponse);
            await RespondWithErrorAsync(httpContext, firetailContext, errorResponse, cancellationToken);
            return;
        }
        finally
        {
            FinalizeLogAsync(firetailContext, httpContext);
        }
    }

    private bool TryMatchRequest(HttpContext httpContext, FiretailContext firetailContext, out LogEntry errorResponse)
    {
        var path = httpContext.Request.Path.Value!;
        var method = httpContext.Request.Method.ToLowerInvariant();
        var contentType = httpContext.Request.ContentType;
        var requestUrl = httpContext.Request.GetDisplayUrl();

        var (match, parameters, matchedPath) = _spec.MatchPath(path, _firetailOptions.BasePath);
        firetailContext.PathParameters = parameters;
        firetailContext.MatchedPath = matchedPath;
        firetailContext.Method = method;
        firetailContext.Match = match;

        if (match == null)
        {
            errorResponse = CreateError(StatusCodes.Status404NotFound, "firetail.route.not.found",
                $"No route available for path {requestUrl}");
            return false;
        }

        if (IsUnsupportedContentType(contentType))
        {
            errorResponse = CreateError(StatusCodes.Status415UnsupportedMediaType,
                "firetail.unsupported.request.content.type", "Unsupported content type",
                new ErrorDetails { ContentType = contentType });
            return false;
        }

        if (match.GetOperation(method) is { } operation)
        {
            firetailContext.Operation = operation;
            errorResponse = null!;
            return true;
        }

        errorResponse = CreateError(StatusCodes.Status405MethodNotAllowed, "firetail.method.not.found",
            $"Method {method} not available for path {requestUrl}");
        return false;
    }

    private static bool ValidateRequest(HttpContext httpContext, FiretailContext firetailContext, string requestBody, out LogEntry errorResponse)
    {
        errorResponse = null!;
        firetailContext.ResponseValidated = true;
        var requestErrors = FiretailRequestValidator.Validate(httpContext.Request, requestBody, firetailContext);
        if (requestErrors.Any())
        {
            errorResponse = CreateError(requestErrors.First().Status,
               "firetail.request.validation.failed", "Failed to validate request",
               requestErrors);
            return false;
        }
        return true;
    }

    private async Task CaptureAndValidateResponseAsync(HttpContext httpContext, FiretailContext firetailContext, CancellationToken cancellationToken)
    {
        try
        {
            var originalBodyStream = httpContext.Response.Body;
            await using var memoryStream = new MemoryStream();
            httpContext.Response.Body = memoryStream;

            await _next(httpContext);

            memoryStream.Seek(0, SeekOrigin.Begin);
            var responseBody = await new StreamReader(memoryStream).ReadToEndAsync(cancellationToken);

            var responseErrors = FiretailResponseValidator.Validate(httpContext, responseBody, firetailContext);

            if (responseErrors.Any())
            {
                firetailContext.Intercepted = true;
                firetailContext.ResponseModified = true;
                httpContext.Response.Body = originalBodyStream;
                httpContext.Response.Clear();
                firetailContext.Observations.AddRange(responseErrors);
                await RespondWithErrorAsync(httpContext, firetailContext, responseErrors.First(), cancellationToken);
            }
            else
            {
                memoryStream.Seek(0, SeekOrigin.Begin);
                await memoryStream.CopyToAsync(originalBodyStream, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            var errorResponse = CreateError(StatusCodes.Status500InternalServerError, "firetail.response.handler.failed", "Response handler threw an error",
                new ErrorDetails { Message = ex.Message });
            firetailContext.Observations.Add(errorResponse);
            await RespondWithErrorAsync(httpContext, firetailContext, errorResponse, cancellationToken);
        }
    }

    private static bool IsUnsupportedContentType(string? contentType) =>
        !string.IsNullOrEmpty(contentType) && contentType != "application/json";

    private static LogEntry CreateError(int statusCode, string type, string title, params List<ErrorDetails> details)
    {
        return new LogEntry
        {
            Status = statusCode,
            Type = type,
            Title = title,
            Details = details.ToList()
        };
    }

    private void FinalizeLogAsync(FiretailContext firetailContext, HttpContext httpContext)
    {
        firetailContext.LatencyTimer.Stop();
        _loggingService.AddLogAsync(httpContext, firetailContext);
    }

    private async Task RespondWithErrorAsync(HttpContext httpContext, FiretailContext firetailContext, LogEntry error, CancellationToken cancellationToken)
    {
        httpContext.Response.StatusCode = error.Status;
        httpContext.Response.ContentType = "application/problem+json";
        await httpContext.Response.WriteAsync(error.ToJson(indented: true), cancellationToken);
    }
}
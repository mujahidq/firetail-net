using Moq;
using Microsoft.AspNetCore.Http;
using Microsoft.OpenApi.Models;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Firetail.Tests;

public class FiretailSample2Tests
{
    private readonly FiretailResponseValidator _responseValidator;
    private readonly FiretailRequestValidator _requestValidator;
    private readonly OpenApiDocument _openApiDoc;
    private readonly FiretailOptions _options;
    private readonly FiretailLoggingService _loggingService;

    public FiretailSample2Tests()
    {
        _responseValidator = new FiretailResponseValidator();
        
        _requestValidator = new FiretailRequestValidator();

        var filePath = Path.Combine(Directory.GetCurrentDirectory(), "sample2.json");
        _openApiDoc = new OpenApiSpecLoader().Load(filePath).Item1;
        _options = new FiretailOptions
        {
            BasePath = _openApiDoc.GetBasePath(),
            SensitiveHeaders = new string[] {"authorization"}
        };
        var httpClientFactory = new Mock<IHttpClientFactory>().Object;
        var logger = new Mock<ILogger<FiretailLoggingService>>().Object;
        _loggingService = new FiretailLoggingService(_options, httpClientFactory, logger);
    }

    private HttpContext CreateHttpContext(string method, string path, string contentType = "application/json", string body = "", string query = "")
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.ContentType = contentType;

        if (!string.IsNullOrEmpty(body))
        {
            context.Request.ContentLength = body.Length;
        }

        if (!string.IsNullOrEmpty(query))
        {
            context.Request.QueryString = new QueryString(query);
        }

        return context;
    }

    private FiretailContext SetupFiretailContext(HttpContext context, string path)
    {
        var (match, parameters, matchedPath) = _openApiDoc.MatchPath(path, _options.BasePath);
        var method = context.Request.Method.ToLower();

        var operation = match?.GetOperation(method);

        return new FiretailContext
        {
            PathParameters = parameters,
            Operation = operation,
            MatchedPath = matchedPath,
        };
    }


    [Fact]
    public async Task InvokeAsync_UnsupportedContentType_ReturnsUnsupportedMediaType()
    {
        var context = CreateHttpContext("GET", "/performance/unbounded-result-set", "text/plain");
        var middleware = new FiretailMiddleware((innerHttpContext) => Task.CompletedTask, _openApiDoc, _loggingService, _options);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status415UnsupportedMediaType, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_ValidRequest_CallsNextMiddleware()
    {
        var context = CreateHttpContext("GET", "/performance/inefficient-algorithm", "application/json", query:"?n=50&searchTerm=5");

        var nextCalled = false;
        var middleware = new FiretailMiddleware((innerHttpContext) => { nextCalled = true; return Task.CompletedTask; }, _openApiDoc, _loggingService, _options);

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_UnmatchedRoute_ReturnsNotFound()
    {
        var context = CreateHttpContext("GET", "/unknown-path");
        var middleware = new FiretailMiddleware((innerHttpContext) => Task.CompletedTask, _openApiDoc, _loggingService, _options);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_MethodNotAllowed_ReturnsMethodNotAllowed()
    {
        var context = CreateHttpContext("DELETE", "/performance/unbounded-result-set");
        var middleware = new FiretailMiddleware((innerHttpContext) => Task.CompletedTask, _openApiDoc, _loggingService, _options);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status405MethodNotAllowed, context.Response.StatusCode);
    }

    
    [Fact]
    public void ValidateRequestAsync_InvalidLimit_ReturnsError()
    {
        var context = CreateHttpContext("GET", "/performance/unbounded-result-set", query: "?limit=0");
        var firetailContext = SetupFiretailContext(context, "/performance/unbounded-result-set");
       
        var errors = FiretailRequestValidator.Validate(context.Request, string.Empty, firetailContext);

        Assert.Single(errors);
        Assert.Equal(StatusCodes.Status400BadRequest, errors[0].Status);
        Assert.Contains("must be greater than or equal to", errors[0].Message);
    }


    [Fact]
    public void ValidateRequestAsync_NGreaterThanMaximum_ReturnsError()
    {
        // Arrange
        var context = CreateHttpContext("GET", "/performance/inefficient-algorithm", query: "?n=100001&searchTerm=5");
        var firetailContext = SetupFiretailContext(context, "/performance/inefficient-algorithm");

        // Act
        var errors = FiretailRequestValidator.Validate(context.Request, string.Empty, firetailContext);

        // Assert
        Assert.Single(errors);
        Assert.Equal(StatusCodes.Status400BadRequest, errors[0].Status);
        Assert.Contains("must be less than or equal to 100000", errors[0].Message);
    }

    [Fact]
    public void ValidateRequestAsync_SearchTermLessThanMinimum_ReturnsError()
    {
        // Arrange
        var context = CreateHttpContext("GET", "/performance/inefficient-algorithm", query: "?n=100&searchTerm=-1");
        var firetailContext = SetupFiretailContext(context, "/performance/inefficient-algorithm");

        // Act
        var errors = FiretailRequestValidator.Validate(context.Request, string.Empty, firetailContext);

        // Assert
        Assert.Single(errors);
        Assert.Equal(StatusCodes.Status400BadRequest, errors[0].Status);
        Assert.Contains("must be greater than or equal to 0", errors[0].Message);
    }

    [Fact]
    public void ValidateRequestAsync_ValidParameters_NoErrors()
    {
        // Arrange
        var context = CreateHttpContext("GET", "/performance/inefficient-algorithm", query: "?n=50&searchTerm=5");
        var firetailContext = SetupFiretailContext(context, "/performance/inefficient-algorithm");

        // Act
        var errors = FiretailRequestValidator.Validate(context.Request, string.Empty, firetailContext);

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateRequestAsync_UnicodeText_ReturnsServerError()
    {
        // Arrange
        var body = "{ \"text\": \"Hello, \uD83D\uDE00\" }";  // Contains an emoji
        var context = CreateHttpContext("POST", "/internal-server-errors/improper-unicode-encoding", body: body);
        var firetailContext = SetupFiretailContext(context, "/internal-server-errors/improper-unicode-encoding");

        // Act
        var errors = FiretailRequestValidator.Validate(context.Request, body, firetailContext);

        // Assert
        Assert.Single(errors);
        Assert.Equal(StatusCodes.Status500InternalServerError, errors[0].Status);
        Assert.Contains("Unicode handling error", errors[0].Message);
    }

    [Fact]
    public void ValidateRequestAsync_MissingTextField_ReturnsBadRequest()
    {
        // Arrange
        var body = "{ \"message\": \"Hello\" }";  // Missing required 'text' field
        var context = CreateHttpContext("POST", "/internal-server-errors/improper-unicode-encoding", body: body);
        var firetailContext = SetupFiretailContext(context, "/internal-server-errors/improper-unicode-encoding");

        // Act
        var errors = FiretailRequestValidator.Validate(context.Request, body, firetailContext);

        // Assert
        Assert.Single(errors);
        Assert.Equal(StatusCodes.Status400BadRequest, errors[0].Status);
        Assert.Contains("Missing required property: body.text", errors[0].Message);
    }

    [Fact]
    public void ValidateRequestAsync_ProperUnicodeText_NoErrors()
    {
        // Arrange
        var body = "{ \"text\": \"Hello, world!\" }";  // Valid text
        var context = CreateHttpContext("POST", "/internal-server-errors/improper-unicode-encoding", body: body);
        var firetailContext = SetupFiretailContext(context, "/internal-server-errors/improper-unicode-encoding");

        // Act
        var errors = FiretailRequestValidator.Validate(context.Request, body, firetailContext);

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public void ImproperInputTypeHandling_ValidCardNumber_PassesValidation()
    {
        var validBody = JsonSerializer.Serialize(new { number = "1234567812345670" });  // Correct input
        var context = CreateHttpContext("POST", "/internal-server-errors/improper-input-type-handling", "application/json", validBody);
        var firetailContext = SetupFiretailContext(context, "/internal-server-errors/improper-input-type-handling");

        var errors = FiretailRequestValidator.Validate(context.Request, validBody, firetailContext);

        Assert.Empty(errors);  // No errors for valid card number
    }

    [Fact]
    public void ImproperInputTypeHandling_NonNumericString_PassesValidation()
    {
        var invalidStringBody = JsonSerializer.Serialize(new { number = "invalid_card_number" });  // Non-numeric, but still a string
        var context = CreateHttpContext("POST", "/internal-server-errors/improper-input-type-handling", "application/json", invalidStringBody);
        var firetailContext = SetupFiretailContext(context, "/internal-server-errors/improper-input-type-handling");

        var errors = FiretailRequestValidator.Validate(context.Request, invalidStringBody, firetailContext);

        Assert.Empty(errors);  // No validation error, as spec allows any string
    }

    [Fact]
    public void ImproperInputTypeHandling_MissingNumberField_ReturnsBadRequest()
    {
        var missingFieldBody = JsonSerializer.Serialize(new { });  // Missing 'number'
        var context = CreateHttpContext("POST", "/internal-server-errors/improper-input-type-handling", "application/json", missingFieldBody);
        var firetailContext = SetupFiretailContext(context, "/internal-server-errors/improper-input-type-handling");

        var errors = FiretailRequestValidator.Validate(context.Request, missingFieldBody, firetailContext);

        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Message != null && e.Message.Contains("Missing required property") && e.Status == StatusCodes.Status400BadRequest);
    }

    [Fact]
    public void ImproperInputTypeHandling_NumberAsInteger_ReturnsValidationError()
    {
        var integerInsteadOfStringBody = JsonSerializer.Serialize(new { number = 1234567812345670 });  // Should be string, not integer
        var context = CreateHttpContext("POST", "/internal-server-errors/improper-input-type-handling", "application/json", integerInsteadOfStringBody);
        var firetailContext = SetupFiretailContext(context, "/internal-server-errors/improper-input-type-handling");

        var errors = FiretailRequestValidator.Validate(context.Request, integerInsteadOfStringBody, firetailContext);

        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Message != null && e.Message.Contains("Invalid type for"));
    }

}

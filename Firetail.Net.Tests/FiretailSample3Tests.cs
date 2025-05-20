using Moq;
using Microsoft.AspNetCore.Http;
using Microsoft.OpenApi.Models;
using System.Text.Json;
using Xunit;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Firetail.Tests;

public class FiretailSample3Tests
{
    private readonly FiretailRequestValidator _requestValidator;
    private readonly OpenApiDocument _openApiDoc;
    private readonly FiretailOptions _options;

    public FiretailSample3Tests()
    {
        // Load Aura API schema
        var filePath = Path.Combine(Directory.GetCurrentDirectory(), "sample3.json");
        _openApiDoc = new OpenApiSpecLoader().Load(filePath).Item1;
        _options = new FiretailOptions
        {
            BasePath = _openApiDoc.GetBasePath()
        };

        _requestValidator = new FiretailRequestValidator();
    }

    private HttpContext CreateHttpContext(string method, string path, string contentType = "application/json", string body = "", string query = "")
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.ContentType = contentType;
        context.Request.QueryString = new QueryString(query);

        if (!string.IsNullOrEmpty(body))
        {
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
            context.Request.ContentLength = body.Length;
        }

        return context;
    }

    private FiretailContext SetupFiretailContext(HttpContext context, string path)
    {
        var (match, parameters, matchedPath) = _openApiDoc.MatchPath(path, _options.BasePath);
        var method = context.Request.Method.ToLower();

        return new FiretailContext
        {
            PathParameters = parameters,
            Operation = match?.GetOperation(method),
            MatchedPath = matchedPath,
        };
    }

    [Fact]
    public void CreateInstance_MissingRequiredFields_ReturnsBadRequest()
    {
        // Arrange
        var invalidBody = JsonSerializer.Serialize(new
        {
            name = "TestInstance",
            cloud_provider = "gcp"
            // Missing version, region, memory, type, tenant_id
        });

        var context = CreateHttpContext("POST", "/instances", "application/json", invalidBody);
        var firetailContext = SetupFiretailContext(context, "/instances");

        // Act
        var errors = FiretailRequestValidator.Validate(context.Request, invalidBody, firetailContext);

        // Assert
        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Message != null && e.Message.Contains("Missing required property: body.version"));
        Assert.Contains(errors, e => e.Message != null && e.Message.Contains("Missing required property: body.region"));
        //[FireTail Request Validation Setup] Missing required property: body.version
    }

    [Fact]
    public void CreateInstance_InvalidCloudProvider_ReturnsBadRequest()
    {
        // Arrange
        var invalidBody = JsonSerializer.Serialize(new
        {
            version = "5",
            region = "europe-west1",
            memory = "8GB",
            name = "Test",
            type = "enterprise-db",
            tenant_id = "project123",
            cloud_provider = "invalid"  // Invalid cloud provider
        });

        var context = CreateHttpContext("POST", "/instances", "application/json", invalidBody);
        var firetailContext = SetupFiretailContext(context, "/instances");

        // Act
        var errors = FiretailRequestValidator.Validate(context.Request, invalidBody, firetailContext);

        // Assert
        Assert.Single(errors);
        Assert.Contains("body.cloud_provider. Expected one of: gcp, aws, azure", errors[0].Message);
    }

    [Fact]
    public async Task GetInstance_InvalidInstanceId_ReturnsNotFound()
    {
        // Arrange
        var context = CreateHttpContext("GET", "/invalid-id");
        var httpClientFactory = new Mock<IHttpClientFactory>().Object;
          var logger = new Mock<ILogger<FiretailLoggingService>>().Object;
        var middleware = new FiretailMiddleware(
            next: _ => Task.CompletedTask,
            spec: _openApiDoc,
            loggingService: new FiretailLoggingService(_options, httpClientFactory, logger),
            firetailOptions: _options);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    [Fact]
    public void UpdateInstance_InvalidMemorySize_ReturnsBadRequest()
    {
        // Arrange
        var invalidBody = JsonSerializer.Serialize(new
        {
            memory = "64GB"  // Assuming 64GB is not a valid option
        });

        var context = CreateHttpContext("PATCH", "/instances/instance123", "application/json", invalidBody);
        var firetailContext = SetupFiretailContext(context, "/instances/{instanceId}");

        // Act
        var errors = FiretailRequestValidator.Validate(context.Request, invalidBody, firetailContext);

        // Assert
        Assert.Single(errors);
        Assert.Contains("Invalid value for body.memory", errors[0].Message);
    }

    [Fact]
    public void CreateInstance_ValidRequest_NoErrors()
    {
        // Arrange
        var validBody = JsonSerializer.Serialize(new
        {
            version = "5",
            region = "europe-west1",
            memory = "8GB",
            name = "Production",
            type = "enterprise-db",
            tenant_id = "project123",
            cloud_provider = "gcp"
        });

        var context = CreateHttpContext("POST", "/instances", "application/json", validBody);
        var firetailContext = SetupFiretailContext(context, "/instances");

        // Act
        var errors = FiretailRequestValidator.Validate(context.Request, validBody, firetailContext);

        // Assert
        Assert.Empty(errors);
    }


    [Fact]
    public void CreateCustomerKey_ValidKeyFormat_ReturnsSuccess()
    {
        // Arrange
        var invalidBody = JsonSerializer.Serialize(new
        {
            key_id = "invalid-format",
            name = "Test Key",
            cloud_provider = "aws",
            instance_type = "enterprise-db",
            region = "us-west-2",
            tenant_id = "project123"
        });

        var context = CreateHttpContext("POST", "/customer-managed-keys", "application/json", invalidBody);
        var firetailContext = SetupFiretailContext(context, "/customer-managed-keys");

        // Act
        var errors = FiretailRequestValidator.Validate(context.Request, invalidBody, firetailContext);

        // Assert
        Assert.Empty(errors);
    }

}
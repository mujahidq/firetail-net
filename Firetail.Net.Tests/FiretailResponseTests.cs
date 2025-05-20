using Xunit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using Firetail;
using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;

namespace Firetail.Tests
{
    public class FiretailResponseFilterAdditionalTests
    {
        //private readonly FiretailResponseFilter _filter;
        private readonly OpenApiDocument _openApiDoc;
        private readonly string _basePath;

        public FiretailResponseFilterAdditionalTests()
        {
            var logger = new FiretailLogger(new Mock<ILogger<FiretailLogger>>().Object, new Mock<FiretailLogCollector>().Object);
            var responseValidator = new FiretailResponseValidator();
           // _filter = new FiretailResponseFilter(logger, responseValidator);

            var filePath = Path.Combine(Directory.GetCurrentDirectory(), "sample2.json");
            _openApiDoc = new OpenApiSpecLoader().Load(filePath).Item1;
            _basePath = _openApiDoc.GetBasePath();
        }

        private FiretailContext CreateFiretailContext(string path, int statusCode)
        {
            var matchedPath = _openApiDoc.MatchPath(path, _basePath).Item1;
            return new FiretailContext
            {
                Operation = matchedPath.GetOperation("get"),
                OriginalStatusCode = statusCode,
                MatchedPath = matchedPath
            };
        }

        [Fact]
        public async Task MissingAcceptHeader_ReturnsValidationError()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Response.ContentType = "application/json";

            var firetailContext = CreateFiretailContext("/response-conformance/missing-field", StatusCodes.Status200OK);

            var responseObject = new { id = "123", name = "John", age = 30 };

            var errors = await _filter.ProcessResponseAsync(httpContext, new ObjectResult(responseObject), firetailContext);

            Assert.Single(errors);
            Assert.Equal("firetail.request.accept.header.missing", errors[0].Type);
            Assert.Contains("The Accept header is missing from the request", errors[0].Title);
        }

        [Fact]
        public async Task EmptyResponse_ReturnsSanitizationError()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Response.ContentType = "application/json";
            httpContext.Request.Headers["Accept"] = "application/json";

            var firetailContext = CreateFiretailContext("/response-conformance/missing-field", StatusCodes.Status200OK);

            var responseObject = "";

            var errors = await _filter.ProcessResponseAsync(httpContext, new ObjectResult(responseObject), firetailContext);

            Assert.Single(errors);
            Assert.Equal("firetail.response.sanitisation.failed", errors[0].Type);
            Assert.Contains("Failed to sanitise response", errors[0].Title);
        }

        //[Fact]
        //public async Task IncorrectPrimitiveType_ReturnsValidationError()
        //{
        //    var httpContext = new DefaultHttpContext();
        //    httpContext.Response.ContentType = "application/json";
        //    httpContext.Request.Headers["Accept"] = "application/json";

        //    var firetailContext = CreateFiretailContext("/response-conformance/missing-field", StatusCodes.Status200OK);

        //    var responseObject = new { id = "123", name = "John", age = "thirty" };

        //    var errors = await _filter.ProcessResponseAsync(httpContext, new ObjectResult(responseObject), firetailContext);

        //    Assert.Single(errors);
        //    Assert.Equal("firetail.response.validation.failed", errors[0].Type);
        //    Assert.Contains("age is expected to be of type 'Integer', but got 'String'", errors[0].Details[0].Message);
        //}

        //[Fact]
        //public async Task ArrayResponse_ValidatesCorrectly()
        //{
        //    var httpContext = new DefaultHttpContext();
        //    httpContext.Response.ContentType = "application/json";
        //    httpContext.Request.Headers["Accept"] = "application/json";

        //    var firetailContext = CreateFiretailContext("/performance/unbounded-result-set", StatusCodes.Status200OK);

        //    var responseObject = new { item1 = "data1", item2 = "data2" };

        //    var errors = await _filter.ProcessResponseAsync(httpContext, new ObjectResult(responseObject), firetailContext);

        //    Assert.Empty(errors);
        //}

        //[Fact]
        //public async Task WildcardAcceptHeader_HandledCorrectly()
        //{
        //    var httpContext = new DefaultHttpContext();
        //    httpContext.Response.ContentType = "application/json";
        //    httpContext.Request.Headers["Accept"] = "*/*";

        //    var firetailContext = CreateFiretailContext("/response-conformance/missing-field", StatusCodes.Status200OK);

        //    var responseObject = new { id = "123", name = "John", age = 30 };

        //    var errors = await _filter.ProcessResponseAsync(httpContext, new ObjectResult(responseObject), firetailContext);

        //    Assert.Empty(errors);
        //}

        [Fact]
        public async Task NonJsonObjectResponse_ReturnsSanitizationError()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Response.ContentType = "application/json";
            httpContext.Request.Headers["Accept"] = "application/json";

            var firetailContext = CreateFiretailContext("/response-conformance/malformed-json", StatusCodes.Status200OK);

            var responseObject = "{12345";

            var errors = await _filter.ProcessResponseAsync(httpContext, new ObjectResult(responseObject), firetailContext);

            Assert.Single(errors);
            Assert.Equal("firetail.response.sanitisation.failed", errors[0].Type);
        }

       
    }
}

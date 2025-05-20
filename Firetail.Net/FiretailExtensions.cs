using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Headers;

namespace Firetail;
public static class FiretailServiceExtensions
{
    public static IServiceCollection AddFiretail(this IServiceCollection services, Action<FiretailOptions>? configure = null)
    {
        var options = new FiretailOptions();
        configure?.Invoke(options);
        if (string.IsNullOrEmpty(options.FiretailAPIKey))
        {
            throw new InvalidOperationException("Firetail API key is missing");
        }
        if (string.IsNullOrEmpty(options.FiretailAPIHost))
        {
            throw new InvalidOperationException("Firetail API endpoint is missing");
        }
        if (string.IsNullOrEmpty(options.ApiDocPath))
        {
            throw new InvalidOperationException("Firetail API doc path is missing");
        }
        var (schema, diagnostic) = new OpenApiSpecLoader().Load(options.ApiDocPath);
        if (diagnostic.Errors.Count > 0)
        {
            throw new InvalidOperationException("Failed to load OpenAPI spec\nErrors:\n" + string.Join("\n", diagnostic.Errors));
        }
        options.BasePath = options.BasePath ?? schema.GetBasePath();
        options.SchemaVersion = diagnostic.SpecificationVersion;

        services.AddSingleton(schema);
        services.AddSingleton(options);
        services.AddSingleton<FiretailLoggingService>();
        services.AddHostedService(provider => provider.GetRequiredService<FiretailLoggingService>());

        services.AddHttpClient("Firetail", client =>
        {
            client.BaseAddress = new Uri($"{options.FiretailAPIHost}/logs/bulk");
            client.DefaultRequestHeaders.Add("x-ft-api-key", options.FiretailAPIKey);
        });

        return services;
    }

    public static IApplicationBuilder UseFiretail(this IApplicationBuilder app) =>
        app.UseMiddleware<FiretailMiddleware>();
}
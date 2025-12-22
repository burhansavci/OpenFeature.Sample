using Microsoft.AspNetCore.Mvc;
using OpenFeature;
using OpenFeature.Hooks;
using OpenFeature.Model;
using OpenFeature.Providers.GOFeatureFlag;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(builder.Environment.ApplicationName))
    .WithTracing(tracing => tracing
        .AddHttpClientInstrumentation()
        .AddAspNetCoreInstrumentation())
    .WithMetrics(metrics => metrics
        .AddHttpClientInstrumentation()
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddMeter("OpenFeature"))
    .UseOtlpExporter();


builder.Services.AddOpenFeature(featureBuilder =>
{
    var metricsHookOptions = MetricsHookOptions.CreateBuilder()
        // .WithCustomDimension("custom_dimension_key", "custom_dimension_value")
        .WithFlagEvaluationMetadata("boolean", s => s.GetBool("boolean"))
        .Build();

    featureBuilder
        .AddHook(sp => new LoggingHook(sp.GetRequiredService<ILogger<LoggingHook>>()))
        .AddHook(_ => new MetricsHook(metricsHookOptions))
        // .AddHook<MetricsHook>()
        .AddHook<TraceEnricherHook>()
        .AddProvider(_ => new GOFeatureFlagProvider(new GOFeatureFlagProviderOptions
        {
            Endpoint = "http://goff-relay-proxy:1031",
            // Endpoint = "http://localhost:1031",
            FlagChangePollingIntervalMs = TimeSpan.FromMilliseconds(500)
        }));
});

builder.Logging.AddOpenTelemetry(options =>
{
    options.IncludeScopes = true;
    options.IncludeFormattedMessage = true;
});

var app = builder.Build();

app.MapOpenApi();
app.MapScalarApiReference();

app.UseHttpsRedirection();


app.MapGet("/welcome", async ([FromServices] IFeatureClient featureClient) =>
{
    var context = EvaluationContext.Builder()
        .SetTargetingKey("default-targeting-key")
        .Build();

    var welcomeMessageEnabled = await featureClient.GetBooleanValueAsync("welcome-message", false, context);

    if (welcomeMessageEnabled)
    {
        return TypedResults.Ok("Hello world! The welcome-message feature flag was enabled!");
    }

    return TypedResults.Ok("Hello world!");
});


app.Run();
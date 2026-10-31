using AuthorizationService.Endpoints;
using AuthorizationService.Implementation.Persistence;
using EwaysVNext.Database;
using EwaysVNext.Host;
using EwaysVNext.Host.Authentication;
using EwaysVNext.Host.Authentication.Extensions;
using EwaysVNext.Host.Middleware;
using EwaysVNext.Outcomes;
using EwaysVNext.Modules.Catalog;
using EwaysVNext.Modules.Customer;
using EwaysVNext.Modules.Partners;
using EwaysVNext.Presentation.Endpoint;
using EwaysVNext.Audit.Implementation;
using EwaysVNext.Jobs.Implementation;
using EwaysVNext.Storage.Implementation;
using EwaysVNext.Presentation.Endpoint.GraphQL;
using EwaysVNext.Presentation.Endpoint.Security;
using EwaysVNext.Host.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;
using Serilog;
using System.Security.Claims;
using EwaysVNext.Modules.Catalog.Adapter.Inbound.Rest.Categories;
using EwaysVNext.Outcomes.Extensions;

if (args.Contains("--probe")) return await Probe.RunAsync(args);

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddModuleConfiguration(
    ["catalog", "partners"],
    builder.Environment.EnvironmentName);
builder.Configuration.AddEnvironmentVariables();
HostCommands.ApplyFlags(builder.Configuration, args);


builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}",
        formatProvider: System.Globalization.CultureInfo.InvariantCulture));

builder.Services
    .AddCatalogModule()
    .AddCustomerModule(builder.Configuration)
    .AddPartnersModule(builder.Configuration);
builder.Services.AddUnifiedDatabase(builder.Configuration, builder.Environment.IsDevelopment());
builder.Services.AddObjectStorage(builder.Configuration);
builder.Services.AddJobs(builder.Configuration);
builder.Services.AddAudit();
builder.Services.AddAuthorizationSystem(builder.Configuration);

builder.Services.AddMultiRealmKeycloakAuthentication(builder.Configuration);
builder.Services.AddEndpoints(typeof(Program).Assembly);

// The standard error body for 401 and 403. Without it those are the only two responses ASP.NET
// Core answers itself, with no body at all.
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, EwaysAuthorizationResultHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, PartnerScopeAuthorizationHandler>();

builder.Services.AddAuthorization(opts =>
{
    opts.AddEwaysAuthorizationPolicies();

    // The names Catalog's endpoints were written against. Removed in phase 2-3 when they are
    // regated onto AuthPolicies.
    opts.AddLegacyPolicyAliases();
});

var allowedOrigins = builder.Configuration.GetSection("Host:AllowedCorsOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(opts =>
{
    opts.AddPolicy("default", p =>
    {
        p.AllowAnyHeader().AllowAnyMethod();
        if (allowedOrigins.Length > 0) p.WithOrigins(allowedOrigins).AllowCredentials();
        else p.AllowAnyOrigin();
    });
});

builder.Services.AddEwaysRateLimiting(builder.Configuration);

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<KeycloakOAuthDocumentTransformer>();
    options.AddOperationTransformer<KeycloakSecurityRequirementOperationTransformer>();
});
builder.Services.AddHealthChecks();

var otlpEndpoint = builder.Configuration["Otlp:Endpoint"]
    ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
var otlpConfigured = !string.IsNullOrWhiteSpace(otlpEndpoint);

void ConfigureOtlp(OtlpExporterOptions o)
{
    o.Endpoint = new Uri(otlpEndpoint!);
    if (string.Equals(builder.Configuration["Otlp:Protocol"], "httpprotobuf", StringComparison.OrdinalIgnoreCase))
        o.Protocol = OtlpExportProtocol.HttpProtobuf;
}

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("EwaysVNext"))
    .WithTracing(t =>
    {
        t.AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddEntityFrameworkCoreInstrumentation()
            .AddSource("EwaysVNext");
        if (otlpConfigured) t.AddOtlpExporter(ConfigureOtlp);
    })
    .WithMetrics(m =>
    {
        m.AddAspNetCoreInstrumentation()
            .AddMeter("EwaysVNext.Sagas");
        if (otlpConfigured) m.AddOtlpExporter(ConfigureOtlp);
    });

if (!otlpConfigured)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine(ProgramMessages.OtlpNotConfigured);
    Console.ResetColor();
}

var app = builder.Build();

if (await HostCommands.RunAsync(app, args) is { } exitCode) return exitCode;

app.UseSerilogRequestLogging();

var workerOnly = string.Equals(
    builder.Configuration["Jobs:Mode"], "WorkerOnly", StringComparison.OrdinalIgnoreCase);

if (!workerOnly && app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    var keycloakOptions = app.Services.GetRequiredService<KeycloakOptions>();

    app.MapScalarApiReference(options =>
    {
        options.Title = "EwaysVNext API";
        options.Theme = ScalarTheme.Purple;
        options.HideModels = false;
        options.ShowSidebar = true;

        options.AddPreferredSecuritySchemes([.. keycloakOptions.Realms.Keys]);
        foreach (var (tenant, realm) in keycloakOptions.Realms)
        {
            options.AddAuthorizationCodeFlow(tenant, flow =>
            {
                flow.ClientId = realm.ClientId;
                flow.Pkce = Pkce.Sha256;
                flow.SelectedScopes = ["openid"];
            });
        }
    });
}

// First in the pipeline: everything downstream, including the error body and the logs,
// reads the correlation id it sets.
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<GlobalExceptionHandlerMiddleware>();
app.UseCors("default");
// Authentication first: the rate limiter partitions by partner, and context.User is anonymous
// until authentication has run — before this order was fixed, every partner request fell into the
// anonymous per-IP partition and the per-partner limit could never apply.
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

if (!workerOnly)
{
    app.MapEndpoints();
    app.MapGraphQLEndpoint(options =>
    {
        options.Tool.Title = "EwaysVNext GraphQL";
        options.Tool.Enable = app.Environment.IsDevelopment();
    });
}

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });

app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });

app.MapHealthChecks("/health");

app.Run();

return 0;

public partial class Program { }

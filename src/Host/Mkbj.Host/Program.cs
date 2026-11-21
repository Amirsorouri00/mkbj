using AuthorizationService.Endpoints;
using AuthorizationService.Implementation.Persistence;
using Mkbj.Database;
using Mkbj.Core.Application;
using Mkbj.Host;
using Mkbj.Host.Authentication;
using Mkbj.Host.Authentication.Extensions;
using Mkbj.Host.Middleware;
using Mkbj.Outcomes;
using Mkbj.Modules.Catalog;
using Mkbj.Modules.Pricing;
using Mkbj.Modules.Customer;
using Mkbj.Modules.Partners;
using Mkbj.Modules.Admin;
using Mkbj.Modules.Sales;
using Mkbj.Modules.Sourcing;
using Mkbj.Modules.Settings;
using Mkbj.Presentation.Endpoint;
using Mkbj.Audit.Implementation;
using Mkbj.Jobs.Implementation;
using Mkbj.Storage.Implementation;
using Mkbj.Presentation.Endpoint.GraphQL;
using Mkbj.Presentation.Endpoint.Security;
using Mkbj.Host.OpenApi;
using Mkbj.Host.RateLimiting;
using Mkbj.Host.Seeding;
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
using Mkbj.Modules.Catalog.Adapter.Inbound.Rest.Categories;
using Mkbj.Outcomes.Extensions;

if (args.Contains("--probe")) return await Probe.RunAsync(args);

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddModuleConfiguration(
    ["catalog", "partners"],
    builder.Environment.EnvironmentName);
builder.Configuration.AddEnvironmentVariables();
HostCommands.ApplyFlags(builder.Configuration, args);


// ReadFrom.Services picks up any ILogEventSink registered in DI. That is what lets a test
// capture everything the application logs and assert that a credential never appears in it.
builder.Host.UseSerilog((ctx, services, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .ReadFrom.Services(services)
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}",
        formatProvider: System.Globalization.CultureInfo.InvariantCulture));

builder.Services
    .AddCatalogModule()
    // After Catalog: Pricing's outbound adapter reaches into it, so the thing it depends on is
    // already registered when its own registrations run.
    .AddPricingModule(builder.Configuration)
    .AddCustomerModule(builder.Configuration)
    .AddPartnersModule(builder.Configuration)
    .AddSettingsModule(builder.Configuration)
    .AddSalesModule(builder.Configuration)
    .AddAdminModule()
    .AddSourcingModule(builder.Configuration);
builder.Services.AddUnifiedDatabase(builder.Configuration, builder.Environment.IsDevelopment());
builder.Services.AddObjectStorage(builder.Configuration);
builder.Services.AddJobs(builder.Configuration);
builder.Services.AddAudit();

// Demo data, run only by --seed, which refuses in Production. Registering it is harmless: a
// serving process never calls a seeder.
builder.Services.AddScoped<IDataSeeder, DemoCatalogDataSeeder>();
builder.Services.AddAuthorizationSystem(builder.Configuration);
// AddGraphQLServer is invoked by independent modules. Apply the shared recursive-field limit
// after the final registration so a module cannot restore Hot Chocolate's lower default.
builder.Services.ConfigureGraphQLValidationLimits();

builder.Services.AddMultiRealmKeycloakAuthentication(builder.Configuration);
builder.Services.AddEndpoints(typeof(Program).Assembly);

// The standard error body for 401 and 403. Without it those are the only two responses ASP.NET
// Core answers itself, with no body at all.
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, MkbjAuthorizationResultHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, PartnerScopeAuthorizationHandler>();

builder.Services.AddAuthorization(opts => opts.AddMkbjAuthorizationPolicies());

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

builder.Services.AddMkbjRateLimiting(builder.Configuration);

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<KeycloakOAuthDocumentTransformer>();
    options.AddOperationTransformer<KeycloakSecurityRequirementOperationTransformer>();
    options.AddDocumentTransformer<PartnerApiKeyOpenApiTransformer>();
    options.AddOperationTransformer<PartnerApiKeyOpenApiTransformer>();
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
    .ConfigureResource(r => r.AddService("mkbj"))
    .WithTracing(t =>
    {
        t.AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddEntityFrameworkCoreInstrumentation()
            .AddSource("mkbj");
        if (otlpConfigured) t.AddOtlpExporter(ConfigureOtlp);
    })
    .WithMetrics(m =>
    {
        m.AddAspNetCoreInstrumentation()
            .AddMeter("mkbj.sagas");
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

// The partner contract is public: somebody else's engineers generate clients from it, and a
// document served only in Development is one they cannot reach. Scalar — the browsable UI —
// stays Development-only, because that is a tool for this team rather than a contract.
if (!workerOnly && builder.Configuration.GetValue("OpenApi:Expose", true))
    app.MapOpenApi();

if (!workerOnly && app.Environment.IsDevelopment())
{
    var keycloakOptions = app.Services.GetRequiredService<KeycloakOptions>();

    app.MapScalarApiReference(options =>
    {
        options.Title = "MKBJ API";
        options.Theme = ScalarTheme.Purple;
        options.HideModels = false;
        options.ShowSidebar = true;

        options.AddPreferredSecuritySchemes([PartnerApiKeyOpenApiTransformer.SchemeName, .. keycloakOptions.Realms.Keys]);
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
// Between the two on purpose: after authentication there is a partner to attribute the call to,
// and before the limiter a 429 is still recorded.
app.UsePartnersModule();
app.UseMiddleware<Mkbj.Modules.Sales.Adapter.Inbound.Rest.Partner.PartnerOrderRequestMiddleware>();
app.UseRateLimiter();
app.UseAuthorization();

if (!workerOnly)
{
    app.MapEndpoints();
    app.MapGraphQLEndpoint(options =>
    {
        options.Tool.Title = "MKBJ GraphQL";
        options.Tool.Enable = app.Environment.IsDevelopment();
    });
}

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });

app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });

app.MapHealthChecks("/health");

app.Run();

return 0;

public partial class Program { }

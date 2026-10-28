using AuthorizationService.Endpoints;
using AuthorizationService.Implementation.Persistence;
using EwaysVNext.Database;
using EwaysVNext.Host;
using EwaysVNext.Host.Authentication;
using EwaysVNext.Host.Authentication.Extensions;
using EwaysVNext.Host.Middleware;
using EwaysVNext.Modules.Catalog;
using EwaysVNext.Modules.Customer;
using EwaysVNext.Presentation.Endpoint;
using EwaysVNext.Presentation.Endpoint.GraphQL;
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

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddModuleConfiguration(
    ["catalog"],
    builder.Environment.EnvironmentName);
builder.Configuration.AddEnvironmentVariables();


builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}",
        formatProvider: System.Globalization.CultureInfo.InvariantCulture));

builder.Services
    .AddCatalogModule()
    .AddCustomerModule(builder.Configuration);
builder.Services.AddUnifiedDatabase(builder.Configuration, builder.Environment.IsDevelopment());
builder.Services.AddAuthorizationSystem(builder.Configuration);

builder.Services.AddMultiRealmKeycloakAuthentication(builder.Configuration);
builder.Services.AddEndpoints(typeof(Program).Assembly);

builder.Services.AddAuthorization(opts =>
{
    opts.AddPolicy("Admin", p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim(KeycloakClaimTypes.Tenant, "Backoffice") && ctx.User.IsInRole("admin")));

    opts.AddPolicy("Customer", p => p.RequireAssertion(ctx =>
        ctx.User.HasClaim(KeycloakClaimTypes.Tenant, "Shop")
        || (ctx.User.HasClaim(KeycloakClaimTypes.Tenant, "Backoffice") && ctx.User.IsInRole("admin"))));
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

builder.Services.AddRateLimiter(opts =>
{
    opts.GlobalLimiter = System.Threading.RateLimiting.PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "anon",
            factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
    opts.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    opts.AddPolicy("admin", context =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.User.Identity?.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "anon",
            factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
});

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

if (args.Contains("--migrate"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<EwaysVNextDbContext>();
    if (db.Database.IsInMemory())
        Console.WriteLine(MessageFormat.Format(ProgramMessages.MigrateSkippedFormat, db.Database.ProviderName));
    else
        await DatabaseMigrator.MigrateAsync(scope.ServiceProvider);

    var authorizationDb = scope.ServiceProvider.GetRequiredService<AuthorizationDbContext>();
    if (authorizationDb.Database.IsInMemory())
    {
        Console.WriteLine(MessageFormat.Format(ProgramMessages.MigrateAuthorizationSkippedFormat, authorizationDb.Database.ProviderName));
    }
    else
    {
        await authorizationDb.Database.MigrateAsync();
    }

    Console.WriteLine(ProgramMessages.MigrateDone);

    return;
}

app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
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

app.UseMiddleware<GlobalExceptionHandlerMiddleware>();
app.UseCors("default");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapEndpoints();
app.MapGraphQLEndpoint(options =>
{
    options.Tool.Title = "EwaysVNext GraphQL";
    options.Tool.Enable = app.Environment.IsDevelopment();
});

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });

app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });

app.MapHealthChecks("/health");

app.Run();

public partial class Program { }

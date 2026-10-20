using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.HttpOverrides;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
    builder.Services.AddOpenTelemetry().UseAzureMonitor();

// Add services to the container.
builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Configure forwarded headers for Azure Container Apps
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

// Add CORS — reads AllowedOrigins from config/env vars (e.g. AllowedOrigins__0)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp",
        policy =>
        {
            var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() 
                ?? Array.Empty<string>();
            
            // Always include localhost for development
            var origins = new HashSet<string>(allowedOrigins)
            {
                "http://localhost:3000",
                "https://localhost:3000"
            };
            
            // Remove empty entries
            origins.RemoveWhere(string.IsNullOrWhiteSpace);
            
            if (origins.Count > 0)
            {
                policy.WithOrigins(origins.ToArray())
                      .AllowAnyHeader()
                      .AllowAnyMethod()
                      .AllowCredentials();
            }
            else
            {
                // Fallback: allow any origin without credentials
                policy.AllowAnyOrigin()
                      .AllowAnyHeader()
                      .AllowAnyMethod();
            }
        });
});

var app = builder.Build();
var startedAt = Stopwatch.StartNew();

// Use forwarded headers for Azure Container Apps
app.UseForwardedHeaders();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Remove UseHttpsRedirection for Azure Container Apps - ACA handles TLS termination
// app.UseHttpsRedirection();

// Use CORS
app.UseCors("AllowReactApp");

app.UseAuthorization();

app.MapControllers();

app.MapGet("/health", () =>
{
    var memoryLimitBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
    using var process = Process.GetCurrentProcess();
    var memoryUsedBytes = process.WorkingSet64;
    var memoryUtilization = memoryLimitBytes > 0
        ? (double)memoryUsedBytes / memoryLimitBytes
        : 0;
    var isHealthy = memoryLimitBytes > 0 && memoryUtilization < 0.9;
    var status = isHealthy ? "Healthy" : "Unhealthy";

    return Results.Json(new
    {
        status,
        uptimeSeconds = (long)startedAt.Elapsed.TotalSeconds,
        checks = new
        {
            memory = new
            {
                status,
                usedBytes = memoryUsedBytes,
                limitBytes = memoryLimitBytes,
                utilizationPercent = memoryLimitBytes > 0
                    ? Math.Round(memoryUtilization * 100, 1)
                    : (double?)null
            },
            downstreamServices = new
            {
                status = "Healthy",
                services = Array.Empty<string>()
            }
        }
    }, statusCode: isHealthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
});

app.Run();

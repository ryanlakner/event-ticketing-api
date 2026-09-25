using System.Text.Json.Serialization;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.OpenApi;
using Ticketing.Api.BackgroundJobs;
using Ticketing.Api.ExceptionHandling;
using Ticketing.Application;
using Ticketing.Application.Reservations;
using Ticketing.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication().AddInfrastructure(builder.Configuration);
builder.Services.Configure<ReservationOptions>(
    builder.Configuration.GetSection(ReservationOptions.SectionName)
);
builder.Services.AddHostedService<ReservationExpiryService>();

builder
    .Services.AddControllers(options =>
        // FluentValidation owns input validation; don't let MVC infer [Required] from nullability.
        options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true
    )
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter())
    );
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc(
        "v1",
        new OpenApiInfo
        {
            Title = "Event Ticketing API",
            Version = "v1",
            Description = "Create events, reserve seats, and confirm tickets without overselling.",
        }
    );
    options.IncludeXmlComments(
        Path.Combine(AppContext.BaseDirectory, $"{typeof(Program).Assembly.GetName().Name}.xml"),
        includeControllerXmlComments: true
    );
});

// App Service injects this setting when Application Insights is linked (see infra/).
if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor();
}

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Swagger:Enabled"))
{
    app.UseSwagger();
    app.UseSwaggerUI(options => options.DocumentTitle = "Event Ticketing API");
}

app.MapControllers();
app.MapHealthChecks("/health");

if (app.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
{
    await app.Services.ApplyMigrationsAsync();
}

await app.RunAsync();

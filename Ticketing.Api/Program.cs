using System.Text.Json.Serialization;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Ticketing.Api.BackgroundJobs;
using Ticketing.Api.ExceptionHandling;
using Ticketing.Api.Identity;
using Ticketing.Api.OpenApi;
using Ticketing.Application;
using Ticketing.Application.Abstractions.Identity;
using Ticketing.Application.Reservations;
using Ticketing.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication().AddInfrastructure(builder.Configuration);
builder.Services.Configure<ReservationOptions>(
    builder.Configuration.GetSection(ReservationOptions.SectionName)
);
builder.Services.AddHostedService<ReservationExpiryService>();

// Token settings (authority, issuer, audiences) bind from Authentication:Schemes:Bearer:
// Entra ID in Azure (set by Terraform), `dotnet user-jwts` locally.
builder
    .Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Fail closed: if an environment is missing issuer or audience settings, reject every
        // token rather than accepting tokens minted for some other application.
        options.TokenValidationParameters.ValidateIssuer = true;
        options.TokenValidationParameters.ValidateAudience = true;
    });

// Secure by default: every endpoint needs a signed-in user unless marked [AllowAnonymous].
builder
    .Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();

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
    options.AddSecurityDefinition(
        AuthorizeOperationFilter.SchemeName,
        new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "An Entra ID access token, or one from `dotnet user-jwts` locally.",
        }
    );
    options.OperationFilter<AuthorizeOperationFilter>();
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

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();

if (app.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
{
    await app.Services.ApplyMigrationsAsync();
}

await app.RunAsync();

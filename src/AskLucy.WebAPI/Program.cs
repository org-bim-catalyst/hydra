using System.Globalization;
using System.Text;
using System.Threading.RateLimiting;
using AskLucy.Application;
using AskLucy.Application.Abstractions;
using AskLucy.Infrastructure;
using AskLucy.Infrastructure.Auth;
using AskLucy.Persistence;
using AskLucy.WebAPI.Auth;
using AskLucy.WebAPI.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// --- Serilog structured logging (constitution &#167;4/&#167;14) ---
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture));

// --- Application / Persistence / Infrastructure (Clean Architecture composition root) ---
builder.Services.AddApplication(builder.Configuration);
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserAccessor, HttpContextCurrentUserAccessor>();

// --- JWT authentication (research.md Topic 1) ---
// JwtOptions is resolved lazily via IOptions (bound in AddInfrastructure), not read
// eagerly from builder.Configuration here — see the AddPersistence lazy-connection-string
// note above for why eager reads at this point in the pipeline are fragile.
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((bearerOptions, jwtOptions) =>
    {
        var jwt = jwtOptions.Value;
        bearerOptions.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("AdministratorOrSuperUser", policy => policy.RequireRole("Administrator", "Super User"));

// --- Rate limiting, tiered by role (research.md Topic 3 / FR-023) ---
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("ai-endpoints", context =>
    {
        var user = context.User;
        var isPrivileged = user.IsInRole("Administrator") || user.IsInRole("Super User");
        var partitionKey = user.Identity?.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = isPrivileged ? 100 : 20,
            QueueLimit = 0,
        });
    });
});

// --- CORS: explicit allow-list, replacing the legacy wildcard (research.md Topic 7) ---
// NOTE: read directly from builder.Configuration, which is safe for real hosting (all
// config sources are loaded before this line runs); unlike AddPersistence/JWT above, no
// WebApplicationFactory test currently exercises CORS, so the same lazy-resolution
// pattern hasn't been needed here yet — apply it the same way if one is added.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod());
});

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ProblemDetailsMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseCors("Frontend");

app.UseAuthentication();
app.UseAuthorization();

app.UseRateLimiter();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

/// <summary>Entry point marker for WebApplicationFactory-based integration tests.</summary>
public partial class Program;

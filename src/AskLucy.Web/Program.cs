using System.Globalization;
using System.Text;
using System.Threading.RateLimiting;
using AskLucy.Application;
using AskLucy.Application.Abstractions;
using AskLucy.Infrastructure;
using AskLucy.Infrastructure.Auth;
using AskLucy.Persistence;
using AskLucy.Web.Auth;
using AskLucy.Web.DevSeed;
using AskLucy.Web.Middleware;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Facebook;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
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
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserAccessor, HttpContextCurrentUserAccessor>();

// --- JWT authentication (research.md Topic 1) ---
// JwtOptions is resolved lazily via IOptions (bound in AddInfrastructure), not read
// eagerly from builder.Configuration here — see the AddPersistence lazy-connection-string
// note above for why eager reads at this point in the pipeline are fragile.
var externalAuthBuilder = builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer()
    // Transient scheme required by RemoteAuthenticationOptions.SignInScheme below, but never
    // actually written to: ExternalAuth.HandleTicketReceivedAsync always calls HandleResponse()
    // before the framework's default sign-in-to-cookie step would run (T073/FR-010/FR-034 —
    // token issuance goes through our own one-time-code exchange instead of a cookie).
    .AddCookie(ExternalAuth.TransientScheme);

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

// Read directly from builder.Configuration — safe here, same as the CORS note below (no
// WebApplicationFactory test overrides these keys, so the eager-read fragility that justifies
// lazy IOptions binding for Jwt/the connection string above doesn't apply to these).
//
// Registered ONLY when actually configured: ASP.NET Core's authentication middleware
// initializes every *request-handler* scheme (i.e. one with a CallbackPath, like Google/
// Facebook) on EVERY request, to check whether that request matches its callback path — an
// always-registered-but-empty ClientId/AppId throws on every single request (via
// OAuthOptions.Validate()), not just when the scheme is actually challenged. Social sign-in is
// optional (unlike Jwt), so dev/test environments without these configured simply don't get
// the scheme registered at all, rather than crashing the whole API.
var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
if (!string.IsNullOrEmpty(googleClientId))
{
    externalAuthBuilder.AddGoogle(GoogleDefaults.AuthenticationScheme, options =>
    {
        options.ClientId = googleClientId;
        options.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"] ?? string.Empty;
        options.SignInScheme = ExternalAuth.TransientScheme;
        options.CallbackPath = "/api/v1/auth/external/google/callback";
        options.ClaimActions.MapJsonKey("email_verified", "email_verified");
        options.Events.OnTicketReceived = ExternalAuth.HandleTicketReceivedAsync;
    });
}

var facebookAppId = builder.Configuration["Authentication:Facebook:AppId"];
if (!string.IsNullOrEmpty(facebookAppId))
{
    externalAuthBuilder.AddFacebook(FacebookDefaults.AuthenticationScheme, options =>
    {
        options.AppId = facebookAppId;
        options.AppSecret = builder.Configuration["Authentication:Facebook:AppSecret"] ?? string.Empty;
        options.SignInScheme = ExternalAuth.TransientScheme;
        options.CallbackPath = "/api/v1/auth/external/facebook/callback";
        options.Events.OnTicketReceived = ExternalAuth.HandleTicketReceivedAsync;
    });
}

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

    // Admin dashboard/user-management endpoints (specs/001-admin-dashboard) — a generous
    // per-user limit closes constitution §6's "every public endpoint is rate-limited"
    // gap for this feature's new endpoints, matching the ai-endpoints pattern above.
    options.AddPolicy("admin-endpoints", context =>
    {
        var partitionKey = context.User.Identity?.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 60,
            QueueLimit = 0,
        });
    });

    // Chat/conversation-management endpoints (specs/002-chat-history-management) — none of
    // these invoke an AI provider directly (that's ai-endpoints, above), but constitution §6
    // still requires every public endpoint to be rate-limited; ChatsController previously had
    // no policy at all (a pre-existing gap found while auditing this during T074), so this
    // closes it with the same generous, non-AI-cost-tiered shape as admin-endpoints.
    options.AddPolicy("chat-endpoints", context =>
    {
        var partitionKey = context.User.Identity?.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 120,
            QueueLimit = 0,
        });
    });

    // AI provider/model catalog, preferences, and usage-summary reads
    // (specs/005-multi-provider-ai-engine) — these don't invoke a provider (that's still
    // ai-endpoints, above), so they get a generous, non-cost-tiered limit like
    // admin-endpoints/chat-endpoints rather than the AI-invoking policy (research.md
    // Decision 6).
    options.AddPolicy("ai-catalog-endpoints", context =>
    {
        var partitionKey = context.User.Identity?.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 120,
            QueueLimit = 0,
        });
    });

    // Cookie-consent endpoints (specs/004-cookie-consent-privacy) — includes one anonymous
    // endpoint (GET /api/v1/cookie-policy), so the partition key falls back to remote IP for
    // unauthenticated callers, same as every other policy here.
    options.AddPolicy("consent-endpoints", context =>
    {
        var partitionKey = context.User.Identity?.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 120,
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

// Serves the React frontend's built assets (wwwroot is populated from ClientApp/dist on
// every build — see AskLucy.Web.csproj) and falls back to index.html for SPA client-side
// routes. A hand-written middleware rather than UseStaticFiles/MapFallbackToFile: the
// built-in StaticFileMiddleware never served files added to wwwroot by our PreBuildEvent
// copy target, even with an explicit PhysicalFileProvider pointed straight at the folder
// and the file independently verified present and readable — reproducible in this exact
// setup both locally and on the deployed host, root cause not pinned down (suspected
// interaction with the SDK's build-time Static Web Assets manifest/endpoint machinery,
// which MapFallbackToFile's single-fixed-file serving mode isn't subject to the same way
// path-matched StaticFileMiddleware is). This reads wwwroot directly off disk on every
// request via the same IFileInfo/CreateReadStream primitives, independently verified
// working here.
var wwwrootProvider = new PhysicalFileProvider(Path.Combine(app.Environment.ContentRootPath, "wwwroot"));
var staticContentTypeProvider = new FileExtensionContentTypeProvider();

app.Use(async (context, next) =>
{
    var requestPath = context.Request.Path.Value ?? "/";
    var relativePath = requestPath.TrimStart('/');

    if (relativePath.Length > 0)
    {
        var fileInfo = wwwrootProvider.GetFileInfo(relativePath);
        if (fileInfo.Exists && !fileInfo.IsDirectory)
        {
            if (!staticContentTypeProvider.TryGetContentType(fileInfo.Name, out var contentType))
            {
                contentType = "application/octet-stream";
            }

            context.Response.ContentType = contentType;
            context.Response.ContentLength = fileInfo.Length;
            await using var stream = fileInfo.CreateReadStream();
            await stream.CopyToAsync(context.Response.Body);
            return;
        }
    }

    // SPA fallback: any GET that isn't a real static file and isn't one of this app's own
    // non-file endpoints serves index.html so React Router can handle it. Excludes "/api",
    // "/health", and "/openapi" so a typo'd/missing API route, the health check, or the
    // generated OpenAPI document (constitution §6 — every endpoint MUST be discoverable via
    // it; found silently swallowed as the SPA shell here while verifying
    // specs/002-chat-history-management tasks.md T075) still reach routing below instead of
    // silently returning the SPA shell.
    if (HttpMethods.IsGet(context.Request.Method)
        && !requestPath.StartsWith("/api", StringComparison.OrdinalIgnoreCase)
        && !requestPath.StartsWith("/openapi", StringComparison.OrdinalIgnoreCase)
        && !requestPath.Equals("/health", StringComparison.OrdinalIgnoreCase))
    {
        var indexFile = wwwrootProvider.GetFileInfo("index.html");
        if (indexFile.Exists)
        {
            context.Response.ContentType = "text/html";
            context.Response.ContentLength = indexFile.Length;
            await using var stream = indexFile.CreateReadStream();
            await stream.CopyToAsync(context.Response.Body);
            return;
        }
    }

    await next();
});

app.UseHttpsRedirection();

app.UseCors("Frontend");

app.UseAuthentication();
app.UseAuthorization();

app.UseRateLimiter();

app.MapControllers();
app.MapHealthChecks("/health");

// Dev-only convenience seed (see DevAdminSeeder's doc comment / ADR-0001). Wrapped so a
// missing/unreachable database at startup degrades to a logged warning, not a crashed host —
// the rest of the app already tolerates the database being down until the first request needs it.
if (app.Environment.IsDevelopment())
{
    try
    {
        await DevAdminSeeder.SeedAsync(app.Services, app.Logger);
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Dev admin seed skipped — could not reach the database.");
    }
}

app.Run();

/// <summary>Entry point marker for WebApplicationFactory-based integration tests.</summary>
public partial class Program;

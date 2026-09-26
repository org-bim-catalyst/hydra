using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using AskLucy.Application;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Admin.Commands.IssueHangfireDashboardSession;
using AskLucy.Application.Authorization;
using AskLucy.Infrastructure;
using AskLucy.Infrastructure.Agents;
using AskLucy.Infrastructure.Auth;
using AskLucy.Infrastructure.CustomModels;
using AskLucy.Infrastructure.Documents;
using AskLucy.Infrastructure.Email;
using AskLucy.Infrastructure.Mcp;
using AskLucy.Infrastructure.Memory;
using AskLucy.Infrastructure.Panels;
using AskLucy.Infrastructure.Retrieval;
using AskLucy.Infrastructure.SiteAnalysis;
using AskLucy.Infrastructure.Workflows;
using AskLucy.Persistence;
using AskLucy.Persistence.HealthChecks;
using AskLucy.Web.Auth;
using AskLucy.Web.DevSeed;
using AskLucy.Web.Middleware;
using AskLucy.Web.StartupTasks;
using Hangfire;
using Hangfire.Dashboard;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Facebook;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
builder.Services.AddApplication(builder.Configuration, builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"));
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserAccessor, HttpContextCurrentUserAccessor>();
builder.Services.AddSingleton<ICorrelationIdAccessor, CorrelationIdAccessor>();

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

        // SignalR's browser client cannot set an Authorization header on the WebSocket/SSE
        // handshake (specs/015-document-intelligence-pipeline, research.md Decision 7) — it
        // sends the access token via an "access_token" query string parameter instead, which
        // JwtBearerHandler only reads here, not from the header, and only for hub paths.
        bearerOptions.Events = new JwtBearerEvents
        {
            // Browsers can't set the Authorization header on a WebSocket/SSE handshake, so hub
            // connections deliver the token another way. The query string is kept for any
            // client that still uses accessTokenFactory; the cookie is what SignalR clients
            // actually rely on now (AccessTokenCookie.cs) — it's attached fresh by the browser
            // on every negotiate/reconnect, unlike a JS-managed factory closure that goes stale
            // once the token it captured expires and is never re-read.
            OnMessageReceived = context =>
            {
                if (context.HttpContext.Request.Path.StartsWithSegments("/hangfire"))
                {
                    // A plain browser navigation to /hangfire (the new tab the admin panel
                    // opens) never carries an Authorization header, so this is the only way
                    // that request can ever authenticate (specs/060-hangfire-dashboard-access,
                    // research.md Decision 1). Read-only cookie name check here — the actual
                    // "was this token minted FOR the dashboard" check is the purpose claim
                    // below, checked after the framework validates the signature/expiry.
                    if (context.Request.Cookies.TryGetValue(HangfireDashboardCookie.Name, out var dashboardToken)
                        && !string.IsNullOrEmpty(dashboardToken))
                    {
                        context.Token = dashboardToken;
                    }

                    return Task.CompletedTask;
                }

                if (!context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                {
                    return Task.CompletedTask;
                }

                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken))
                {
                    context.Token = accessToken;
                    return Task.CompletedTask;
                }

                if (context.Request.Cookies.TryGetValue(AccessTokenCookie.Name, out var cookieToken) && !string.IsNullOrEmpty(cookieToken))
                {
                    context.Token = cookieToken;
                }

                return Task.CompletedTask;
            },

            // A signature and an expiry are the only things JWT validation checks on its own, so
            // a session revoked mid-token-lifetime would otherwise keep working until the token
            // aged out. Checked here rather than in a claims transformation because only this
            // hook can actually refuse the request.
            OnTokenValidated = async context =>
            {
                // Runs after signature/expiry validation, so claims can be trusted here — unlike
                // in OnMessageReceived above, which only decides *where* to read the token from.
                // A validly signed token minted for any other purpose (e.g. the SPA's normal
                // access token, replayed via this cookie) is rejected even though its signature
                // checks out (specs/060-hangfire-dashboard-access, research.md Decision 2).
                if (context.HttpContext.Request.Path.StartsWithSegments("/hangfire")
                    && context.Principal?.FindFirstValue(HangfireDashboardSessionClaims.PurposeClaimType)
                        != HangfireDashboardSessionClaims.PurposeClaimValue)
                {
                    context.Fail("Token is not valid for the Hangfire dashboard.");
                    return;
                }

                var validator = context.HttpContext.RequestServices.GetRequiredService<ActiveSessionTokenValidator>();

                if (!await validator.IsStillActiveAsync(context.Principal!, context.HttpContext.RequestAborted))
                {
                    context.Fail("This session has been signed out.");
                }
            },
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
        options.Scope.Add("profile");
        options.ClaimActions.MapJsonKey("email_verified", "email_verified");
        options.ClaimActions.MapJsonKey("urn:google:picture", "picture");
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
        options.Fields.Add("first_name");
        options.Fields.Add("last_name");
        options.Fields.Add("picture");
        options.ClaimActions.MapJsonKey(ClaimTypes.GivenName, "first_name");
        options.ClaimActions.MapJsonKey(ClaimTypes.Surname, "last_name");
        options.ClaimActions.MapCustomJson("urn:facebook:picture", user => user
            .GetProperty("picture")
            .GetProperty("data")
            .GetProperty("url")
            .GetString());
        options.Events.OnTicketReceived = ExternalAuth.HandleTicketReceivedAsync;
    });
}

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("AdministratorOrSuperUser", policy => policy.RequireRole("Administrator", "Super User"));

// --- Role Definition, Role Assignment & Permission Catalogue (specs/055-role-management) ---
// Replaces the JWT's baked-in role claims with the caller's *current* role/permissions on every
// request (research.md Decision 3) — fixes the pre-existing gap where a role change only took
// effect after the 15-minute access token expired, for every existing IsInRole caller too.
builder.Services.AddScoped<IClaimsTransformation, CurrentAuthorizationClaimsTransformation>();
builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationCacheInvalidator, MemoryCacheAuthorizationCacheInvalidator>();
builder.Services.Replace(ServiceDescriptor.Scoped<IAuthorizationMiddlewareResultHandler, PermissionDeniedAuditResultHandler>());
builder.Services.AddHostedService<PermissionCatalogReconciler>();

// --- Session revocation enforced on the access token, not just the refresh cookie ---
// Same shape as the role-claims gap directly above, and for the same reason: a JWT keeps working
// on its own signature, so revoking a session has to be *checked* per request or it lands up to a
// whole access-token lifetime late. See ActiveSessionTokenValidator.
builder.Services.AddScoped<ActiveSessionTokenValidator>();
builder.Services.AddScoped<ISessionRevocationCache, MemoryCacheSessionRevocationCache>();

// --- Rate limiting, tiered by role (research.md Topic 3 / FR-023) ---
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("ai-endpoints", context =>
    {
        var user = context.User;
        var isPrivileged = user.IsInRole("Administrator") || user.IsInRole("Super User");
        var partitionKey = RateLimitPartitions.UserOrClientKey(context);

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = isPrivileged ? 100 : 20,
            QueueLimit = 0,
        });
    });

    // Password recovery/management endpoints (specs/058-password-recovery). Partitioned on the
    // client IP rather than identity: the forgot-password and reset endpoints are anonymous, and
    // keying a visible 429 to an email address would itself be an account-existence oracle
    // (FR-003). The per-email throttle that complements this lives in the handler, where it can
    // silently no-op — see research.md Topic 3.
    options.AddPolicy("auth-endpoints", context =>
    {
        var partitionKey = context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 10,
            QueueLimit = 0,
        });
    });

    // Admin dashboard/user-management endpoints (specs/001-admin-dashboard) — a generous
    // per-user limit closes constitution §6's "every public endpoint is rate-limited"
    // gap for this feature's new endpoints, matching the ai-endpoints pattern above.
    options.AddPolicy("admin-endpoints", context =>
    {
        var partitionKey = RateLimitPartitions.UserOrClientKey(context);

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
        var partitionKey = RateLimitPartitions.UserOrClientKey(context);

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
        var partitionKey = RateLimitPartitions.UserOrClientKey(context);

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 120,
            QueueLimit = 0,
        });
    });

    // Knowledge Base management endpoints (specs/014-knowledge-base-management) — none of
    // these invoke an AI provider directly, so they get the same generous, non-cost-tiered
    // limit as chat-endpoints/ai-catalog-endpoints rather than the AI-invoking policy.
    options.AddPolicy("knowledge-base-endpoints", context =>
    {
        var partitionKey = RateLimitPartitions.UserOrClientKey(context);

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
        var partitionKey = RateLimitPartitions.UserOrClientKey(context);

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 120,
            QueueLimit = 0,
        });
    });

    // Document Intelligence Pipeline endpoints (specs/015-document-intelligence-pipeline) — none
    // of these invoke an AI provider directly (classification/language detection are background
    // jobs, not request-synchronous), so this gets the same generous, non-cost-tiered shape as
    // knowledge-base-endpoints/chat-endpoints (constitution §6).
    options.AddPolicy("document-endpoints", context =>
    {
        var partitionKey = RateLimitPartitions.UserOrClientKey(context);

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 120,
            QueueLimit = 0,
        });
    });

    // Upload-chunk endpoint specifically (research.md Decision 6) — tighter than
    // document-endpoints above since each call carries a file chunk's worth of I/O cost, not a
    // cheap metadata read/write.
    options.AddPolicy("document-upload-chunk-endpoints", context =>
    {
        var partitionKey = RateLimitPartitions.UserOrClientKey(context);

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 30,
            QueueLimit = 0,
        });
    });

    // RAG search/dashboard endpoints (specs/016-rag-semantic-search, research.md Decision 12) —
    // none of these invoke the chat AI provider directly (that's still ai-endpoints), so they get
    // the same generous, non-cost-tiered shape as knowledge-base-endpoints/document-endpoints.
    options.AddPolicy("retrieval-search-endpoints", context =>
    {
        var partitionKey = RateLimitPartitions.UserOrClientKey(context);

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 120,
            QueueLimit = 0,
        });
    });

    // RAG indexing-trigger endpoints (research.md Decision 12) — tighter than search, mirroring
    // document-upload-chunk-endpoints, given the cost of a full/incremental reindex.
    options.AddPolicy("retrieval-indexing-endpoints", context =>
    {
        var partitionKey = RateLimitPartitions.UserOrClientKey(context);

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 10,
            QueueLimit = 0,
        });
    });

    // AI Memory System endpoints (specs/018-ai-memory-system, research.md Decision 17) — the
    // CRUD/browse/preferences API surface never invokes the chat AI provider directly (extraction/
    // conflict-detection/sensitivity-classification all run in background jobs), so this gets the
    // same generous, non-cost-tiered shape as knowledge-base-endpoints/retrieval-search-endpoints.
    options.AddPolicy("memory-endpoints", context =>
    {
        var partitionKey = RateLimitPartitions.UserOrClientKey(context);

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 120,
            QueueLimit = 0,
        });
    });

    // Prompt Library CRUD/organization/versioning/test-case/export-import endpoints
    // (specs/019-prompt-library-workspace, contracts/prompts-api.md) — none of these invoke an AI
    // provider directly, so they get the same generous, non-cost-tiered shape as
    // knowledge-base-endpoints/memory-endpoints. The prompt *execution* endpoint
    // (POST /api/v1/prompts/{id}/executions) invokes IAIProvider directly and uses the
    // cost-tiered "ai-endpoints" policy above instead, not this one.
    options.AddPolicy("prompt-endpoints", context =>
    {
        var partitionKey = RateLimitPartitions.UserOrClientKey(context);

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 120,
            QueueLimit = 0,
        });
    });

    // Agent Framework endpoints (specs/020-ai-agent-framework) — same generous, non-cost-tiered
    // shape as prompt-endpoints/memory-endpoints: starting an execution only enqueues a Hangfire
    // job (cheap), it never synchronously invokes an AI provider within the request itself, so it
    // doesn't need the cost-tiered "ai-endpoints" policy. FR-042's per-user concurrency cap, not
    // HTTP rate limiting, is what bounds actual AI cost exposure for this feature.
    options.AddPolicy("agent-endpoints", context =>
    {
        var partitionKey = RateLimitPartitions.UserOrClientKey(context);

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 120,
            QueueLimit = 0,
        });
    });

    // Workflow & Tool Orchestration Engine (specs/022-workflow-orchestration-engine) — same
    // generous, non-cost-tiered shape as agent-endpoints: starting an execution only enqueues a
    // Hangfire job (cheap); FR-069's per-user concurrency cap, not HTTP rate limiting, is what
    // bounds actual execution/AI cost exposure.
    options.AddPolicy("workflow-endpoints", context =>
    {
        var partitionKey = RateLimitPartitions.UserOrClientKey(context);

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 120,
            QueueLimit = 0,
        });
    });

    // specs/057-site-analysis-agent — read-only rehydration (GET .../site-analyses), same tier as
    // workflow-endpoints.
    options.AddPolicy("site-analysis-endpoints", context =>
    {
        var partitionKey = RateLimitPartitions.UserOrClientKey(context);

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 120,
            QueueLimit = 0,
        });
    });

    // MCP server administration (specs/021-mcp-integration) — Administrator/Super User only, same
    // admin-CRUD cost tier as admin-endpoints; a dedicated policy (not a reuse of admin-endpoints)
    // because test-connection/refresh-capabilities make outbound calls to external MCP servers,
    // which is a materially different cost/risk profile worth capping independently of the rest
    // of the admin surface.
    options.AddPolicy("mcp-admin-endpoints", context =>
    {
        var partitionKey = RateLimitPartitions.UserOrClientKey(context);

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 60,
            QueueLimit = 0,
        });
    });

    // MCP catalog browsing (specs/021-mcp-integration, User Story 4's McpCatalogController) — any
    // authenticated user, read-only, never invokes an MCP server directly (that only happens
    // during agent execution, which the per-server/per-tool IMcpRateLimiter already bounds) — same
    // generous, non-cost-tiered shape as agent-endpoints.
    options.AddPolicy("mcp-endpoints", context =>
    {
        var partitionKey = RateLimitPartitions.UserOrClientKey(context);

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 120,
            QueueLimit = 0,
        });
    });

    // Funnel/CTA analytics event recording (specs/023-flumeria-landing-experience,
    // contracts/analytics-funnel-events-api.md) — always anonymous (fired from the public
    // landing/auth pages before any session exists), so the partition key is effectively
    // always the caller's IP. Tighter than the generous 120/min authenticated-CRUD policies
    // above: this endpoint has no legitimate reason to be called more than a few times per
    // minute by one visitor (one event per CTA click / funnel completion).
    options.AddPolicy("analytics-endpoints", context =>
    {
        var partitionKey = RateLimitPartitions.UserOrClientKey(context);

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 30,
            QueueLimit = 0,
        });
    });

    // specs/027-immersive-viewer-platform (contracts/weather-api.md): a simple pass-through
    // lookup, not AI-invoking — generous enough for the widget's periodic refresh plus manual
    // reloads/multiple tabs, tight enough to not become a free proxy for the upstream provider.
    options.AddPolicy("weather-endpoints", context =>
    {
        var partitionKey = RateLimitPartitions.UserOrClientKey(context);

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 30,
            QueueLimit = 0,
        });
    });

    // specs/028-ai-floating-panels (contracts/panel-preferences-api.md): a simple preference
    // read/write, not AI-invoking — same generous, non-cost-tiered shape as weather-endpoints
    // (constitution §6, caught during /speckit-analyze as a gap this plan initially missed).
    options.AddPolicy("panels-endpoints", context =>
    {
        var partitionKey = RateLimitPartitions.UserOrClientKey(context);

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 30,
            QueueLimit = 0,
        });
    });

    // specs/052-solar-analysis contracts/building-footprints-endpoint.md — fronts a shared free
    // Overpass service that has its own limits and has already returned 429 to this system
    // (research D4); same generous, non-AI-cost-tiered shape as weather-endpoints (§6 requires
    // every public endpoint carry a rate-limit policy — WeatherController is the precedent).
    options.AddPolicy("buildings-endpoints", context =>
    {
        var partitionKey = RateLimitPartitions.UserOrClientKey(context);

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 30,
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
        .AllowAnyMethod()
        .AllowCredentials());
});

// Global fix (discovered during specs/015 US7 work): AddControllers() alone leaves enums
// serializing as their raw numeric ordinal (System.Text.Json's default), but every DTO across
// every module returns enums directly and every frontend TypeScript type/comparison assumes a
// string (e.g. `status.processingStatus === 'Completed'`). Without this converter, every such
// comparison silently evaluates to false at runtime — verified empirically before this fix:
// serializing a DocumentProcessingStatus.Completed field produced {"status":3}, not
// {"status":"Completed"}. Applied globally (not per-DTO) since the mismatch is universal, not
// specific to the Documents module.
builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks()
    // specs/029-fix-chat-widget-bugs FR-012, research.md Decision 2 — a readiness signal
    // (tagged "ready", surfaced at /health/ready below, kept separate from the plain
    // liveness /health mapping) that catches an unapplied EF Core migration before it
    // manifests as a live-request 500, the root cause of this feature's Bug 1.
    .AddCheck<PendingMigrationsHealthCheck>("pending-migrations", tags: ["ready"])
    // specs/045 T105 — degraded (not unhealthy) when the last system-agent provisioning pass was
    // deferred, so the gap is visible on /health/ready rather than silent.
    .AddCheck<AskLucy.Web.HealthChecks.SystemAgentProvisioningHealthCheck>("system-agent-provisioning", tags: ["ready"]);
builder.Services.AddSignalR();

var app = builder.Build();

// Applies any pending EF Core migrations on every startup, in every environment — closes the
// exact gap that let 20260914180820_AddRoleManagement.cs ship merged to main but never actually
// run against production (2026-09-16 incident: Roles/Role-assignments 500s from missing
// columns). /health/ready's PendingMigrationsHealthCheck above only ever reported that gap;
// nothing previously closed it. A failed migration is logged critical but does not crash the
// host, matching this file's existing tolerance of the database being briefly unreachable at
// startup (see the dev-seed block below) — an operator watching /health/ready still sees it.
try
{
    using var migrationScope = app.Services.CreateScope();
    var migrationDbContext = migrationScope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
    await migrationDbContext.Database.MigrateAsync();
}
catch (Exception ex)
{
#pragma warning disable CA1848
    app.Logger.LogCritical(ex, "Automatic database migration failed at startup.");
#pragma warning restore CA1848
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ProblemDetailsMiddleware>();

// One structured summary line per request (method, path, status code, elapsed ms), covering
// every request this pipeline handles — including the ones a controller never sees (a failed
// static-file lookup, auth/authorization rejections, a rate-limit rejection, a routing miss).
// Placed after CorrelationIdMiddleware so CorrelationId is already in Serilog's LogContext and
// tags this line too, and after ProblemDetailsMiddleware so the status code logged here is the
// real, already-translated Problem Details status rather than a since-rewritten one. Closes the
// exact gap that made a pre-controller failure (e.g. the DI-cycle hang this session's
// RequestSiteAnalysisCapability fix addressed) produce no log line at all — see
// di_selfreferential_factory_deadlock's "absence of a log proves nothing" note.
app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Serves the React frontend's built static assets (wwwroot is populated from ClientApp/dist
// on every build — see AskLucy.Web.csproj). A hand-written middleware rather than
// UseStaticFiles: the built-in StaticFileMiddleware never served files added to wwwroot by
// our PreBuildEvent copy target, even with an explicit PhysicalFileProvider pointed straight
// at the folder and the file independently verified present and readable — reproducible in
// this exact setup both locally and on the deployed host, root cause not pinned down
// (suspected interaction with the SDK's build-time Static Web Assets manifest/endpoint
// machinery). This reads wwwroot directly off disk on every request via the same
// IFileInfo/CreateReadStream primitives, independently verified working here. The SPA
// index.html fallback is a separate concern, registered as app.MapFallback further below
// (specs/029-fix-chat-widget-bugs research.md Decision 7) — deliberately not merged back
// into this middleware, so this static-asset-serving path (already fixed once) stays
// untouched by that fix.
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

    await next();
});

app.UseHttpsRedirection();

app.UseCors("Frontend");

app.UseAuthentication();
app.UseAuthorization();

app.UseRateLimiter();

// Overrides the dashboard's stock colors with the app's own palette tokens
// (specs/060-hangfire-dashboard-access, research.md Decision 3). Hangfire has no
// DashboardOptions.StylesheetFiles property in 1.8.x — custom stylesheets are registered once,
// globally, as embedded resources via DashboardRoutes.Routes, keyed by manifest resource name
// (see AskLucy.Web.csproj's <EmbeddedResource> for HangfireTheme/*.css). DarkModeEnabled=true
// is a static on/off switch, not a per-request theme choice — Hangfire itself wraps the dark
// stylesheet in `@media (prefers-color-scheme: dark)` and has no other toggle-persistence
// mechanism (confirmed by inspecting Hangfire.Core.dll directly; no cookie/localStorage/query
// param hook exists), so which one a browser renders follows the OS/browser color scheme, not
// the SPA's own theme store.
DashboardRoutes.AddStylesheet(typeof(Program).Assembly, "AskLucy.Web.HangfireTheme.hangfire-theme.css");
DashboardRoutes.AddStylesheetDarkMode(typeof(Program).Assembly, "AskLucy.Web.HangfireTheme.hangfire-theme-dark.css");

// Document Intelligence Pipeline's job dashboard (specs/015-document-intelligence-pipeline,
// research.md Decision 2) — administrator/operator-only, see HangfireDashboardAuthorizationFilter
// for why a direct browser visit won't authenticate on this JWT-Bearer-only host.
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = [new HangfireDashboardAuthorizationFilter()],
    DarkModeEnabled = true,
});

// US6 — refreshes DocumentStatistics (dashboard aggregates) every minute; the dashboard's live
// counts (queue depth, in-progress, etc.) are computed directly on every request instead, so
// this interval only governs the slower-changing totals/storage/distribution fields
// (data-model.md, SC-011). Idempotent — safe to call on every startup.
RecurringJob.AddOrUpdate<DocumentStatisticsRecomputeJob>(
    "document-statistics-recompute", job => job.RecomputeAllAsync(CancellationToken.None), Cron.Minutely);

// AI Memory System (specs/018-ai-memory-system, research.md Decision 6/18) — the sweep is the
// safety net for the per-turn enqueue in SendChatMessageCommandHandler; cleanup purges expired/
// stale-archived memories daily (FR-031). Both idempotent — safe to call on every startup.
RecurringJob.AddOrUpdate<MemoryExtractionSweepJob>(
    "memory-extraction-sweep", job => job.RunAsync(CancellationToken.None), "*/15 * * * *");
RecurringJob.AddOrUpdate<MemoryCleanupJob>(
    "memory-cleanup", job => job.RunAsync(CancellationToken.None), Cron.Daily);

// specs/058-password-recovery T054 — drops spent password reset tokens past their 90-day
// retention. Retention housekeeping, not security: the tokens are already inert. Idempotent.
RecurringJob.AddOrUpdate<PasswordResetTokenCleanupJob>(
    "password-reset-token-cleanup", job => job.RunAsync(CancellationToken.None), Cron.Daily);

// spec 021-mcp-integration User Story 6 (research.md Decision 10) — a 5-minute cadence matching
// McpRuntimeOptions.HealthCheckIntervalMinutes's own default; each run only actually
// checks/refreshes what's due (health check: every enabled server every cycle; capability
// refresh: only servers past their own per-server CapabilityRefreshIntervalMinutes). Both
// idempotent — safe to call on every startup.
RecurringJob.AddOrUpdate<McpServerHealthCheckJob>(
    "mcp-server-health-check", job => job.RunAsync(CancellationToken.None), "*/5 * * * *");
RecurringJob.AddOrUpdate<McpCapabilityRefreshJob>(
    "mcp-capability-refresh", job => job.RunAsync(CancellationToken.None), "*/5 * * * *");

app.MapControllers();
// specs/029-fix-chat-widget-bugs contracts/health-readiness-endpoint.md — /health (liveness)
// MUST stay unaffected by the new "ready"-tagged check below (data-model.md: "keeping
// liveness and readiness semantics distinct"). MapHealthChecks runs every registered check
// when no Predicate is given, so without this exclusion /health would also start failing
// whenever a migration is merely pending — found via T007's own integration test actually
// running, not by inspection.
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = check => !check.Tags.Contains("ready"),
});
// Additive — does not replace /health above. Deployment/orchestration tooling opts into
// this signal separately.
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
});
app.MapHub<DocumentProcessingHub>("/hubs/document-processing");
app.MapHub<RetrievalIndexingHub>("/hubs/retrieval-indexing");
app.MapHub<MemoryHub>("/hubs/memory");
app.MapHub<AgentExecutionHub>("/hubs/agent-execution");
app.MapHub<WorkflowExecutionHub>("/hubs/workflow-execution");
app.MapHub<PanelHub>("/hubs/panels");
app.MapHub<SiteAnalysisHub>("/hubs/site-analysis");
app.MapHub<CustomModelDeploymentHub>("/hubs/custom-model-deployments");

// SPA fallback: any GET that didn't match a static file (the app.Use above) or any endpoint
// mapped above (controllers, hubs, health checks, OpenAPI) serves index.html so React Router
// can handle the client-side route. Registered as app.MapFallback — an endpoint-routing
// endpoint with the lowest possible match priority — rather than the previous hand-rolled
// prefix-exclusion list (specs/029-fix-chat-widget-bugs research.md Decision 7). That list
// only knew about "/api", "/openapi", and "/health" and was never updated for "/hubs", so
// every SignalR hub handshake GET (all 6 hubs, not just /hubs/panels) was silently served
// index.html instead of reaching MapHub — the production root cause of
// "EventSource... MIME type (\"text/html\")" / WebSocket handshake failures. MapFallback
// can't repeat that mistake: any endpoint explicitly mapped above always wins automatically,
// by routing precedence, with no list left to fall out of sync.
app.MapFallback(async context =>
{
    if (!HttpMethods.IsGet(context.Request.Method))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    var indexFile = wwwrootProvider.GetFileInfo("index.html");
    if (indexFile.Exists)
    {
        context.Response.ContentType = "text/html";
        context.Response.ContentLength = indexFile.Length;
        await using var stream = indexFile.CreateReadStream();
        await stream.CopyToAsync(context.Response.Body);
        return;
    }

    context.Response.StatusCode = StatusCodes.Status404NotFound;
});

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
        // Single dev-only startup call, not a hot path — a LoggerMessage delegate adds
        // ceremony without a measurable benefit here.
#pragma warning disable CA1848
        app.Logger.LogWarning(ex, "Dev admin seed skipped — could not reach the database.");
#pragma warning restore CA1848
    }

    try
    {
        await DevAiProviderSeeder.SeedAsync(app.Services, app.Logger);
    }
    catch (Exception ex)
    {
#pragma warning disable CA1848
        app.Logger.LogWarning(ex, "Dev AI provider seed skipped — could not reach the database.");
#pragma warning restore CA1848
    }

    try
    {
        await DevBaselineSeeder.SeedAsync(app.Services, app.Logger);
    }
    catch (Exception ex)
    {
#pragma warning disable CA1848
        app.Logger.LogWarning(ex, "Dev baseline seed skipped — could not reach the database.");
#pragma warning restore CA1848
    }
}

// specs/066 FR-009: backfills CredentialHint for any provider credential configured before this
// feature shipped. Unlike the dev-only seeders above, this must run in every environment —
// production credentials predate the feature too — so it sits outside the IsDevelopment() guard.
// Wrapped the same way: a missing/unreachable database at startup degrades to a logged warning,
// not a crashed host.
try
{
    await CredentialHintBackfillService.RunAsync(app.Services, app.Logger);
}
catch (Exception ex)
{
#pragma warning disable CA1848
    app.Logger.LogWarning(ex, "Credential hint backfill skipped — could not reach the database.");
#pragma warning restore CA1848
}

// Which geocoding provider is live is decided silently by whether Geocoding:GoogleMapsApiKey
// happens to be configured (Infrastructure/DependencyInjection.cs), and the two report their
// candidate `importance` on visibly different scales — Google synthesises 0.40-0.90 from
// location_type, Nominatim returns a real Wikipedia-linkage popularity score where an ordinary
// local park sits near 0.08. A deployment with a key and a dev machine without one therefore
// resolve the same place query differently, which is exactly how "show me Al Safa Park 2"
// worked in Production and returned NotFound locally with nothing in the logs to say why.
// Stated once at startup so that divergence can never again read as a code regression.
var hasGeocodingKey = !string.IsNullOrWhiteSpace(app.Configuration["Geocoding:GoogleMapsApiKey"]);
// CA1873 is a false positive here: it flags the call even after the arguments are hoisted into
// plain locals, and this is a single startup statement, not a hot path — nothing to defer.
#pragma warning disable CA1848, CA1873
app.Logger.LogInformation(
    "Geocoding provider: {Provider} (Geocoding:GoogleMapsApiKey {KeyState}).",
    hasGeocodingKey ? "GoogleMaps" : "Nominatim",
    hasGeocodingKey ? "configured" : "not configured");
#pragma warning restore CA1848, CA1873

app.Run();

/// <summary>Entry point marker for WebApplicationFactory-based integration tests.</summary>
public partial class Program;

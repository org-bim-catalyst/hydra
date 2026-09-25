using AskLucy.Application.Abstractions;
using AskLucy.Infrastructure.OperationalFailures;

namespace AskLucy.Web.Middleware;

/// <summary>
/// specs/074 research D11: a running background job's id first, then the id
/// <see cref="CorrelationIdMiddleware"/> assigned to the current request, otherwise null. Lives in
/// Web beside <see cref="AskLucy.Web.Auth.HttpContextCurrentUserAccessor"/> because Infrastructure
/// has no ASP.NET Core reference; the job half lives in Infrastructure, where the Hangfire filter
/// sets it.
/// </summary>
public sealed class CorrelationIdAccessor(IHttpContextAccessor httpContextAccessor) : ICorrelationIdAccessor
{
    public string? Current =>
        JobCorrelationContext.Current
        ?? (httpContextAccessor.HttpContext?.Items.TryGetValue(CorrelationIdKeys.ItemsKey, out var value) == true
            ? value as string
            : null);
}

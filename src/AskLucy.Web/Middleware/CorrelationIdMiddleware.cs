namespace AskLucy.Web.Middleware;

/// <summary>
/// Assigns a correlation id at the edge of every request and propagates it through logs
/// and error responses (constitution &#167;14).
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var existing) && existing.Count > 0
            ? existing.ToString()
            : Guid.CreateVersion7().ToString();

        context.Items[HeaderName] = correlationId;

        using (Serilog.Context.LogContext.PushProperty("CorrelationId", correlationId))
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers[HeaderName] = correlationId;
                return Task.CompletedTask;
            });

            await next(context);
        }
    }
}

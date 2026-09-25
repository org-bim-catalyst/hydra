namespace AskLucy.Application.Abstractions;

/// <summary>Where the request's correlation id is kept, shared by the middleware that writes it and the accessor that reads it.</summary>
public static class CorrelationIdKeys
{
    /// <summary>The <c>HttpContext.Items</c> key. Same value as the response header name.</summary>
    public const string ItemsKey = "X-Correlation-Id";
}

using System.Globalization;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Web.Localization;

namespace AskLucy.Web.Middleware;

/// <summary>
/// Sets <see cref="CultureInfo.CurrentUICulture"/> to the caller's effective language on endpoints marked
/// <see cref="LocalizedSurfaceAttribute"/> (research R15). It runs after authentication, so there is a user to resolve for; an
/// anonymous caller, or any endpoint without the attribute, keeps the default English.
/// </summary>
/// <remarks>
/// The culture also goes into <c>HttpContext.Items</c>: it is an async-local value, so a change made here doesn't reach the Problem Details
/// middleware that wraps this one, and that middleware localizes the response text from the stored value instead.
/// </remarks>
public sealed class LocalizedSurfaceCultureMiddleware(RequestDelegate next)
{
    public const string CultureItemKey = "AskLucy.LocalizedSurfaceCulture";

    public async Task InvokeAsync(HttpContext context, ICurrentUserAccessor currentUser, IEffectiveLanguageResolver resolver)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<LocalizedSurfaceAttribute>() is not null && currentUser.UserId is { } userId)
        {
            // A failure to resolve is a real failure: it propagates to the Problem Details middleware instead of silently answering in English.
            var language = await resolver.ResolveAsync(userId, null, context.RequestAborted);
            var culture = CultureInfo.GetCultureInfo(language);
            CultureInfo.CurrentUICulture = culture;
            context.Items[CultureItemKey] = culture;
        }

        await next(context);
    }
}

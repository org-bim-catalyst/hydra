namespace AskLucy.Web.Localization;

/// <summary>
/// Opts an endpoint into server-side localization (specs/067 research R15): on it, validation messages and Problem Details text follow the
/// caller's effective language. Every other endpoint stays English, so the rest of the app is unchanged (the Arabic scope).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class LocalizedSurfaceAttribute : Attribute;

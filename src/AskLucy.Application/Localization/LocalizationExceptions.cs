namespace AskLucy.Application.Localization;

/// <summary>A localization change that can't be accepted: localization is off, or a language isn't supported or provided. Mapped to 422.</summary>
public sealed class LocalizationRejectedException(string message) : Exception(message);

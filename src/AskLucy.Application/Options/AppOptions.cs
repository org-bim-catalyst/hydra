namespace AskLucy.Application.Options;

public sealed class AppOptions
{
    public const string SectionName = "App";

    public required string FrontendBaseUrl { get; init; }
}

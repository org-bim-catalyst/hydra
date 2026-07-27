namespace AskLucy.Infrastructure.Email;

public sealed class SendGridOptions
{
    public const string SectionName = "SendGrid";

    public required string ApiKey { get; init; }

    public string FromEmail { get; init; } = "no-reply@asklucy.io";

    public string FromName { get; init; } = "Ask Lucy";
}

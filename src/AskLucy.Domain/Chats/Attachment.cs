using AskLucy.Domain.Common;

namespace AskLucy.Domain.Chats;

/// <summary>
/// A file reference associated with a <see cref="Message"/> (FR-017) — an uploaded document
/// or a generated image already produced by an existing capability (chat/translate/image
/// generation). This persists the reference; it does not introduce new upload/storage
/// capability (spec.md Assumptions). Child of <see cref="Message"/>'s aggregate, not
/// independently reachable (constitution &#167;5) — created only via <see cref="Message.AddAttachment"/>.
/// </summary>
public sealed class Attachment : BaseEntity
{
    public Guid MessageId { get; private set; }

    public string FileName { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = string.Empty;

    /// <summary>The existing signed-URL/storage reference the file is already served from — never a raw physical path (CLAUDE.md File Management).</summary>
    public string AccessLocation { get; private set; } = string.Empty;

    private Attachment()
    {
        // Required by EF Core materialization.
    }

    internal static Attachment Create(Guid messageId, string fileName, string contentType, string accessLocation, string actor)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new DomainRuleViolationException("An attachment file name is required.");
        }

        if (string.IsNullOrWhiteSpace(accessLocation))
        {
            throw new DomainRuleViolationException("An attachment access location is required.");
        }

        return new Attachment
        {
            Id = Guid.CreateVersion7(),
            MessageId = messageId,
            FileName = fileName,
            ContentType = contentType,
            AccessLocation = accessLocation,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }
}

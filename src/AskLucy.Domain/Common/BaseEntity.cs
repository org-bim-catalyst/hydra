namespace AskLucy.Domain.Common;

/// <summary>
/// Base type for every aggregate root: surrogate key, audit trail, soft delete, and
/// optimistic concurrency, per constitution &#167;5 (Database Principles).
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTime? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    public DateTime? DeletedAtUtc { get; set; }

    public string? DeletedBy { get; set; }

    public bool IsDeleted => DeletedAtUtc is not null;

    public byte[] RowVersion { get; set; } = [];
}

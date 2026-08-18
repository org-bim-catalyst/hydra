using AskLucy.Application.Abstractions;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AskLucy.Persistence.Memory;

/// <summary>
/// Encrypts/decrypts a string column at rest via <see cref="IMemoryContentProtector"/> (research.md
/// Decision 12) — applied to <c>Memory.Content</c>, <c>MemoryVersion.PreviousContent</c>, and
/// <c>MemoryReference.ContentSnapshot</c>. Every one of them is PII by construction (personal
/// facts/preferences about a specific user), so constitution §8's "sensitive PII... uses
/// column/field-level encryption" clause applies to the whole column, not only rows flagged
/// <c>IsSensitive</c>.
///
/// <para>Safe under EF Core's per-property change tracking despite the protector's
/// non-deterministic output (a fresh random IV per call): the converter only runs when EF has
/// already determined the CLR string value changed (a real edit), never on every
/// <c>SaveChanges</c> for an unrelated field change (e.g. <c>Memory.Reinforce</c> bumping
/// <c>FrequencyCount</c>) — EF compares the in-memory CLR snapshot, not re-derives the DB value, to
/// decide whether a property needs writing.</para>
/// </summary>
public sealed class EncryptedStringConverter(IMemoryContentProtector protector) : ValueConverter<string, string>(
    plaintext => protector.Protect(plaintext),
    ciphertext => protector.Unprotect(ciphertext));

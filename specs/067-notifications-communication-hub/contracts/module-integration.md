# Contract: Module Integration & Internal Abstractions

This contract is internal: it defines how platform modules request notifications (FR-001, FR-002) and the seams that keep channels and providers replaceable (FR-023, FR-060). The Application-layer interfaces live in `AskLucy.Application/Notifications/Abstractions/`.

---

## `INotificationPublisher`: the only entry point for modules

```csharp
public interface INotificationPublisher
{
    /// Adds a durable outbox event to the current unit of work. Performs no I/O and never throws
    /// for delivery reasons; it throws only for programming errors (unknown type, undeclared
    /// variable, malformed recipient), which surface in tests. The caller commits it with its own
    /// IUnitOfWork.SaveChangesAsync (research R2).
    void Publish(NotificationRequest request);
}

public sealed record NotificationRequest(
    string Type,                                      // NotificationTypeCatalog key
    NotificationRecipient Recipient,
    IReadOnlyDictionary<string, string?> Variables,   // declared variables only; plain values, never HTML/URLs
    RelatedItem? RelatedItem = null,                  // (Type, Id) - drives route + availability checks
    string? EventKey = null,                          // de-duplication identity (FR-008)
    string? Language = null);                         // explicit language, the first FR-044 candidate

public abstract record NotificationRecipient
{
    public sealed record User(string UserId) : NotificationRecipient;
    public sealed record Users(IReadOnlyList<string> UserIds) : NotificationRecipient;          // ≤ 100
    public sealed record Audience(bool AllActiveUsers, IReadOnlyList<string> RoleIds) : NotificationRecipient; // announcements only
    public sealed record AddressForUser(string UserId, string EmailAddress) : NotificationRecipient; // unverified/new address (FR-009c)
    public sealed record AddressLookup(string EmailAddress) : NotificationRecipient;           // password reset: resolved in background (R12)
    public sealed record SupportMailbox : NotificationRecipient;                               // address from server config only
}
```

**Rules for callers**:
1. Call `Publish` **before** your own `SaveChangesAsync`, in the same unit of work as the state change the notification reports (FR-006).
2. Never pass channels, providers, templates, HTML, URLs, tokens or secrets. The request says *what happened*, not how to deliver it (FR-002, FR-013).
3. Use a stable `EventKey` whenever the same event could be raised twice: `{itemType}:{itemId}:{event}`, for example `workflow-execution:7b1…:failed`, or `workflow-approval:{approvalId}:requested`.
4. Only notify the owner of `RelatedItem`, or the recipient the owning system has already resolved (for example the approver; FR-035, FR-053).
5. Do not wrap the call in try/catch. It can only fail for programming errors, which must surface in tests (principle VIII).

**Emit points in this feature**:

| Module | Location (current code) | Types |
|---|---|---|
| Agents | `Application/Agents/Runtime/AgentExecutionOrchestrator.cs` (start/complete/fail; `RequestApproval` ~L311) | `agent.*` |
| Workflows | `Application/Workflows/Runtime/WorkflowExecutionOrchestrator.cs` (start/complete/fail; `RequestApproval` ~L539/547), `PauseWorkflowExecutionCommandHandler` | `workflow.*` |
| Documents | `Infrastructure/Documents/ProcessingNotifier.cs` (`NotifyAsync` → publisher; stage/progress pushes unchanged), OCR and version sites | `document.*` |
| Indexing / KB | indexing job completion and failure sites; KB update commands | `document.indexing.*`, `knowledge-base.*` |
| Memory | `Infrastructure/Memory/MemoryNotifier.cs` → publisher | `memory.*` |
| Account | `RegisterCommandHandler`, resend-confirmation, `RequestEmailChangeCommandHandler`, `RequestPasswordReset*`, `ChangePasswordCommandHandler`, `RequestAccountSupportCommandHandler` | `account.*`, `security.password-changed` |
| Security | 2FA enable/disable/recovery-code handlers | `security.two-factor.*`, `security.recovery-codes.regenerated` |
| Admin | `PublishSystemAnnouncementCommandHandler` | `system.announcement.published` |

`IProcessingNotifier.NotifyAsync` and `IMemoryNotifier` keep their signatures, so their callers don't change. Their implementations become thin adapters over `INotificationPublisher`, and the legacy repositories are removed.

**Committing the event**: in Infrastructure jobs that currently save through their own `IUnitOfWork`, the publisher call joins that save. Where a notifier is invoked *after* the job's final save today, the plan moves the call before that save, so the event commits with the state change.

---

## Hub-internal abstractions (Application → implemented in Infrastructure)

| Interface | Responsibility | Implementation |
|---|---|---|
| `INotificationOutboxStore` | claim, complete or release outbox events (R4) | Persistence `NotificationOutboxRepository` |
| `INotificationRepository` | aggregate persistence; center queries; delivery queue claim, sweep and retention batches | Persistence |
| `INotificationTemplateRepository` | templates and versions; published-version lookup by `(type, channel, language)` with an `en` fallback | Persistence |
| `INotificationPreferenceRepository` | sparse overrides | Persistence |
| `IEffectiveLanguageResolver` | FR-044 resolution (R14) | Infrastructure, cached |
| `INotificationTemplateRenderer` | logic-free render → `RenderedInApp` / `RenderedEmail` (R9) | Infrastructure |
| `INotificationLinkBuilder` | absolute and relative routes from the type route template (R11) | Infrastructure |
| `IAccountLinkIssuer` | mint confirmation, change-email and reset links **at send time** (R10) | Infrastructure/Identity |
| `INotificationRealtimePublisher` | push `notificationCreated`, `notificationUpdated` and `unreadCountChanged` | Infrastructure/Notifications (SignalR) |
| `INotificationChannelSender` | `Channel` plus `SendAsync(DeliveryContext, CancellationToken) → ChannelSendResult` | `EmailChannelSender`. In-app has no sender, because it is delivered at materialization. |
| `IEmailSender` (existing, **extended**) | `SendAsync(EmailMessage, CancellationToken)` | `SmtpEmailSender` (MailKit, STARTTLS) |
| `INotificationAccessCheck` | per related-item-type authorization re-check (R24) | owning modules |
| `INotificationWakeSignal` | pulse the dispatcher and worker after commit (R3) | Infrastructure singleton |

```csharp
public sealed record EmailMessage(
    string To,
    string Subject,                 // header-validated: no CR/LF/control chars (FR-051)
    string HtmlBody,
    string TextBody,
    string MessageId,               // "<{deliveryId}@{domain}>" (R5)
    string? ReplyTo = null,         // validated address; From/Return-Path come only from SmtpOptions
    IReadOnlyList<EmailAttachment>? Attachments = null);

public sealed record EmailAttachment(string FileName, string ContentType, Func<CancellationToken, Task<Stream>> OpenRead);

public sealed record ChannelSendResult(ChannelSendOutcome Outcome, DeliveryFailureKind? FailureKind, string? SafeReason, string? ProviderResponse);
public enum ChannelSendOutcome { Sent, Delivered, TransientFailure, PermanentFailure }
```

**Adding a channel** (FR-060, SC-012):
1. Add a `NotificationChannel` enum value.
2. Implement `INotificationChannelSender`.
3. Declare the channel on the relevant catalogue types.
4. Add templates for it.
5. Register it in DI.

`INotificationPublisher`, `NotificationRouter` and the emitters don't change. A stub `TestChannelSender` in the test suite proves this (SC-012).

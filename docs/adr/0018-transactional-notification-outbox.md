# ADR 0018: Transactional Notification Outbox with In-Process Workers

**Status:** Accepted

**Date:** 2026-09-27

**Feature:** [specs/067-notifications-communication-hub](../../specs/067-notifications-communication-hub/spec.md)

## Context

Before this feature, every module that told a user something did it its own way. Document
processing wrote `DocumentNotifications` rows, memory wrote `MemoryNotifications` rows, and
account emails were Hangfire jobs enqueued after the handler's own save.

That last pattern has two gaps. Hangfire's SQL storage is not enlisted in the EF Core
transaction, so a crash between `SaveChangesAsync` and `Enqueue` loses the email. And an enqueue
placed before a save that then rolls back notifies about a change that never happened.
Constitution §5 asks for "domain events or an outbox, not multiple partial commits", and §3 asks
for reactions to be dispatched after commit.

The hub also has latency targets Hangfire recurring jobs can't meet: 5 s p95 from event to the
in-app push (SC-001), and 1 minute for account emails (SC-014). Recurring jobs have 1-minute
granularity.

## Decision

### 1. Modules publish into the caller's unit of work

`INotificationPublisher.Publish(NotificationRequest)` validates the request and adds a
`NotificationOutboxEvent` to the scoped `DbContext`. It performs no I/O and never saves. The row
commits atomically with the caller's own state change on the caller's `SaveChangesAsync`. Callers
publish *before* that save, and never wrap the call in try/catch: it throws only for programming
errors (an unknown type, an undeclared variable), which must surface in tests.

### 2. Two `BackgroundService` workers, woken by a signal

- `NotificationOutboxDispatcher` claims pending events, resolves recipients, routes channels
  through the pure `NotificationRouter`, renders the in-app copy and materializes `Notification`
  and `NotificationDelivery` rows in one save. Only after that commit does it push over SignalR.
- `NotificationDeliveryWorker` claims due non-in-app deliveries (email), renders, sends and
  records the outcome.

Both poll every second when busy and every 5 s when idle. A `SaveChanges` interceptor pulses a
process-local wake signal when a save adds an outbox event or a delivery, so on the same instance
latency is milliseconds; across instances it is at most one poll interval.

Each iteration runs in its own DI scope, so a worker never shares a request `DbContext`. Because
.NET 10 can skip `ExecuteAsync` on a fast start-stop, both workers release their claimed work in
`StopAsync`.

### 3. Lease-based claims

A claim is a conditional `ExecuteUpdateAsync`: set the lease owner, lease expiry and in-progress
status *where* the row is still pending and unleased (or its lease has expired). One affected row
means the claim won. Two workers can never both win, so the design is safe to scale out without
a distributed lock. The lease is 2 minutes, above the 60 s SMTP timeout.

The delivery worker claims **one delivery at a time**, just before it sends it, rather than leasing a
whole batch up front. A batch lease starts every clock at once, so a delivery waiting behind slow sends
could outlive its lease without ever being sent, and the sweeper would then record it as ambiguous when
nothing had left the process. Found by the 1,000-delivery fault-injection run (T108). The outbox
dispatcher keeps batch claims: its events are processed in milliseconds and a lost lease only means a retry.

### 4. At-most-once email

The claim commits `Sending` and increments the attempt count before the SMTP call. A delivery
still `Sending` when its lease expires crashed mid-send, so its outcome is unknown. The lease
sweeper marks it `Failed` with `AmbiguousOutcome` and does **not** resend it; an administrator can
retry it deliberately. The channel sender reports `SendProgress.MarkTransmissionStarted()` immediately
before the message can leave the process, so a shutdown that arrives while the delivery is still being
rendered gives the claim back unspent (the attempt is returned too); only a shutdown after that point leaves
the delivery for the sweeper. Every email carries `Message-ID: <{deliveryId}@{domain}>`, so a receiving
system can collapse a duplicate after such a retry. The in-app channel has no ambiguity: its
delivery is created `Delivered` in the same commit as the notification.

### 5. Hangfire stays for recurring maintenance

Retention cleanup and the lease sweeper remain Hangfire recurring jobs, where 1-minute
granularity is fine.

## Consequences

- A committed state change and its notification can no longer diverge.
- There is one more pattern in the codebase: a polling `BackgroundService` over an outbox table,
  alongside Hangfire. New cross-module reactions that need durability should use the same outbox
  rather than a post-save enqueue.
- The wake signal is process-local. With several instances, another instance picks up an event
  within one poll interval, not immediately. That is acceptable for the current single-instance
  host and the stated targets.
- Duplicate events are removed at materialization by a unique `(RecipientUserId, EventKey)` index,
  never at publish, so a duplicate can never roll back the emitter's own transaction.

## Alternatives considered

- **Keep enqueueing Hangfire jobs after the save.** Not atomic with the state change.
- **Hangfire enqueue inside a shared `TransactionScope`.** Needs Hangfire storage on the same
  connection and ambient-transaction support, and couples every emitter to Hangfire.
- **In-memory domain events dispatched after `SaveChanges`.** The platform has no dispatcher, and
  a crash after commit but before dispatch still loses the event.
- **A message broker.** New infrastructure that 10,000 notifications an hour doesn't justify.
- **`UPDATE TOP(n) … WITH (READPAST, UPDLOCK) OUTPUT` claims.** Faster at very high volume, but
  SQL Server-specific raw SQL the current volume doesn't need.

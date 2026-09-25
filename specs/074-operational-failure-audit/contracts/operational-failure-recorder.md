# Contract: Operational Failure Recorder (in-process)

This is the Application-layer seam that every engine calls. It lives in
`AskLucy.Application/OperationalFailures/Abstractions/`.

```csharp
public interface IOperationalFailureRecorder
{
    /// Enqueues a failure for the admin trail. Synchronous, non-blocking, never throws.
    void Record(OperationalFailureReport report);

    /// Voice only: marks a recovery on the open incident with the same key; ignored when none is open.
    void RecordRecovery(VoiceRecoveryReport report);
}
```

The fields are listed in [data-model.md](../data-model.md#application-level-types-not-persisted).

## Guarantees the implementation gives callers

| # | Guarantee | Verified by |
|---|---|---|
| G1 | Returns in O(µs) and never awaits I/O | Infrastructure test: a store that blocks forever still lets `Record` return immediately |
| G2 | Never throws, even when the report is null or malformed, the channel is full, or the logger throws | Infrastructure test with a throwing sink; the caller's exception is unchanged |
| G3 | Captures the correlation id and UTC time on the caller's thread | Test: `Record` inside a request, and the stored id equals the response's `traceId` |
| G4 | A full channel or failed persistence is logged with correlation id, engine and kind, and is never re-recorded | `FakeLogger` assertion; the store receives no report about the recorder itself |
| G5 | The reason is sanitised and truncated by the ingestor, whatever the caller passed | Sanitizer corpus test plus the SC-006 scan |
| G6 | Severity is derived from engine, kind and outcome; callers cannot set it | Compile-time: the report has no severity field |

## Rules for callers

1. **Log first, then record.** The existing log line stays the system of record. `Record` is an
   addition (research D2).
2. **Pass system prose as `Reason`.** Never pass `ex.Message` from a vendor, or a response body,
   prompt, reply, file name, password, 2FA code or token. When you have only an exception, pass
   `Exception = ex` and a short, fixed `Reason` such as "Text-to-speech request failed". The
   classifier supplies the kind from the exception type.
3. **Pass `Outcome`, not severity.** Use `DegradedServed` when a fallback served the user, and
   `RecoveredByRetry` only for sites that record a retry that succeeded. The default and the rule is
   not to record those at all (spec Assumptions).
4. **Mark what you recorded if it propagates.** Call `ex.MarkOperationalFailureRecorded()` before
   rethrowing. The middleware and the Hangfire filter skip marked exceptions, which keeps it to
   exactly one occurrence per failure (research D11).
5. **Do not record** (FR-006a):
   - user input validation errors;
   - genuine not-found;
   - the platform's own rate-limit refusals;
   - access-token expiry or refresh;
   - `OperationCanceledException` caused by the caller's or request's token.
6. **Subject vs occurrence references.** Set `Subject` only to a workflow *definition*, an agent, a
   document or an MCP server. Chats, users, runs and job instances go on the occurrence fields
   (FR-018).

## Call sites

This is the planned list. `/speckit-tasks` turns each into a task, with a test that forces the
failure.

| Engine | Site | Operation | Subject | Notes |
|---|---|---|---|---|
| Chat | `AiController` mid-stream `catch` (L342–400) | "Chat reply" | — | Classifies through `IFailureClassifier`, which fixes FR-004. The user and model text comes from `UserFacingFailureText`. |
| Chat / AiProvider | `ProblemDetailsMiddleware` (boundary) | from the endpoint: "Chat reply", "Generate title", … | — | Unmarked system-side exceptions only |
| AiProvider | Scheduled provider health check (`ProviderHealthCheckHostedService`) | "Health check" | — | No user (US4 scenario 4) |
| Voice | `TextToSpeechStreamer` (L44 failover, L67), `CreateSpeechToTextSessionCommandHandler` (L48) | "Text-to-speech" / "Transcription" | — | `IsFailover`. A recovery calls `RecordRecovery`. `VoiceProviderHealthRecorder` is unchanged. |
| Embeddings / DocumentProcessing | `DocumentProcessingPipeline` (`stage.Fail`/`job.Fail`, L167–168) | "Index document" / "Extract text" / … (stage name) | Document | Also sets `KnowledgeBaseId` |
| ImageGeneration | `GenerateImageCommandHandler` | "Generate image" | — | |
| Agent | Where `AgentExecutionStep` / `AgentExecution` become Failed (`AgentExecution.cs` L198) | tool name | Agent | `AgentExecutionId` |
| Workflow | Where `WorkflowExecutionNode` becomes Failed (L72) | step type | Workflow | `WorkflowExecutionId`, `WorkflowExecutionNodeId`. The run-level Failed is not recorded again. |
| Mcp | Tool-call failure path; `RefreshMcpCapabilitiesCommandHandler`; `TestMcpServerConnectionCommandHandler` | "{server}: {tool}" / "Refresh capabilities" / "Test connection" | McpServer | Kind from `McpFailureCategory`. The correlation id matches `McpAuditLog`. |
| BackgroundJob | `OperationalFailureJobFilter.OnStateElection` (final `FailedState` only) | "Job: {TypeName}.{Method}" | — | `JobId`. Skipped when the exception is marked. |
| Access | `PermissionDeniedAuditResultHandler` | required permission key | — | 403 |
| Access | `ProblemDetailsMiddleware`, `OwnershipDeniedException` arm | "Open {item type}" | — | Response stays 404 (research D12) |
| Access | `LoginCommandHandler`, the 2FA handler, the external-login callback handler | "Sign-in" / "Two-factor" / "External sign-in: {provider}" | — | `SourceIp`, and the matched account only (FR-012a) |

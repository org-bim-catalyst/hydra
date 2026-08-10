using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Common;
using AskLucy.Application.Prompts;
using AskLucy.Application.Prompts.Commands.AddTag;
using AskLucy.Application.Prompts.Commands.ArchivePrompt;
using AskLucy.Application.Prompts.Commands.CreateCustomCategory;
using AskLucy.Application.Prompts.Commands.CreatePrompt;
using AskLucy.Application.Prompts.Commands.DeletePrompt;
using AskLucy.Application.Prompts.Commands.DeleteTestCase;
using AskLucy.Application.Prompts.Commands.DuplicatePrompt;
using AskLucy.Application.Prompts.Commands.DuplicateVersion;
using AskLucy.Application.Prompts.Commands.ExecutePrompt;
using AskLucy.Application.Prompts.Commands.ExportPrompts;
using AskLucy.Application.Prompts.Commands.ImportPrompts;
using AskLucy.Application.Prompts.Commands.RateExecution;
using AskLucy.Application.Prompts.Commands.RecordPromptExecution;
using AskLucy.Application.Prompts.Commands.RemoveTag;
using AskLucy.Application.Prompts.Commands.RestorePrompt;
using AskLucy.Application.Prompts.Commands.RestoreVersion;
using AskLucy.Application.Prompts.Commands.SaveTestCase;
using AskLucy.Application.Prompts.Commands.SetFavorite;
using AskLucy.Application.Prompts.Commands.SetPinned;
using AskLucy.Application.Prompts.Commands.UpdatePrompt;
using AskLucy.Application.Prompts.Queries.CompareExecutions;
using AskLucy.Application.Prompts.Queries.CompareVersions;
using AskLucy.Application.Prompts.Queries.GetExecution;
using AskLucy.Application.Prompts.Queries.GetPrompt;
using AskLucy.Application.Prompts.Queries.GetPromptStatistics;
using AskLucy.Application.Prompts.Queries.GetVersion;
using AskLucy.Application.Prompts.Queries.ListCategories;
using AskLucy.Application.Prompts.Queries.ListExecutions;
using AskLucy.Application.Prompts.Queries.ListPrompts;
using AskLucy.Application.Prompts.Queries.ListTags;
using AskLucy.Application.Prompts.Queries.ListTestCases;
using AskLucy.Application.Prompts.Queries.ListVersions;
using AskLucy.Application.Prompts.Queries.PreviewPrompt;
using AskLucy.Domain.Prompts;
using AskLucy.Web.Contracts;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace AskLucy.Web.Controllers.v1;

/// <summary>Every operation is implicitly scoped to the caller (FR-090, contracts/prompts-api.md).</summary>
[ApiController]
[Authorize]
[EnableRateLimiting("prompt-endpoints")]
[Route("api/v1/prompts")]
public sealed class PromptsController(
    ISender mediator, IAIProviderRepository providerRepository, IAIModelRepository modelRepository,
    IOptions<JsonOptions> jsonOptions) : ControllerBase
{
    /// <summary>Search/filter/sort/paginate the caller's own prompts (FR-050–FR-053, contracts/prompts-api.md).</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<PromptListItemDto>>> List(
        [FromQuery] PromptListView view = PromptListView.All,
        [FromQuery] string? q = null,
        [FromQuery] Guid? categoryId = null,
        [FromQuery] string? tag = null,
        [FromQuery] Guid? folderId = null,
        [FromQuery] PromptStatus? status = null,
        [FromQuery] string? cursor = null,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default) =>
        Ok(await mediator.Send(new ListPromptsQuery(view, q, categoryId, tag, folderId, status, cursor, pageSize), cancellationToken));

    // --- Favorites / pinned (contracts/prompts-api.md) ---

    [HttpPut("{id:guid}/favorite")]
    public async Task<IActionResult> SetFavorite(Guid id, [FromBody] SetFavoriteRequest request, CancellationToken cancellationToken)
    {
        await mediator.Send(new SetFavoriteCommand(id, request.IsFavorite), cancellationToken);
        return NoContent();
    }

    [HttpPut("{id:guid}/pinned")]
    public async Task<IActionResult> SetPinned(Guid id, [FromBody] SetPinnedRequest request, CancellationToken cancellationToken)
    {
        await mediator.Send(new SetPinnedCommand(id, request.IsPinned), cancellationToken);
        return NoContent();
    }

    // --- Tags (contracts/prompts-api.md) ---

    [HttpPost("{id:guid}/tags")]
    public async Task<IActionResult> AddTag(Guid id, AddPromptTagRequest request, CancellationToken cancellationToken)
    {
        var tagId = await mediator.Send(new AddTagCommand(id, request.Value), cancellationToken);
        return CreatedAtAction(nameof(Get), new { id }, new { id = tagId, value = request.Value });
    }

    [HttpDelete("{id:guid}/tags/{tagId:guid}")]
    public async Task<IActionResult> RemoveTag(Guid id, Guid tagId, CancellationToken cancellationToken)
    {
        await mediator.Send(new RemoveTagCommand(id, tagId), cancellationToken);
        return NoContent();
    }

    [HttpGet("tags")]
    public async Task<ActionResult<IReadOnlyList<string>>> ListTags(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new ListTagsQuery(), cancellationToken));

    // --- Categories (contracts/prompts-api.md) ---

    [HttpGet("categories")]
    public async Task<ActionResult<IReadOnlyList<PromptCategoryDto>>> ListCategories(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new ListCategoriesQuery(), cancellationToken));

    [HttpPost("categories")]
    public async Task<ActionResult<PromptCategoryDto>> CreateCategory(CreatePromptCategoryRequest request, CancellationToken cancellationToken)
    {
        var category = await mediator.Send(new CreateCustomCategoryCommand(request.Name), cancellationToken);
        return CreatedAtAction(nameof(ListCategories), category);
    }

    [HttpPost]
    public async Task<ActionResult<PromptDetailDto>> Create(CreatePromptRequest request, CancellationToken cancellationToken)
    {
        var prompt = await mediator.Send(
            new CreatePromptCommand(
                request.Name, request.Description, request.PromptType,
                request.SystemInstructions, request.DeveloperInstructions, request.UserInstructions,
                request.ContextText, request.ExamplesText, request.OutputInstructions, request.Constraints,
                request.CategoryId, request.FolderId,
                request.RequiredCapabilities ?? PromptCapabilityRequirements.None, request.PreferredModelKey,
                request.Variables ?? []),
            cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = prompt.Id }, prompt);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PromptDetailDto>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetPromptQuery(id), cancellationToken));

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<PromptDetailDto>> Update(Guid id, UpdatePromptRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(
            new UpdatePromptCommand(
                id, request.Name, request.Description, request.PromptType,
                request.SystemInstructions, request.DeveloperInstructions, request.UserInstructions,
                request.ContextText, request.ExamplesText, request.OutputInstructions, request.Constraints,
                request.CategoryId, request.FolderId,
                request.RequiredCapabilities ?? PromptCapabilityRequirements.None, request.PreferredModelKey,
                request.Variables ?? [], request.ChangeDescription),
            cancellationToken));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new DeletePromptCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/actions/archive")]
    public async Task<IActionResult> Archive(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new ArchivePromptCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/actions/restore")]
    public async Task<IActionResult> Restore(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new RestorePromptCommand(id), cancellationToken);
        return NoContent();
    }

    /// <summary>Deep copy — new prompt, fresh version-1 history, auto-suffixed name on collision (FR-001, FR-006, spec.md Edge Cases).</summary>
    [HttpPost("{id:guid}/actions/duplicate")]
    public async Task<ActionResult<PromptDetailDto>> Duplicate(Guid id, CancellationToken cancellationToken)
    {
        var duplicate = await mediator.Send(new DuplicatePromptCommand(id), cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = duplicate.Id }, duplicate);
    }

    /// <summary>Resolves content with supplied/example/default variable values — no AI provider call (FR-005).</summary>
    [HttpPost("{id:guid}/preview")]
    public async Task<ActionResult<PromptPreviewDto>> Preview(Guid id, PreviewPromptRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new PreviewPromptQuery(id, request.VariableValues ?? new Dictionary<string, string?>()), cancellationToken));

    // --- Versions (contracts/prompts-api.md) ---

    [HttpGet("{id:guid}/versions")]
    public async Task<ActionResult<IReadOnlyList<PromptVersionSummaryDto>>> ListVersions(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new ListVersionsQuery(id), cancellationToken));

    [HttpGet("{id:guid}/versions/{versionNumber:int}")]
    public async Task<ActionResult<PromptVersionDetailDto>> GetVersion(Guid id, int versionNumber, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetVersionQuery(id, versionNumber), cancellationToken));

    [HttpGet("{id:guid}/versions/compare")]
    public async Task<ActionResult<PromptVersionComparisonDto>> CompareVersions(
        Guid id, [FromQuery] int from, [FromQuery] int to, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new CompareVersionsQuery(id, from, to), cancellationToken));

    /// <summary>Creates a new version copying the restored content — history is never deleted or overwritten (FR-033).</summary>
    [HttpPost("{id:guid}/versions/{versionNumber:int}/actions/restore")]
    public async Task<ActionResult<PromptDetailDto>> RestoreVersion(Guid id, int versionNumber, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new RestoreVersionCommand(id, versionNumber), cancellationToken));

    [HttpPost("{id:guid}/versions/{versionNumber:int}/actions/duplicate")]
    public async Task<ActionResult<PromptDetailDto>> DuplicateVersion(Guid id, int versionNumber, CancellationToken cancellationToken)
    {
        var duplicate = await mediator.Send(new DuplicateVersionCommand(id, versionNumber), cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = duplicate.Id }, duplicate);
    }

    // --- Execution (contracts/prompt-execution-api.md) ---

    /// <summary>
    /// Streams a test execution (FR-040-FR-046). Uses the cost-tiered `ai-endpoints` policy
    /// (overriding the controller-level `prompt-endpoints`) since this is the one action that
    /// invokes <see cref="IAIProvider"/> directly — mirrors <c>AiController.Chat</c>/<c>VoiceReply</c>'s
    /// SSE + provider-failure-surfaces-as-an-explicit-error-event shape exactly (constitution §2.VIII).
    /// </summary>
    [HttpPost("{id:guid}/executions")]
    [EnableRateLimiting("ai-endpoints")]
    public async Task Execute(Guid id, ExecutePromptRequest request, CancellationToken cancellationToken)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";

        var startedAt = DateTime.UtcNow;
        var outputText = new System.Text.StringBuilder();
        ChatUsage? finalUsage = null;
        Guid? promptVersionId = null;
        string? resolvedVariableValuesJson = null;
        RagRetrievalOutcome? retrievalOutcome = null;
        MemoryRetrievalOutcome? memoryOutcome = null;
        Exception? failure = null;

        try
        {
            await foreach (var chunk in mediator.CreateStream(
                new ExecutePromptCommand(
                    id, request.VersionNumber, request.VariableValues ?? new Dictionary<string, string?>(),
                    request.ProviderId, request.ModelId, request.GenerationParameters,
                    request.UseRagContext, request.KnowledgeBaseIds, request.UseMemoryContext),
                cancellationToken))
            {
                if (!string.IsNullOrEmpty(chunk.ContentDelta))
                {
                    outputText.Append(chunk.ContentDelta);
                    await Response.WriteAsync($"data: {JsonSerializer.Serialize(new { type = "content", content = chunk.ContentDelta })}\n\n", cancellationToken);
                    await Response.Body.FlushAsync(cancellationToken);
                }

                if (chunk.Usage is not null)
                {
                    finalUsage = chunk.Usage;
                }

                if (chunk.PromptVersionId is not null)
                {
                    promptVersionId = chunk.PromptVersionId;
                    resolvedVariableValuesJson = chunk.ResolvedVariableValuesJson;
                    retrievalOutcome = chunk.RetrievalOutcome;
                    memoryOutcome = chunk.MemoryOutcome;
                }
            }
        }
        catch (Exception ex) when (ex is AiProviderUnavailableException or AiProviderRateLimitedException or AiProviderAuthenticationException)
        {
            failure = ex;
            var payload = new { type = "error", errorType = ex.GetType().Name, title = "The prompt execution failed.", detail = ex.Message };
            await Response.WriteAsync($"data: {JsonSerializer.Serialize(payload)}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }

        var latencyMs = (int)(DateTime.UtcNow - startedAt).TotalMilliseconds;
        var provider = await providerRepository.GetByIdAsync(request.ProviderId, cancellationToken);
        var model = await modelRepository.GetByIdAsync(request.ModelId, cancellationToken);

        // FR-081/FR-082: only a Grounded/Found outcome carries citations/memory-references worth
        // persisting for the workspace's own observability — mirrors AiController.Chat's identical
        // "only attach citations when actually grounded" rule.
        var ragCitationsJson = retrievalOutcome?.Type == RagRetrievalOutcomeType.Grounded
            ? JsonSerializer.Serialize(retrievalOutcome.Citations)
            : null;
        var memoryReferencesJson = memoryOutcome?.Type == MemoryRetrievalOutcomeType.Found
            ? JsonSerializer.Serialize(memoryOutcome.UsedMemories)
            : null;

        var executionId = await mediator.Send(
            new RecordPromptExecutionCommand(
                id,
                promptVersionId ?? Guid.Empty,
                PromptExecutionOrigin.TestingWorkspace,
                request.ModelId,
                provider?.ProviderKey ?? string.Empty,
                model?.ModelKey ?? string.Empty,
                (decimal?)request.GenerationParameters?.Temperature,
                request.GenerationParameters?.MaxTokens,
                request.GenerationParameters?.JsonMode ?? false,
                resolvedVariableValuesJson ?? "{}",
                request.UseRagContext,
                request.UseMemoryContext,
                failure is null ? PromptExecutionOutcome.Success : PromptExecutionOutcome.Failed,
                failure?.Message,
                latencyMs,
                outputText.ToString(),
                finalUsage?.InputTokenCount,
                finalUsage?.OutputTokenCount,
                RagCitationsJson: ragCitationsJson,
                MemoryReferencesJson: memoryReferencesJson),
            cancellationToken);

        await Response.WriteAsync($"data: {JsonSerializer.Serialize(new { type = "done", executionId })}\n\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }

    [HttpGet("{id:guid}/executions")]
    public async Task<ActionResult<PagedResult<PromptExecutionSummaryDto>>> ListExecutions(
        Guid id, [FromQuery] string? cursor, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default) =>
        Ok(await mediator.Send(new ListExecutionsQuery(id, cursor, pageSize), cancellationToken));

    [HttpGet("/api/v1/prompt-executions/{executionId:guid}")]
    public async Task<ActionResult<PromptExecutionDetailDto>> GetExecution(Guid executionId, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetExecutionQuery(executionId), cancellationToken));

    [HttpGet("/api/v1/prompt-executions/compare")]
    public async Task<ActionResult<IReadOnlyList<PromptExecutionDetailDto>>> CompareExecutions(
        [FromQuery] Guid[] executionIds, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new CompareExecutionsQuery(executionIds), cancellationToken));

    [HttpPut("/api/v1/prompt-executions/{executionId:guid}/rating")]
    public async Task<IActionResult> RateExecution(Guid executionId, RateExecutionRequest request, CancellationToken cancellationToken)
    {
        await mediator.Send(new RateExecutionCommand(executionId, request.Value), cancellationToken);
        return NoContent();
    }

    // --- Test cases (contracts/prompt-execution-api.md) ---

    [HttpPost("{id:guid}/test-cases")]
    public async Task<ActionResult<PromptTestCaseDto>> SaveTestCase(Guid id, SaveTestCaseRequest request, CancellationToken cancellationToken)
    {
        var testCase = await mediator.Send(
            new SaveTestCaseCommand(
                id, request.Name, request.VariableValuesJson, request.ExpectedOutput, request.EvaluationCriteria,
                request.ProviderKey, request.ModelKey, request.SourceExecutionId),
            cancellationToken);
        return CreatedAtAction(nameof(ListTestCases), new { id }, testCase);
    }

    [HttpGet("{id:guid}/test-cases")]
    public async Task<ActionResult<IReadOnlyList<PromptTestCaseDto>>> ListTestCases(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new ListTestCasesQuery(id), cancellationToken));

    [HttpDelete("{id:guid}/test-cases/{testCaseId:guid}")]
    public async Task<IActionResult> DeleteTestCase(Guid id, Guid testCaseId, CancellationToken cancellationToken)
    {
        await mediator.Send(new DeleteTestCaseCommand(id, testCaseId), cancellationToken);
        return NoContent();
    }

    /// <summary>spec.md "Prompt Statistics" API requirement, FR-062.</summary>
    [HttpGet("{id:guid}/statistics")]
    public async Task<ActionResult<PromptStatisticsDto>> GetStatistics(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetPromptStatisticsQuery(id), cancellationToken));

    // --- Export / Import (contracts/prompts-api.md) ---

    /// <summary>Exports one or more of the caller's own prompts to a portable, downloadable JSON file (FR-070).</summary>
    [HttpPost("export")]
    public async Task<IActionResult> Export(ExportPromptsRequest request, CancellationToken cancellationToken)
    {
        var file = await mediator.Send(new ExportPromptsCommand(request.PromptIds), cancellationToken);
        var fileName = file.Prompts.Count == 1 ? $"{SanitizeFileName(file.Prompts[0].Name)}.json" : "prompts-export.json";
        // Must use the app's configured (string-enum) JsonSerializerOptions here, not the
        // System.Text.Json default (raw numeric ordinals) — a manual JsonSerializer call, unlike
        // returning a plain object result, does not pick up Program.cs's AddJsonOptions()
        // configuration automatically. Re-import binds the same shape via [FromBody], which DOES
        // use that configuration and therefore expects string enum values — using the default
        // options here would silently break every export/import round-trip.
        return File(JsonSerializer.SerializeToUtf8Bytes(file, jsonOptions.Value.JsonSerializerOptions), "application/json", fileName);
    }

    /// <summary>Imports a previously-exported file — atomic, all-or-nothing validation; a name collision on any entry is auto-suffixed, never a failure (FR-071/FR-072).</summary>
    [HttpPost("import")]
    public async Task<ActionResult<IReadOnlyList<PromptListItemDto>>> Import(PromptExportFile request, CancellationToken cancellationToken)
    {
        var created = await mediator.Send(new ImportPromptsCommand(request), cancellationToken);
        return CreatedAtAction(nameof(List), new { }, created);
    }

    private static string SanitizeFileName(string name)
    {
        var sanitized = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
        return string.IsNullOrWhiteSpace(sanitized) ? "prompt" : sanitized;
    }
}

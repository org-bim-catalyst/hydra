using AskLucy.Application.SiteAnalysis.Queries.GetSiteAnalysis;
using AskLucy.Application.SiteAnalysis.Queries.ListSiteAnalysesByChat;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AskLucy.Web.Controllers.v1;

/// <summary>contracts/site-analysis-api.md — the rehydration path (FR-016-FR-019). Read-only; nothing here starts or mutates an analysis, which happens only through conversation with Lucy.</summary>
[ApiController]
[Authorize]
[EnableRateLimiting("site-analysis-endpoints")]
[Route("api/v1/site-analyses")]
public sealed class SiteAnalysesController(ISender mediator) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SiteAnalysisDetailDto>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetSiteAnalysisQuery(id), cancellationToken));

    /// <summary>tasks.md T047 — rehydration for a whole conversation on open; bounded to that chat's own analyses (constitution §6).</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SiteAnalysisDetailDto>>> ListByChat([FromQuery] Guid userChatId, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new ListSiteAnalysesByChatQuery(userChatId), cancellationToken));
}

using MediatR;

namespace AskLucy.Application.Ai.Queries.GetProviderHealthRetention;

/// <summary>specs/082 contracts "GET": the effective retention window, its source and the version needed to change it.</summary>
public sealed record GetProviderHealthRetentionQuery : IRequest<ProviderHealthRetentionDto>;

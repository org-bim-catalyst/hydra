using AskLucy.Domain.CustomModels;
using MediatR;

namespace AskLucy.Application.CustomModels.Commands.SetCustomModelAvailability;

/// <summary>
/// specs/072 contracts/admin-custom-models.md <c>PUT {id}/availability</c> (FR-030). Only a completed
/// model can be made available, and only one per repository; making one unavailable is always allowed.
/// </summary>
public sealed record SetCustomModelAvailabilityCommand(Guid Id, CustomModelAvailability Availability) : IRequest<CustomModelSummaryDto>;

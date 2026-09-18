using AskLucy.Domain.Ai;
using MediatR;

namespace AskLucy.Application.Ai.Commands.SetAiCapabilityAssignment;

/// <summary>
/// Assigns a provider to a capability, or clears the assignment when <paramref name="ProviderId"/>
/// is null — clearing returns the capability to the platform default rather than disabling it
/// (except <see cref="AiCapability.ImageGeneration"/>, which then reports "not configured").
/// <paramref name="ModelId"/> pins one of that provider's models; null follows the provider's
/// own default, and is required for <see cref="AiCapability.ImageGeneration"/>.
/// </summary>
public sealed record SetAiCapabilityAssignmentCommand(AiCapability Capability, Guid? ProviderId, Guid? ModelId = null) : IRequest;

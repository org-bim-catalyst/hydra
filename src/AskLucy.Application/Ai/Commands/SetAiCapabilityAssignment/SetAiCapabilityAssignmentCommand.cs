using AskLucy.Domain.Ai;
using MediatR;

namespace AskLucy.Application.Ai.Commands.SetAiCapabilityAssignment;

/// <summary>
/// Assigns a provider to a capability, or clears the assignment when <paramref name="ProviderId"/>
/// is null — clearing returns the capability to the platform default rather than disabling it.
/// </summary>
public sealed record SetAiCapabilityAssignmentCommand(AiCapability Capability, Guid? ProviderId) : IRequest;

using AskLucy.Application.Abstractions;
using AskLucy.Domain.Ai;
using AskLucy.Domain.Common;
using MediatR;

namespace AskLucy.Application.Ai.Commands.ValidateModelReplacement;

/// <summary>specs/069 FR-011a: the administrator accepts the replacement that is already in use.</summary>
public sealed record AcceptModelReplacementCommand(Guid DeprecationId) : IRequest;

public sealed class AcceptModelReplacementCommandHandler(
    IModelDeprecationRepository deprecations,
    ICurrentUserAccessor currentUser,
    IUnitOfWork unitOfWork) : IRequestHandler<AcceptModelReplacementCommand>
{
    public async Task Handle(AcceptModelReplacementCommand request, CancellationToken cancellationToken)
    {
        var actor = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var deprecation = await deprecations.GetByIdAsync(request.DeprecationId, cancellationToken)
            ?? throw new KeyNotFoundException("Deprecation not found.");

        try
        {
            deprecation.Accept(actor, DateTime.UtcNow);
        }
        catch (DomainRuleViolationException ex)
        {
            throw new ModelReplacementConflictException(ex.Message);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>The replacement is no longer awaiting validation. Mapped to 409.</summary>
public sealed class ModelReplacementConflictException(string message) : Exception(message);

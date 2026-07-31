using FluentValidation;

namespace AskLucy.Application.Ai.Commands.ApplyProviderModelSync;

/// <summary>
/// specs/009-selective-model-sync-review FR-013: a request selecting nothing on either
/// side is rejected as a server-side backstop to FR-008's client-side Confirm-disabled
/// guard. A stale row (added.ModelKey already existing, or removedFromVendor.Id not
/// belonging to the provider) is no longer validated here — FR-007a moved that check into
/// <see cref="ApplyProviderModelSyncCommandHandler"/> so one stale row never rejects the
/// rest of an otherwise-valid selection.
/// </summary>
public sealed class ApplyProviderModelSyncCommandValidator : AbstractValidator<ApplyProviderModelSyncCommand>
{
    public ApplyProviderModelSyncCommandValidator()
    {
        RuleFor(c => c.ProviderId).NotEmpty();

        RuleFor(c => c)
            .Must(c => c.Added.Count > 0 || c.RemovedFromVendor.Count > 0)
            .WithMessage("Nothing to apply.");
    }
}

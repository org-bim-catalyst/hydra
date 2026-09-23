using AskLucy.Application.Options;
using AskLucy.Domain.CustomModels;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace AskLucy.Application.CustomModels.Commands.SubmitCustomModelDeployment;

/// <summary>
/// specs/072 contracts "Validation". The Domain value objects are the rules; this only runs them
/// and keys each error by the contract's field name so the dialog can show it under that field.
/// </summary>
public sealed class SubmitCustomModelDeploymentCommandValidator : AbstractValidator<SubmitCustomModelDeploymentCommand>
{
    public const int MaxSourceLength = CustomModel.MaxSourceUrlLength;

    public SubmitCustomModelDeploymentCommandValidator(IOptionsMonitor<CustomModelsOptions> options)
    {
        // One Custom rule per field (rather than a NotEmpty/MaximumLength chain) so each field
        // reports exactly one error, keyed by the contract's field name.
        RuleFor(c => c.Source).Custom((source, context) =>
        {
            var error = string.IsNullOrWhiteSpace(source) ? "A source is required."
                : source.Length > MaxSourceLength ? $"A source must be at most {MaxSourceLength} characters."
                : HuggingFaceModelSource.TryParse(source, out _, out var parseError) ? null
                : parseError;
            if (error is not null)
            {
                context.AddFailure("source", error);
            }
        });

        RuleFor(c => c.Destination).Custom((destination, context) =>
        {
            var error = string.IsNullOrWhiteSpace(destination) ? "A destination is required."
                : destination.Length > DeploymentDestination.MaxLength ? $"A destination must be at most {DeploymentDestination.MaxLength} characters."
                : DeploymentDestination.TryCreate(destination, options.CurrentValue.GetAllowedDestinationPrefixes(), out _, out var createError) ? null
                : createError;
            if (error is not null)
            {
                context.AddFailure("destination", error);
            }
        });

        RuleFor(c => c.Name)
            .MaximumLength(CustomModel.MaxNameLength)
            .Matches(@"^[\p{L}\p{N}][\p{L}\p{N} ._-]{0,99}$")
            .WithMessage("A name must start with a letter or digit and contain only letters, digits, spaces, '.', '_' or '-'.")
            .When(c => !string.IsNullOrWhiteSpace(c.Name))
            .OverridePropertyName("name");
    }
}

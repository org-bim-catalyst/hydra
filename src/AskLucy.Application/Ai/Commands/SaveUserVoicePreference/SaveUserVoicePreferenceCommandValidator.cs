using AskLucy.Domain.Ai;
using FluentValidation;

namespace AskLucy.Application.Ai.Commands.SaveUserVoicePreference;

/// <summary>data-model.md: <c>VoiceSpeed</c>/<c>VoiceStyle</c>, when present, must fall within
/// ElevenLabs' allowed range — rejected with a specific message, never silently clamped
/// (constitution §2.VIII). Bounds below are ElevenLabs' documented typical ranges; confirm
/// against the current API reference before production (research.md residual verification
/// risk).</summary>
public sealed class SaveUserVoicePreferenceCommandValidator : AbstractValidator<SaveUserVoicePreferenceCommand>
{
    public SaveUserVoicePreferenceCommandValidator()
    {
        RuleFor(c => c.ConversationMode)
            .Must(mode => Enum.TryParse<VoiceConversationMode>(mode, out _))
            .WithMessage("conversationMode must be one of: PushToTalk, Continuous.");

        RuleFor(c => c.VoiceSpeed)
            .InclusiveBetween(0.5, 2.0)
            .When(c => c.VoiceSpeed.HasValue)
            .WithMessage("voiceSpeed must be between 0.5 and 2.0.");

        RuleFor(c => c.VoiceStyle)
            .InclusiveBetween(0.0, 1.0)
            .When(c => c.VoiceStyle.HasValue)
            .WithMessage("voiceStyle must be between 0.0 and 1.0.");
    }
}

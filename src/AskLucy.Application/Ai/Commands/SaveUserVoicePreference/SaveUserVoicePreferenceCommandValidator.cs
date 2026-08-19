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
    /// <summary>specs/026-floating-chat-assistant data-model.md — the product's currently
    /// supported response-language codes (mirrors the frontend's `languageOptions.ts`).</summary>
    private static readonly HashSet<string> SupportedLanguages = ["en", "ar", "es", "fr", "de"];

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

        // FR-017/data-model.md: rejected outright, never silently coerced to a supported
        // value or ignored (constitution §2.VIII).
        RuleFor(c => c.DefaultLanguage)
            .Must(language => SupportedLanguages.Contains(language!))
            .When(c => c.DefaultLanguage is not null)
            .WithMessage("defaultLanguage must be one of: en, ar, es, fr, de.");
    }
}

using MediatR;

namespace AskLucy.Application.Ai.Commands.Translate;

/// <summary>Preserves FR-003's legacy behavior: AI-assisted translation with direction-aware HTML output.</summary>
public sealed record TranslateCommand(string Text, string TargetLanguage) : IRequest<string>;

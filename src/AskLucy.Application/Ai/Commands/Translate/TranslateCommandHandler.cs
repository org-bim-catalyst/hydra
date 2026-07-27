using System.Text.RegularExpressions;
using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Ai.Commands.Translate;

public sealed partial class TranslateCommandHandler(IAIProvider aiProvider) : IRequestHandler<TranslateCommand, string>
{
    public async Task<string> Handle(TranslateCommand request, CancellationToken cancellationToken)
    {
        List<ChatMessage> messages =
        [
            new(ChatRole.System,
                "You are a translation assistant. Translate the user's text into " +
                $"{request.TargetLanguage}. Respond with a single HTML fragment wrapped in a " +
                "```html code block, using <span lang=\"...\" dir=\"ltr|rtl\"> per phrase as appropriate."),
            new(ChatRole.User, request.Text),
        ];

        var response = await aiProvider.ChatAsync(messages, cancellationToken);

        var match = HtmlBlockPattern().Match(response);
        return match.Success ? match.Groups[1].Value.Trim() : response.Trim();
    }

    [GeneratedRegex(@"```html\s*(.*?)\s*```", RegexOptions.Singleline)]
    private static partial Regex HtmlBlockPattern();
}

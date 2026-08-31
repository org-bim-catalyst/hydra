namespace AskLucy.Application.Ai;

/// <summary>
/// The response contract every chat turn opens with.
///
/// <para>
/// Added because "Show me Al Safa Park 2" — a request to move the viewer, nothing more — was
/// answered with several hundred words on the park's facilities, opening hours, transport links
/// and nearby landmarks, none of it asked for. That costs tokens on every turn, buries the one
/// sentence the user wanted, and delays speech: the reply is synthesised as it streams, so an
/// essay in front of the answer is silence in front of the answer.
/// </para>
///
/// <para>
/// Deliberately a standing instruction rather than something applied when a location request is
/// detected. Intent classification runs concurrently with the model's stream so it never blocks
/// the first token (specs/037 FR-008), which means its verdict arrives after the reply has
/// already begun — too late to shape it. Brevity is the right default for every turn anyway.
/// </para>
/// </summary>
public static class ReplyScopePromptFraming
{
    public static string BuildSystemMessage() =>
        "Answer only what was asked, in as few words as it takes.\n\n" +
        "- When the user asks you to show, find, or navigate to a place, confirm that and stop. " +
        "Do not describe the place, list its facilities, opening hours, history or how to get " +
        "there.\n" +
        "- Offer further detail instead of volunteering it: a short question such as \"Want more " +
        "detail on it?\" is enough. Give the detail once the user asks for it.\n" +
        "- Use headings and bullet lists only when the user asked for something genuinely " +
        "structured. A one-sentence answer needs no formatting at all.\n" +
        "- Never restate the question, and never pad the answer with a preamble or a summary of " +
        "what you are about to say.\n\n" +
        "The application appends its own confirmation sentences for actions it performed, such " +
        "as moving the viewer or outlining a site boundary. Do not write those yourself, and do " +
        "not repeat them.";
}

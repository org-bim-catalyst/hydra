// Ported from Supertone's Supertonic C# reference implementation (Helper.cs):
// https://github.com/supertone-inc/supertonic
//
// MIT License
//
// Copyright (c) 2026 Supertone Inc.
//
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software
// and associated documentation files (the "Software"), to deal in the Software without
// restriction, including without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all copies or
// substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING
// BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
// DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

using System.Text;
using System.Text.RegularExpressions;

namespace AskLucy.Infrastructure.Ai.Supertonic;

/// <summary>
/// Supertonic's text front end: language resolution, normalisation, sentence chunking and the
/// code-unit → token-id mapping. Pure functions, kept apart from the ONNX sessions so they can be
/// tested without the model files.
/// </summary>
internal static partial class SupertonicText
{
    public const string FallbackLanguage = "en";

    private static readonly HashSet<string> SupportedLanguages = new(StringComparer.Ordinal)
    {
        "en", "ko", "ja", "ar", "bg", "cs", "da", "de", "el", "es", "et", "fi", "fr", "hi", "hr", "hu",
        "id", "it", "lt", "lv", "nl", "pl", "pt", "ro", "ru", "sk", "sl", "sv", "tr", "uk", "vi", "na",
    };

    private static readonly (string From, string To)[] SymbolReplacements =
    [
        ("–", "-"), ("‑", "-"), ("—", "-"), ("_", " "),
        ("“", "\""), ("”", "\""), ("‘", "'"), ("’", "'"),
        ("´", "'"), ("`", "'"),
        ("[", " "), ("]", " "), ("|", " "), ("/", " "), ("#", " "), ("→", " "), ("←", " "),
    ];

    private static readonly (string From, string To)[] ExpressionReplacements =
    [
        ("@", " at "), ("e.g.,", "for example, "), ("i.e.,", "that is, "),
    ];

    /// <summary>Reduces a BCP 47 tag ("en-US", "AR") to the bare code Supertonic was trained on,
    /// or <c>null</c> when the model does not speak that language.</summary>
    public static string? ResolveLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        var primary = language.Trim().Split('-', '_')[0].ToLowerInvariant();
        return SupportedLanguages.Contains(primary) ? primary : null;
    }

    /// <summary>Splits on paragraphs, then sentences (skipping common abbreviations), packing
    /// sentences into chunks of at most <paramref name="maxLength"/> characters. A single sentence
    /// longer than the limit stays whole, as upstream does.</summary>
    public static IReadOnlyList<string> Chunk(string text, string language)
    {
        var maxLength = language is "ko" or "ja" ? 120 : 300;
        var chunks = new List<string>();

        var paragraphs = ParagraphBreak().Split(text.Trim())
            .Select(p => p.Trim())
            .Where(p => p.Length > 0);

        foreach (var paragraph in paragraphs)
        {
            var current = new StringBuilder();
            foreach (var sentence in SentenceBreak().Split(paragraph))
            {
                if (sentence.Length == 0)
                {
                    continue;
                }

                if (current.Length + sentence.Length + 1 <= maxLength)
                {
                    if (current.Length > 0)
                    {
                        current.Append(' ');
                    }

                    current.Append(sentence);
                }
                else
                {
                    if (current.Length > 0)
                    {
                        chunks.Add(current.ToString().Trim());
                    }

                    current.Clear().Append(sentence);
                }
            }

            if (current.Length > 0)
            {
                chunks.Add(current.ToString().Trim());
            }
        }

        if (chunks.Count == 0 && text.Trim().Length > 0)
        {
            chunks.Add(text.Trim());
        }

        return chunks;
    }

    /// <summary>Normalises <paramref name="text"/> and wraps it in the language tags the text
    /// encoder expects. <paramref name="language"/> must already be a supported code.</summary>
    public static string Preprocess(string text, string language)
    {
        text = RemoveEmojis(text.Normalize(NormalizationForm.FormKD));

        foreach (var (from, to) in SymbolReplacements)
        {
            text = text.Replace(from, to, StringComparison.Ordinal);
        }

        text = SpecialSymbols().Replace(text, string.Empty);

        foreach (var (from, to) in ExpressionReplacements)
        {
            text = text.Replace(from, to, StringComparison.Ordinal);
        }

        text = SpaceBeforePunctuation().Replace(text, "$1");
        text = RepeatedQuote().Replace(text, "$1");
        text = Whitespace().Replace(text, " ").Trim();

        if (!EndsWithTerminator().IsMatch(text))
        {
            text += ".";
        }

        return $"<{language}>{text}</{language}>";
    }

    /// <summary>Maps each UTF-16 code unit through the model's indexer; anything outside it is 0.</summary>
    public static long[] ToTokenIds(string preprocessed, long[] indexer)
    {
        var ids = new long[preprocessed.Length];
        for (var i = 0; i < preprocessed.Length; i++)
        {
            var code = preprocessed[i];
            ids[i] = code < indexer.Length ? indexer[code] : 0;
        }

        return ids;
    }

    private static string RemoveEmojis(string text)
    {
        var result = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            int codePoint;
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                codePoint = char.ConvertToUtf32(text[i], text[i + 1]);
                i++;
            }
            else
            {
                codePoint = text[i];
            }

            if (!IsEmoji(codePoint))
            {
                result.Append(char.ConvertFromUtf32(codePoint));
            }
        }

        return result.ToString();
    }

    private static bool IsEmoji(int codePoint) =>
        codePoint is (>= 0x1F600 and <= 0x1F64F)
            or (>= 0x1F300 and <= 0x1F5FF)
            or (>= 0x1F680 and <= 0x1F6FF)
            or (>= 0x1F700 and <= 0x1F77F)
            or (>= 0x1F780 and <= 0x1F7FF)
            or (>= 0x1F800 and <= 0x1F8FF)
            or (>= 0x1F900 and <= 0x1F9FF)
            or (>= 0x1FA00 and <= 0x1FA6F)
            or (>= 0x1FA70 and <= 0x1FAFF)
            or (>= 0x2600 and <= 0x26FF)
            or (>= 0x2700 and <= 0x27BF)
            or (>= 0x1F1E6 and <= 0x1F1FF);

    [GeneratedRegex(@"\n\s*\n+")]
    private static partial Regex ParagraphBreak();

    [GeneratedRegex(@"(?<!Mr\.|Mrs\.|Ms\.|Dr\.|Prof\.|Sr\.|Jr\.|Ph\.D\.|etc\.|e\.g\.|i\.e\.|vs\.|Inc\.|Ltd\.|Co\.|Corp\.|St\.|Ave\.|Blvd\.)(?<!\b[A-Z]\.)(?<=[.!?])\s+")]
    private static partial Regex SentenceBreak();

    [GeneratedRegex(@"[♥☆♡©\\]")]
    private static partial Regex SpecialSymbols();

    [GeneratedRegex(@" ([,.!?;:'])")]
    private static partial Regex SpaceBeforePunctuation();

    [GeneratedRegex(@"([""'`])\1+")]
    private static partial Regex RepeatedQuote();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"[.!?;:,'""“”‘’)\]}…。」』】〉》›»]$")]
    private static partial Regex EndsWithTerminator();
}

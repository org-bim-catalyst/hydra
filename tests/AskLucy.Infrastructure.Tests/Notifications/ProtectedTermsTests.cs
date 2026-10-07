using System.Text.RegularExpressions;
using AskLucy.Infrastructure.Notifications.Templates;
using FluentAssertions;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Notifications;

/// <summary>
/// T188 (SC-016) — protected names stay verbatim in the Arabic defaults, and
/// <see cref="ProtectedTerms"/> is the same list as the canonical <c>docs/localization/do-not-translate.md</c>.
/// </summary>
public sealed partial class ProtectedTermsTests
{
    [Fact]
    public void EveryProtectedTermInAnEnglishSeed_AppearsVerbatimInTheMatchingArabicSeed()
    {
        var seeds = NotificationTemplateSeeder.LoadSeeds();
        var arabic = seeds.Where(s => s.Key.Language == "ar").ToDictionary(s => (s.Key.Type, s.Key.Channel));
        var checkedTerms = 0;

        foreach (var english in seeds.Where(s => s.Key.Language == "en"))
        {
            arabic.TryGetValue((english.Key.Type, english.Key.Channel), out var ar).Should().BeTrue($"'{english.ResourceName}' needs an Arabic twin");
            var englishText = AllText(english);
            var arabicText = AllText(ar!);

            foreach (var term in ProtectedTerms.All.Where(t => ContainsTerm(englishText, t)))
            {
                ContainsTerm(arabicText, term).Should().BeTrue($"'{term}' is in '{english.ResourceName}' and must stay verbatim in Arabic");
                checkedTerms++;
            }
        }

        checkedTerms.Should().BePositive("the English defaults mention Ask Lucy, so the check must have run");
    }

    [Fact]
    public void ProtectedTerms_MatchesTheCanonicalDoc_Exactly()
    {
        var docTerms = File.ReadAllLines(LocateDoc())
            .SkipWhile(line => !line.Trim().Equals("## Protected terms", StringComparison.Ordinal))
            .Skip(1)
            .Select(line => line.TrimEnd())
            .Where(line => line.StartsWith("- ", StringComparison.Ordinal))
            .Select(line => line[2..].Trim())
            .ToList();

        docTerms.Should().NotBeEmpty();
        ProtectedTerms.All.Should().Equal(docTerms);
        ProtectedTerms.All.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void WrapInBdi_WrapsWholeTermsOnly()
    {
        ProtectedTerms.WrapInBdi("Ask Lucy &amp; the APIs use API keys")
            .Should().Be("<bdi>Ask Lucy</bdi> &amp; the APIs use <bdi>API</bdi> keys");
    }

    [Fact]
    public void WrapInBdi_LeavesTextWithoutProtectedTermsUntouched()
    {
        ProtectedTerms.WrapInBdi("مرحبًا بك").Should().Be("مرحبًا بك");
    }

    private static string AllText(TemplateSeed seed)
    {
        var c = seed.Content;
        return string.Join('\n', new[] { seed.Name, c.Subject, c.Preheader, c.Greeting, c.Heading, c.SafetyNote, c.FooterNote, c.Title, c.Message, c.ActionLabel }
            .Concat(c.BodyParagraphs).Where(t => !string.IsNullOrEmpty(t)));
    }

    private static bool ContainsTerm(string text, string term) =>
        Regex.IsMatch(text, @"(?<![\p{L}\p{N}])" + Regex.Escape(term) + @"(?![\p{L}\p{N}])", RegexOptions.CultureInvariant);

    private static string LocateDoc()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "docs", "localization", "do-not-translate.md");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("docs/localization/do-not-translate.md wasn't found above the test assembly directory.");
    }
}

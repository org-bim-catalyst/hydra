using AskLucy.Domain.Notifications;
using FluentAssertions;
using Xunit;

namespace AskLucy.Domain.Tests.Notifications;

public sealed class TemplateTokenParserTests
{
    private static bool Declared(string name) => name is "workflowName" or "failureSummary";

    [Theory]
    [InlineData("Hello {{workflowName}}")]
    [InlineData("Hello {{ workflowName }}")]
    [InlineData("Hello {{   workflowName\t}}")]
    public void Parse_AcceptsWhitespaceVariants(string text)
    {
        var result = TemplateTokenParser.Parse(text);

        result.IsValid.Should().BeTrue();
        result.Variables.Should().Equal("workflowName");
    }

    [Fact]
    public void Parse_ReturnsDistinctVariablesInOrder()
    {
        TemplateTokenParser.Parse("{{ b }} {{ a }} {{ b }}").Variables.Should().Equal("b", "a");
    }

    [Theory]
    [InlineData("Hello {{ workflowName")]
    [InlineData("Hello workflowName }}")]
    [InlineData("Hello {{ 1bad }}")]
    [InlineData("Hello {{ has space }}")]
    [InlineData("Hello {{ a.b }}")]
    [InlineData("Hello {{}}")]
    public void Parse_RejectsMalformedTokens(string text)
    {
        TemplateTokenParser.Parse(text).IsValid.Should().BeFalse();
    }

    [Fact]
    public void ValidateAgainst_ReportsAnUnknownVariable_WithTheOffendingToken()
    {
        var result = TemplateTokenParser.ValidateAgainst("{{ workflowName }} failed: {{ secret }}", Declared);

        result.Errors.Should().ContainSingle().Which.Token.Should().Be("{{ secret }}");
    }

    [Theory]
    [InlineData("See https://evil.example")]
    [InlineData("See http://evil.example")]
    [InlineData("See www.evil.example")]
    [InlineData("Click <a href=x>here</a>")]
    [InlineData("Bold </b>")]
    public void ValidateAgainst_RejectsRawUrlsAndHtml_InTextFields(string text)
    {
        TemplateTokenParser.ValidateAgainst(text, Declared).IsValid.Should().BeFalse();
    }

    [Fact]
    public void ValidateAgainst_AllowsALessThanSignThatIsNotATag()
    {
        TemplateTokenParser.ValidateAgainst("Fewer than 3 < 5 attempts", Declared).IsValid.Should().BeTrue();
    }

    [Fact]
    public void ActionUrl_IsAcceptedOnlyInLinkFields()
    {
        TemplateTokenParser.ValidateAgainst("Open {{ actionUrl }}", Declared).IsValid.Should().BeFalse();
        TemplateTokenParser.ValidateAgainst("{{ actionUrl }}", Declared, TemplateFieldKind.Link).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Substitute_ReplacesWellFormedTokens_AndLeavesTheRest()
    {
        var output = TemplateTokenParser.Substitute("{{ workflowName }} failed {{ 1bad }}", name => name.ToUpperInvariant());

        output.Should().Be("WORKFLOWNAME failed {{ 1bad }}");
    }
}

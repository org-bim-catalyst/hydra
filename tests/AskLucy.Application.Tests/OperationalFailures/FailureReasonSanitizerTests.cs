using AskLucy.Application.OperationalFailures;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.OperationalFailures;

/// <summary>specs/074 research D8 — the corpus every stored reason is redacted against (FR-011–FR-013, SC-006).</summary>
public sealed class FailureReasonSanitizerTests
{
    private const string Jwt =
        "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4ifQ.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c";

    [Theory]
    [InlineData("Request failed: Bearer abcDEF123456.token-value_xyz", "abcDEF123456.token-value_xyz")]
    [InlineData("Token " + Jwt + " was refused", "SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c")]
    [InlineData("OpenAI said sk-proj-Abc123Def456Ghi789Jkl is invalid", "sk-proj-Abc123Def456Ghi789Jkl")]
    [InlineData("Anthropic said sk-ant-api03-Zx9Yw8Vu7Ts6Rq5Po4 is invalid", "sk-ant-api03-Zx9Yw8Vu7Ts6Rq5Po4")]
    [InlineData("Gemini key AIzaSyDaGmWKa4JsXZ-HjGw7ISLn_3namBGewQe rejected", "AIzaSyDaGmWKa4JsXZ-HjGw7ISLn_3namBGewQe")]
    [InlineData("ElevenLabs key xi-8f3k2m9q7w1z5r4t rejected", "xi-8f3k2m9q7w1z5r4t")]
    [InlineData("Authorization: Basic dXNlcjpzZWNyZXRwYXNz", "dXNlcjpzZWNyZXRwYXNz")]
    [InlineData("Cookie: session=abc123secret; theme=dark", "abc123secret")]
    [InlineData("Set-Cookie: refresh=r3fr3shT0k3n; HttpOnly", "r3fr3shT0k3n")]
    [InlineData("{\"authorization\":\"Bearer hunter2hunter2\"}", "hunter2hunter2")]
    [InlineData("xi-api-key: 1a2b3c4d5e6f", "1a2b3c4d5e6f")]
    [InlineData("GET https://api.example.com/v1/models?key=myS3cretKey&alt=json", "myS3cretKey")]
    [InlineData("GET /v1/items?page=2&api_key=S3cretApiKey", "S3cretApiKey")]
    [InlineData("callback?token=t0kenValue", "t0kenValue")]
    [InlineData("blob.core.windows.net/x?sv=2020&sig=SiGnAtUrE%2Bvalue", "SiGnAtUrE%2Bvalue")]
    [InlineData("login failed password=Pa55word!", "Pa55word!")]
    public void Sanitize_RedactsTheSecret(string input, string secret)
    {
        var result = FailureReasonSanitizer.Sanitize(input);

        result.Should().NotContain(secret);
        result.Should().Contain(FailureReasonSanitizer.Redacted);
    }

    [Fact]
    public void Sanitize_RedactsConnectionStringCredentials()
    {
        var result = FailureReasonSanitizer.Sanitize(
            "Cannot open database. Server=sql.example.net;Database=AskLucy;User ID=lucy_admin;Password=Sup3rS3cret!;Encrypt=True");

        result.Should().NotContain("lucy_admin");
        result.Should().NotContain("Sup3rS3cret!");
        result.Should().Contain("Database=AskLucy");
    }

    [Fact]
    public void Sanitize_RedactsALongHexRun()
    {
        var hex = "0123456789abcdef0123456789abcdef01234567";

        FailureReasonSanitizer.Sanitize($"Signature {hex} mismatch").Should().Be($"Signature {FailureReasonSanitizer.Redacted} mismatch");
    }

    [Fact]
    public void Sanitize_RedactsALongBase64Run()
    {
        var base64 = "QWxhZGRpbjpvcGVuIHNlc2FtZQ9aB3cD4eF5gH6iJ7kL8mN9oP0qR1sT2uV3wX4y==";

        FailureReasonSanitizer.Sanitize($"Payload {base64} rejected").Should().Be($"Payload {FailureReasonSanitizer.Redacted} rejected");
    }

    [Fact]
    public void Sanitize_CollapsesMultiLineVendorJsonToOneLine()
    {
        var input = "{\n  \"error\": {\n\t\"message\": \"Model overloaded\",\r\n    \"type\": \"server_error\"\n  }\n}";

        var result = FailureReasonSanitizer.Sanitize(input);

        result.Should().NotContainAny("\n", "\r", "\t", "  ");
        result.Should().Be("{ \"error\": { \"message\": \"Model overloaded\", \"type\": \"server_error\" } }");
    }

    [Fact]
    public void Sanitize_TruncatesTo500CharactersWithAnEllipsis()
    {
        var input = string.Join(' ', Enumerable.Repeat("failure", 200));

        var result = FailureReasonSanitizer.Sanitize(input);

        result.Length.Should().Be(FailureReasonSanitizer.MaxLength);
        result.Should().EndWith("…");
    }

    [Theory]
    [InlineData("The AI provider rejected the credential.")]
    [InlineData("Text-to-speech request failed")]
    [InlineData("Index document failed at stage Embedding (timed out after 30 s).")]
    [InlineData("Authorization failed for the requested scope")]
    [InlineData("Unexpected token '<' in the response")]
    public void Sanitize_LeavesPlainSystemProseUnchanged(string prose)
    {
        FailureReasonSanitizer.Sanitize(prose).Should().Be(prose);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\t ")]
    public void Sanitize_ReturnsEmptyForNoReason(string? input)
    {
        FailureReasonSanitizer.Sanitize(input).Should().BeEmpty();
    }

    [Fact]
    public void Sanitize_DoesNotLeakASecretCutByThePreTruncationBound()
    {
        // 19 redactable 200-character keys shrink to well under 500 characters, so a key cut by the
        // internal pre-truncation bound would reach the output as a fragment too short to match.
        var keys = string.Concat(Enumerable.Repeat("sk-" + new string('a', 196) + " ", 19));
        var prose = string.Concat(Enumerable.Repeat("word ", 39));
        var input = keys + prose + "xi-8f3k2m9q7w1z5r4t";

        (keys + prose).Length.Should().Be(FailureReasonSanitizer.InputBound - 5);
        FailureReasonSanitizer.Sanitize(input).Should().NotContain("xi-8f").And.EndWith("word");
    }
}

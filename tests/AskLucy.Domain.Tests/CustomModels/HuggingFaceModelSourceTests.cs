using AskLucy.Domain.CustomModels;
using FluentAssertions;
using Xunit;

namespace AskLucy.Domain.Tests.CustomModels;

/// <summary>specs/072 research D4 — the source grammar is the first SSRF layer, so every rejection is a pure Domain test.</summary>
public sealed class HuggingFaceModelSourceTests
{
    [Theory]
    [InlineData("https://huggingface.co/Supertone/supertonic-3", "Supertone/supertonic-3", "main", null)]
    [InlineData("https://www.huggingface.co/Supertone/supertonic-3", "Supertone/supertonic-3", "main", null)]
    [InlineData("http://huggingface.co/Supertone/supertonic-3", "Supertone/supertonic-3", "main", null)]
    [InlineData("https://huggingface.co/Supertone/supertonic-3/", "Supertone/supertonic-3", "main", null)]
    [InlineData("https://huggingface.co/Supertone/supertonic-3/tree/main", "Supertone/supertonic-3", "main", null)]
    [InlineData("https://huggingface.co/Supertone/supertonic-3/tree/v1.0", "Supertone/supertonic-3", "v1.0", null)]
    [InlineData("https://huggingface.co/Supertone/supertonic-3/tree/refs/pr/3", "Supertone/supertonic-3", "refs/pr/3", null)]
    [InlineData("https://huggingface.co/Supertone/supertonic-3/resolve/main/onnx/x.onnx", "Supertone/supertonic-3", "main", "onnx/x.onnx")]
    [InlineData("https://huggingface.co/Supertone/supertonic-3/blob/0123456789abcdef0123456789abcdef01234567/README.md", "Supertone/supertonic-3", "0123456789abcdef0123456789abcdef01234567", "README.md")]
    public void TryParse_ShouldAccept_ModelRepositoryUrls(string raw, string repositoryId, string revision, string? ignoredFilePath)
    {
        var ok = HuggingFaceModelSource.TryParse(raw, out var source, out var error);

        ok.Should().BeTrue(error);
        error.Should().BeNull();
        source!.RepositoryId.Should().Be(repositoryId);
        source.Owner.Should().Be("Supertone");
        source.Repository.Should().Be("supertonic-3");
        source.Revision.Should().Be(revision);
        source.IgnoredFilePath.Should().Be(ignoredFilePath);
        source.DerivedName.Should().Be("supertonic-3");
        source.SourceUrl.Should().Be(raw);
    }

    [Theory]
    [InlineData("https://github.com/Supertone/supertonic-3")]
    [InlineData("https://huggingface.co.evil.example/Supertone/supertonic-3")]
    [InlineData("https://huggingface.co@evil.example/Supertone/supertonic-3")]
    [InlineData("https://user:secret@huggingface.co/Supertone/supertonic-3")]
    [InlineData("https://18.244.28.50/Supertone/supertonic-3")]
    [InlineData("https://[::1]/Supertone/supertonic-3")]
    [InlineData("https://huggingface.co:8443/Supertone/supertonic-3")]
    [InlineData("ftp://huggingface.co/Supertone/supertonic-3")]
    [InlineData("file:///C:/Supertone/supertonic-3")]
    [InlineData("https://huggingface.co/datasets/Supertone/supertonic-3")]
    [InlineData("https://huggingface.co/spaces/Supertone/supertonic-3")]
    [InlineData("https://huggingface.co/Supertone")]
    [InlineData("https://huggingface.co/")]
    [InlineData("https://huggingface.co/../supertonic-3")]
    [InlineData("https://huggingface.co/Supertone/..")]
    [InlineData("https://huggingface.co/Supertone/.")]
    [InlineData("https://huggingface.co/./supertonic-3")]
    [InlineData("https://huggingface.co/a/../Supertone/supertonic-3")]
    [InlineData("https://huggingface.co/Supertone/%2e%2e")]
    [InlineData("https://huggingface.co/Super tone/supertonic-3")]
    [InlineData("https://huggingface.co/Supertone/supertonic 3")]
    [InlineData("https://huggingface.co\\Supertone\\supertonic-3")]
    [InlineData("https://huggingface.co/Supertone/supertonic-3/discussions")]
    [InlineData("https://huggingface.co/Supertone/supertonic-3/tree")]
    [InlineData("https://huggingface.co/Supertone/supertonic-3/tree/refs/pr")]
    [InlineData("https://huggingface.co/Supertone/supertonic-3/tree/-bad")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Supertone/supertonic-3")]
    [InlineData("/Supertone/supertonic-3")]
    public void TryParse_ShouldReject_AnythingThatIsNotAHuggingFaceModelRepository(string raw)
    {
        var ok = HuggingFaceModelSource.TryParse(raw, out var source, out var error);

        ok.Should().BeFalse();
        source.Should().BeNull();
        error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TryParse_ShouldReject_Null()
    {
        HuggingFaceModelSource.TryParse(null, out var source, out var error).Should().BeFalse();
        source.Should().BeNull();
        error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TryParse_ShouldTrimSurroundingWhitespace()
    {
        HuggingFaceModelSource.TryParse("  https://huggingface.co/Supertone/supertonic-3  ", out var source, out _).Should().BeTrue();
        source!.SourceUrl.Should().Be("https://huggingface.co/Supertone/supertonic-3");
    }

    [Fact]
    public void TryParse_ShouldNameTheReason_ForADatasetUrl()
    {
        _ = HuggingFaceModelSource.TryParse("https://huggingface.co/datasets/x/y", out _, out var error);

        error.Should().Contain("not a model repository");
    }

    [Fact]
    public void TryParse_ShouldNameTheReason_ForAnotherHost()
    {
        _ = HuggingFaceModelSource.TryParse("https://github.com/x/y", out _, out var error);

        error.Should().Contain("huggingface.co");
    }
}

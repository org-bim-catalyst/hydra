using AskLucy.Domain.Documents;
using AskLucy.Infrastructure.Documents.Preview;
using FluentAssertions;
using ImageMagick;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Documents.Preview;

/// <summary>FR-043 — the thumbnail generator must decode every supported raster format and shrink it to 400 px.</summary>
public sealed class ImageThumbnailGeneratorTests
{
    [Theory]
    [InlineData(MagickFormat.Png, DocumentFileType.Png)]
    [InlineData(MagickFormat.Jpeg, DocumentFileType.Jpeg)]
    [InlineData(MagickFormat.WebP, DocumentFileType.Webp)]
    [InlineData(MagickFormat.Tiff, DocumentFileType.Tiff)]
    public async Task GenerateAsync_ShouldDecodeTheFormat_AndShrinkToTheThumbnailBar(MagickFormat format, DocumentFileType fileType)
    {
        using var source = new MagickImage(MagickColors.Red, 800, 600);
        using var content = new MemoryStream(source.ToByteArray(format));
        var generator = new ImageThumbnailGenerator();

        generator.CanHandle(fileType).Should().BeTrue();

        var results = await generator.GenerateAsync(content, fileType, TestContext.Current.CancellationToken);

        var thumbnail = results.Should().ContainSingle().Subject;
        thumbnail.PreviewType.Should().Be(DocumentPreviewType.Thumbnail);
        thumbnail.Content.Should().NotBeNullOrEmpty();

        using var decoded = new MagickImage(thumbnail.Content!);
        decoded.Format.Should().Be(MagickFormat.Png);
        decoded.Width.Should().Be(400, "the 800×600 source scales down to the 400 px bar with its aspect preserved");
        decoded.Height.Should().Be(300);
    }

    [Fact]
    public void CanHandle_ShouldRefuseFormatsThePreviewPipelineDoesNotTreatAsImages()
    {
        var generator = new ImageThumbnailGenerator();

        generator.CanHandle(DocumentFileType.Pdf).Should().BeFalse();
        generator.CanHandle(DocumentFileType.Word).Should().BeFalse();
    }
}

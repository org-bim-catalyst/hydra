using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai.Images;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Ai.Images;

/// <summary>
/// Every form a provider may return an image in — hosted URL, base64, data URL, binary — must come
/// out as the same verified bytes, with the format read from the content itself, never trusted
/// from a declared MIME type.
/// </summary>
public sealed class GeneratedImageMaterializerTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x01];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];
    private static readonly byte[] Webp = [.. "RIFF"u8.ToArray(), 0x24, 0x00, 0x00, 0x00, .. "WEBP"u8.ToArray(), .. "VP8 "u8.ToArray()];

    private readonly IRemoteFileDownloader _downloader = Substitute.For<IRemoteFileDownloader>();

    private GeneratedImageMaterializer CreateSut() => new(_downloader);

    [Fact]
    public async Task MaterializeAsync_ShouldDecodeBase64_AsReturnedByGptImageModels()
    {
        var image = await CreateSut().MaterializeAsync(new GeneratedImagePayload.Base64(Convert.ToBase64String(Png)), TestContext.Current.CancellationToken);

        image.Content.Should().Equal(Png);
        image.ContentType.Should().Be("image/png");
        image.FileExtension.Should().Be(".png");
    }

    [Fact]
    public async Task MaterializeAsync_ShouldIgnoreWhitespaceWrappedAcrossLines_InBase64()
    {
        var wrapped = string.Join("\n", Convert.ToBase64String(Png).Chunk(4).Select(c => new string(c)));

        var image = await CreateSut().MaterializeAsync(new GeneratedImagePayload.Base64(wrapped), TestContext.Current.CancellationToken);

        image.Content.Should().Equal(Png);
    }

    [Fact]
    public async Task MaterializeAsync_ShouldDecodeABase64DataUrl()
    {
        var dataUrl = $"data:image/jpeg;base64,{Convert.ToBase64String(Jpeg)}";

        var image = await CreateSut().MaterializeAsync(new GeneratedImagePayload.DataUrl(dataUrl), TestContext.Current.CancellationToken);

        image.Content.Should().Equal(Jpeg);
        image.ContentType.Should().Be("image/jpeg");
        image.FileExtension.Should().Be(".jpg");
    }

    [Fact]
    public async Task MaterializeAsync_ShouldAcceptABinaryBody_AndDetectWebp()
    {
        var image = await CreateSut().MaterializeAsync(new GeneratedImagePayload.Binary(Webp), TestContext.Current.CancellationToken);

        image.ContentType.Should().Be("image/webp");
        image.FileExtension.Should().Be(".webp");
    }

    [Fact]
    public async Task MaterializeAsync_ShouldDownloadARemoteUrl()
    {
        var url = new Uri("https://files.example/img.png");
        _downloader.DownloadAsync(url, Arg.Any<CancellationToken>()).Returns(new DownloadedFile(new MemoryStream(Png), "image/png"));

        var image = await CreateSut().MaterializeAsync(new GeneratedImagePayload.RemoteUrl(url), TestContext.Current.CancellationToken);

        image.Content.Should().Equal(Png);
    }

    [Fact]
    public async Task MaterializeAsync_ShouldTrustTheBytes_NotTheDeclaredMimeType()
    {
        var image = await CreateSut().MaterializeAsync(new GeneratedImagePayload.Base64(Convert.ToBase64String(Jpeg), "image/png"), TestContext.Current.CancellationToken);

        image.ContentType.Should().Be("image/jpeg");
    }

    [Fact]
    public async Task MaterializeAsync_ShouldReject_ContentThatIsNotAnImage()
    {
        var html = "<html>error</html>"u8.ToArray();

        var act = () => CreateSut().MaterializeAsync(new GeneratedImagePayload.Binary(html), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidGeneratedImageException>();
    }

    [Fact]
    public async Task MaterializeAsync_ShouldReject_MalformedBase64()
    {
        var act = () => CreateSut().MaterializeAsync(new GeneratedImagePayload.Base64("not*base64!"), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidGeneratedImageException>();
    }

    [Fact]
    public async Task MaterializeAsync_ShouldReject_AnOversizedImage()
    {
        var oversized = new byte[GeneratedImageMaterializer.MaxImageBytes + 1];
        Png.CopyTo(oversized, 0);

        var act = () => CreateSut().MaterializeAsync(new GeneratedImagePayload.Binary(oversized), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidGeneratedImageException>();
    }
}

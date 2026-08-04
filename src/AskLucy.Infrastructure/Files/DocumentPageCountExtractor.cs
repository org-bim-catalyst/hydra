using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using AskLucy.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace AskLucy.Infrastructure.Files;

internal static partial class DocumentPageCountExtractorLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Page-count extraction failed for a {DocumentType} document — leaving PageCount unset")]
    public static partial void ExtractionFailed(ILogger logger, Exception exception, KnowledgeBaseDocumentType documentType);
}

/// <summary>
/// BCL-only page-count extraction (research.md Decision 5 — no PDF/Office parsing library
/// dependency). PPTX counts slide XML entries directly; DOCX reads Word's own
/// <c>docProps/app.xml</c> page-count hint; PDF does a lightweight regex scan for the
/// document's <c>/Type /Pages</c> object's <c>/Count</c> value rather than a full xref/
/// trailer parse. Any failure is caught and logged, never thrown (interface contract).
/// </summary>
public sealed partial class DocumentPageCountExtractor(ILogger<DocumentPageCountExtractor> logger) : IDocumentPageCountExtractor
{
    [GeneratedRegex(@"/Type\s*/Pages\b[\s\S]{0,500}?/Count\s+(\d+)", RegexOptions.None, "en-US")]
    private static partial Regex PagesForwardPattern();

    [GeneratedRegex(@"/Count\s+(\d+)[\s\S]{0,500}?/Type\s*/Pages\b", RegexOptions.None, "en-US")]
    private static partial Regex PagesBackwardPattern();

    [GeneratedRegex(@"^ppt/slides/slide\d+\.xml$", RegexOptions.None, "en-US")]
    private static partial Regex SlideEntryPattern();

    [GeneratedRegex(@"<Pages>(\d+)</Pages>", RegexOptions.None, "en-US")]
    private static partial Regex WordAppPagesPattern();

    public async Task<int?> ExtractPageCountAsync(Stream content, KnowledgeBaseDocumentType documentType, CancellationToken cancellationToken = default)
    {
        try
        {
            return documentType switch
            {
                KnowledgeBaseDocumentType.Pdf => await ExtractPdfPageCountAsync(content, cancellationToken),
                KnowledgeBaseDocumentType.PowerPoint => ExtractPptxSlideCount(content),
                KnowledgeBaseDocumentType.Word => ExtractDocxPageCount(content),
                _ => null,
            };
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or FormatException)
        {
            DocumentPageCountExtractorLog.ExtractionFailed(logger, ex, documentType);
            return null;
        }
    }

    private static async Task<int?> ExtractPdfPageCountAsync(Stream content, CancellationToken cancellationToken)
    {
        content.Position = 0;
        using var reader = new StreamReader(content, Encoding.Latin1, leaveOpen: true);
        var text = await reader.ReadToEndAsync(cancellationToken);
        content.Position = 0;

        var match = PagesForwardPattern().Match(text);
        if (!match.Success)
        {
            match = PagesBackwardPattern().Match(text);
        }

        return match.Success && int.TryParse(match.Groups[1].Value, out var count) ? count : null;
    }

    private static int? ExtractPptxSlideCount(Stream content)
    {
        content.Position = 0;
        using var archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
        content.Position = 0;

        var slideCount = archive.Entries.Count(e => SlideEntryPattern().IsMatch(e.FullName));
        return slideCount > 0 ? slideCount : null;
    }

    private static int? ExtractDocxPageCount(Stream content)
    {
        content.Position = 0;
        using var archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
        var appXmlEntry = archive.GetEntry("docProps/app.xml");
        content.Position = 0;

        if (appXmlEntry is null)
        {
            return null;
        }

        using var appXmlStream = appXmlEntry.Open();
        using var appXmlReader = new StreamReader(appXmlStream, Encoding.UTF8);
        var xml = appXmlReader.ReadToEnd();

        var match = WordAppPagesPattern().Match(xml);
        return match.Success && int.TryParse(match.Groups[1].Value, out var count) ? count : null;
    }
}

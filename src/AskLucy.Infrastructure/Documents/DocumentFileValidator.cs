using System.IO.Compression;
using System.Text;
using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Domain.Documents;

namespace AskLucy.Infrastructure.Documents;

/// <summary>
/// Magic-byte content validator for the full <see cref="DocumentFileType"/> set (research.md
/// Decision 11). Byte-signature logic for formats shared with the knowledge-base validator
/// (PDF, OOXML, plain-text sniffing) is deliberately re-derived here rather than shared via a
/// common helper — two small, independently-evolving call sites reading the same well-known
/// magic bytes is the DRY-safe kind of duplication (research.md Decision 11), not the business-
/// logic duplication constitution §2.III forbids.
/// </summary>
public sealed class DocumentFileValidator : IDocumentFileValidator
{
    private static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();
    private static readonly byte[] ZipSignature = [0x50, 0x4B, 0x03, 0x04];
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] BmpSignature = "BM"u8.ToArray();
    private static readonly byte[] TiffLittleEndianSignature = [0x49, 0x49, 0x2A, 0x00];
    private static readonly byte[] TiffBigEndianSignature = [0x4D, 0x4D, 0x00, 0x2A];
    private static readonly byte[] RiffSignature = "RIFF"u8.ToArray();
    private static readonly byte[] WebpSignature = "WEBP"u8.ToArray();
    private static readonly byte[] RtfSignature = @"{\rtf"u8.ToArray();

    public async Task<DocumentFileValidationResult> ValidateAsync(Stream content, string fileName, CancellationToken cancellationToken = default)
    {
        var buffered = content.CanSeek ? content : await BufferAsync(content, cancellationToken);
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        var header = new byte[Math.Min(16, (int)Math.Max(0, buffered.Length))];
        buffered.Position = 0;
        var read = await buffered.ReadAsync(header.AsMemory(0, header.Length), cancellationToken);
        buffered.Position = 0;

        if (StartsWith(header, read, PdfSignature))
        {
            return extension == ".pdf"
                ? DocumentFileValidationResult.Valid(DocumentFileType.Pdf, "application/pdf")
                : DocumentFileValidationResult.Invalid($"File content is a PDF, but its name has extension '{extension}'.");
        }

        if (StartsWith(header, read, ZipSignature))
        {
            return ValidateOoxml(buffered, extension);
        }

        if (StartsWith(header, read, PngSignature))
        {
            return extension == ".png"
                ? DocumentFileValidationResult.Valid(DocumentFileType.Png, "image/png")
                : DocumentFileValidationResult.Invalid($"File content is a PNG image, but its name has extension '{extension}'.");
        }

        if (StartsWith(header, read, JpegSignature))
        {
            return extension is ".jpg" or ".jpeg"
                ? DocumentFileValidationResult.Valid(DocumentFileType.Jpeg, "image/jpeg")
                : DocumentFileValidationResult.Invalid($"File content is a JPEG image, but its name has extension '{extension}'.");
        }

        if (StartsWith(header, read, TiffLittleEndianSignature) || StartsWith(header, read, TiffBigEndianSignature))
        {
            return extension is ".tif" or ".tiff"
                ? DocumentFileValidationResult.Valid(DocumentFileType.Tiff, "image/tiff")
                : DocumentFileValidationResult.Invalid($"File content is a TIFF image, but its name has extension '{extension}'.");
        }

        if (StartsWith(header, read, BmpSignature))
        {
            return extension == ".bmp"
                ? DocumentFileValidationResult.Valid(DocumentFileType.Bmp, "image/bmp")
                : DocumentFileValidationResult.Invalid($"File content is a BMP image, but its name has extension '{extension}'.");
        }

        if (StartsWith(header, read, RiffSignature) && read >= 12 && header.AsSpan(8, 4).SequenceEqual(WebpSignature))
        {
            return extension == ".webp"
                ? DocumentFileValidationResult.Valid(DocumentFileType.Webp, "image/webp")
                : DocumentFileValidationResult.Invalid($"File content is a WEBP image, but its name has extension '{extension}'.");
        }

        if (StartsWith(header, read, RtfSignature))
        {
            return extension == ".rtf"
                ? DocumentFileValidationResult.Valid(DocumentFileType.Rtf, "application/rtf")
                : DocumentFileValidationResult.Invalid($"File content is an RTF document, but its name has extension '{extension}'.");
        }

        if (IsPlainText(buffered))
        {
            return await ValidatePlainTextAsync(buffered, extension, cancellationToken);
        }

        return DocumentFileValidationResult.Invalid(
            "File content does not match any supported document type (PDF, Word, Excel, PowerPoint, RTF, Markdown, HTML, CSV, JSON, XML, Text, PNG, JPEG, TIFF, BMP, WEBP).");
    }

    private static DocumentFileValidationResult ValidateOoxml(Stream buffered, string extension)
    {
        try
        {
            using var archive = new ZipArchive(buffered, ZipArchiveMode.Read, leaveOpen: true);
            buffered.Position = 0;

            var hasWord = archive.GetEntry("word/document.xml") is not null;
            var hasExcel = archive.GetEntry("xl/workbook.xml") is not null;
            var hasPowerPoint = archive.GetEntry("ppt/presentation.xml") is not null;

            return (hasWord, hasExcel, hasPowerPoint, extension) switch
            {
                (true, _, _, ".docx") => DocumentFileValidationResult.Valid(
                    DocumentFileType.Word, "application/vnd.openxmlformats-officedocument.wordprocessingml.document"),
                (_, true, _, ".xlsx") => DocumentFileValidationResult.Valid(
                    DocumentFileType.Excel, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
                (_, _, true, ".pptx") => DocumentFileValidationResult.Valid(
                    DocumentFileType.PowerPoint, "application/vnd.openxmlformats-officedocument.presentationml.presentation"),
                (true, _, _, _) => DocumentFileValidationResult.Invalid($"File content is a Word document, but its name has extension '{extension}'."),
                (_, true, _, _) => DocumentFileValidationResult.Invalid($"File content is an Excel workbook, but its name has extension '{extension}'."),
                (_, _, true, _) => DocumentFileValidationResult.Invalid($"File content is a PowerPoint presentation, but its name has extension '{extension}'."),
                _ => DocumentFileValidationResult.Invalid("File is a ZIP archive but not a recognized Office Open XML document."),
            };
        }
        catch (InvalidDataException)
        {
            return DocumentFileValidationResult.Invalid("File content could not be read as a valid archive.");
        }
    }

    private static async Task<DocumentFileValidationResult> ValidatePlainTextAsync(Stream buffered, string extension, CancellationToken cancellationToken)
    {
        switch (extension)
        {
            case ".md":
                return DocumentFileValidationResult.Valid(DocumentFileType.Markdown, "text/markdown");
            case ".csv":
                return DocumentFileValidationResult.Valid(DocumentFileType.Csv, "text/csv");
            case ".txt":
                return DocumentFileValidationResult.Valid(DocumentFileType.Text, "text/plain");
            case ".htm":
            case ".html":
                return DocumentFileValidationResult.Valid(DocumentFileType.Html, "text/html");
            case ".xml":
                return DocumentFileValidationResult.Valid(DocumentFileType.Xml, "application/xml");
            case ".json":
                return await IsValidJsonAsync(buffered, cancellationToken)
                    ? DocumentFileValidationResult.Valid(DocumentFileType.Json, "application/json")
                    : DocumentFileValidationResult.Invalid("File has a '.json' extension, but its content is not valid JSON.");
            default:
                return DocumentFileValidationResult.Invalid(
                    $"File content looks like plain text, but its extension '{extension}' is not one of the supported text types (.md, .csv, .txt, .html, .json, .xml).");
        }
    }

    private static async Task<bool> IsValidJsonAsync(Stream buffered, CancellationToken cancellationToken)
    {
        buffered.Position = 0;
        try
        {
            using var document = await JsonDocument.ParseAsync(buffered, cancellationToken: cancellationToken);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        finally
        {
            buffered.Position = 0;
        }
    }

    private static bool StartsWith(byte[] header, int read, byte[] signature) =>
        read >= signature.Length && header.AsSpan(0, signature.Length).SequenceEqual(signature);

    private static bool IsPlainText(Stream buffered)
    {
        buffered.Position = 0;
        var sampleLength = (int)Math.Min(8000, buffered.Length);
        var sample = new byte[sampleLength];
        _ = buffered.Read(sample, 0, sampleLength);
        buffered.Position = 0;

        // A NUL byte anywhere in the sample is a strong signal of binary content — real text
        // files (including UTF-8/UTF-16 with BOM) don't contain them in normal prose.
        return !sample.Contains((byte)0) && IsValidUtf8(sample);
    }

    private static bool IsValidUtf8(byte[] sample)
    {
        try
        {
            _ = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(sample);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static async Task<MemoryStream> BufferAsync(Stream content, CancellationToken cancellationToken)
    {
        var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;
        return buffer;
    }
}

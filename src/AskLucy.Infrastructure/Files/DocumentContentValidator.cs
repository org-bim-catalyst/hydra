using System.IO.Compression;
using System.Text;
using AskLucy.Application.Abstractions;

namespace AskLucy.Infrastructure.Files;

/// <summary>
/// Magic-byte content validator (constitution §8; specs/014-knowledge-base-management
/// research.md Decision 8) for the supported knowledge-base document types. OOXML formats
/// (.docx/.xlsx/.pptx) share the same ZIP signature, so they are disambiguated by inspecting
/// the archive's own entry names, not by extension. Legacy binary Office formats (.doc/.xls/
/// .ppt) are not supported — only modern Office Open XML.
/// </summary>
public sealed class DocumentContentValidator : IDocumentContentValidator
{
    private static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();
    private static readonly byte[] ZipSignature = [0x50, 0x4B, 0x03, 0x04];

    public async Task<DocumentValidationResult> ValidateAsync(Stream content, string fileName, CancellationToken cancellationToken = default)
    {
        var buffered = content.CanSeek ? content : await BufferAsync(content, cancellationToken);
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        var header = new byte[Math.Min(8, (int)Math.Max(0, buffered.Length))];
        buffered.Position = 0;
        var read = await buffered.ReadAsync(header.AsMemory(0, header.Length), cancellationToken);
        buffered.Position = 0;

        if (read >= PdfSignature.Length && header.AsSpan(0, PdfSignature.Length).SequenceEqual(PdfSignature))
        {
            return extension == ".pdf"
                ? DocumentValidationResult.Valid(KnowledgeBaseDocumentType.Pdf, "application/pdf")
                : DocumentValidationResult.Invalid($"File content is a PDF, but its name has extension '{extension}'.");
        }

        if (read >= ZipSignature.Length && header.AsSpan(0, ZipSignature.Length).SequenceEqual(ZipSignature))
        {
            return ValidateOoxml(buffered, extension);
        }

        if (IsPlainText(buffered))
        {
            return extension switch
            {
                ".md" => DocumentValidationResult.Valid(KnowledgeBaseDocumentType.Markdown, "text/markdown"),
                ".csv" => DocumentValidationResult.Valid(KnowledgeBaseDocumentType.Csv, "text/csv"),
                ".txt" => DocumentValidationResult.Valid(KnowledgeBaseDocumentType.Text, "text/plain"),
                _ => DocumentValidationResult.Invalid($"File content looks like plain text, but its extension '{extension}' is not one of the supported text types (.md, .csv, .txt)."),
            };
        }

        return DocumentValidationResult.Invalid("File content does not match any supported document type (PDF, Word, Excel, PowerPoint, Markdown, CSV, Text).");
    }

    private static DocumentValidationResult ValidateOoxml(Stream buffered, string extension)
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
                (true, _, _, ".docx") => DocumentValidationResult.Valid(
                    KnowledgeBaseDocumentType.Word, "application/vnd.openxmlformats-officedocument.wordprocessingml.document"),
                (_, true, _, ".xlsx") => DocumentValidationResult.Valid(
                    KnowledgeBaseDocumentType.Excel, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
                (_, _, true, ".pptx") => DocumentValidationResult.Valid(
                    KnowledgeBaseDocumentType.PowerPoint, "application/vnd.openxmlformats-officedocument.presentationml.presentation"),
                (true, _, _, _) => DocumentValidationResult.Invalid($"File content is a Word document, but its name has extension '{extension}'."),
                (_, true, _, _) => DocumentValidationResult.Invalid($"File content is an Excel workbook, but its name has extension '{extension}'."),
                (_, _, true, _) => DocumentValidationResult.Invalid($"File content is a PowerPoint presentation, but its name has extension '{extension}'."),
                _ => DocumentValidationResult.Invalid("File is a ZIP archive but not a recognized Office Open XML document."),
            };
        }
        catch (InvalidDataException)
        {
            return DocumentValidationResult.Invalid("File content could not be read as a valid archive.");
        }
    }

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

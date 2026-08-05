using System.Text;
using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Domain.Documents;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;

namespace AskLucy.Infrastructure.Documents.Extraction;

/// <summary>Structured extraction for Office Open XML formats (FR-022, research.md Decision 5) via Microsoft's own OOXML SDK.</summary>
public sealed class OpenXmlTextExtractor : IDocumentTextExtractor
{
    public bool CanHandle(DocumentFileType fileType) =>
        fileType is DocumentFileType.Word or DocumentFileType.Excel or DocumentFileType.PowerPoint;

    public Task<DocumentTextExtractionResult> ExtractAsync(Stream content, DocumentFileType fileType, CancellationToken cancellationToken = default)
    {
        content.Position = 0;

        var result = fileType switch
        {
            DocumentFileType.Word => ExtractWord(content),
            DocumentFileType.Excel => ExtractExcel(content),
            DocumentFileType.PowerPoint => ExtractPowerPoint(content),
            _ => throw new NotSupportedException($"{fileType} is not a supported Office Open XML format."),
        };

        return Task.FromResult(result);
    }

    private static DocumentTextExtractionResult ExtractWord(Stream content)
    {
        using var document = WordprocessingDocument.Open(content, false);
        var body = document.MainDocumentPart?.Document.Body;
        var elements = new List<DocumentStructureElement>();

        if (body is not null)
        {
            foreach (var child in body.ChildElements)
            {
                switch (child)
                {
                    case Paragraph paragraph:
                        AddParagraph(elements, paragraph);
                        break;
                    case DocumentFormat.OpenXml.Wordprocessing.Table table:
                        elements.Add(new DocumentStructureElement("table", ExtractTableText(table)));
                        break;
                }
            }
        }

        var plainText = string.Join('\n', elements.Select(e => e.Text));
        var pageCount = document.ExtendedFilePropertiesPart?.Properties?.Pages?.Text is { } pagesText
            && int.TryParse(pagesText, out var pages) ? pages : (int?)null;

        return WithMetadata(new DocumentTextExtractionResult(plainText, ToJson(elements), pageCount), document);
    }

    /// <summary>Core properties (FR-023) are exposed uniformly on every OOXML package type via <see cref="OpenXmlPackage.PackageProperties"/>.</summary>
    private static DocumentTextExtractionResult WithMetadata(DocumentTextExtractionResult result, DocumentFormat.OpenXml.Packaging.OpenXmlPackage document)
    {
        var properties = document.PackageProperties;
        return result with
        {
            Title = properties.Title,
            Author = properties.Creator,
            CreationDateUtc = properties.Created,
            ModificationDateUtc = properties.Modified,
            Keywords = properties.Keywords,
        };
    }

    private static void AddParagraph(List<DocumentStructureElement> elements, Paragraph paragraph)
    {
        var text = paragraph.InnerText;
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var styleId = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        var headingLevel = ParseHeadingLevel(styleId);

        foreach (var hyperlink in paragraph.Descendants<DocumentFormat.OpenXml.Wordprocessing.Hyperlink>())
        {
            var linkText = hyperlink.InnerText;
            if (!string.IsNullOrWhiteSpace(linkText))
            {
                elements.Add(new DocumentStructureElement("hyperlink", linkText));
            }
        }

        if (headingLevel is not null)
        {
            elements.Add(new DocumentStructureElement("heading", text, headingLevel));
        }
        else if (paragraph.ParagraphProperties?.NumberingProperties is not null)
        {
            elements.Add(new DocumentStructureElement("list-item", text));
        }
        else
        {
            elements.Add(new DocumentStructureElement("paragraph", text));
        }
    }

    private static int? ParseHeadingLevel(string? styleId)
    {
        if (styleId is null || !styleId.StartsWith("Heading", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return int.TryParse(styleId.AsSpan("Heading".Length), out var level) ? level : 1;
    }

    private static string ExtractTableText(DocumentFormat.OpenXml.Wordprocessing.Table table) =>
        string.Join(" | ", table.Descendants<TableCell>().Select(cell => cell.InnerText));

    private static DocumentTextExtractionResult ExtractExcel(Stream content)
    {
        using var document = SpreadsheetDocument.Open(content, false);
        var workbookPart = document.WorkbookPart ?? throw new InvalidDataException("Workbook part missing.");
        var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable;
        var elements = new List<DocumentStructureElement>();

        foreach (var worksheetPart in workbookPart.WorksheetParts)
        {
            var sheetData = worksheetPart.Worksheet.Elements<SheetData>().FirstOrDefault();
            if (sheetData is null)
            {
                continue;
            }

            foreach (var row in sheetData.Elements<Row>())
            {
                var rowText = string.Join(" | ", row.Elements<Cell>().Select(cell => GetCellText(cell, sharedStrings)));
                if (!string.IsNullOrWhiteSpace(rowText))
                {
                    elements.Add(new DocumentStructureElement("table-row", rowText));
                }
            }
        }

        var plainText = string.Join('\n', elements.Select(e => e.Text));
        return WithMetadata(new DocumentTextExtractionResult(plainText, ToJson(elements), PageCount: null), document);
    }

    private static string GetCellText(Cell cell, SharedStringTable? sharedStrings)
    {
        var value = cell.CellValue?.Text;
        if (value is null)
        {
            return string.Empty;
        }

        if (cell.DataType?.Value == CellValues.SharedString && sharedStrings is not null && int.TryParse(value, out var index))
        {
            return sharedStrings.ElementAtOrDefault(index)?.InnerText ?? string.Empty;
        }

        return value;
    }

    private static DocumentTextExtractionResult ExtractPowerPoint(Stream content)
    {
        using var document = PresentationDocument.Open(content, false);
        var slideParts = document.PresentationPart?.SlideParts.ToList() ?? [];
        var elements = new List<DocumentStructureElement>();

        for (var i = 0; i < slideParts.Count; i++)
        {
            var slideText = string.Join(" ", slideParts[i].Slide.Descendants<A.Text>().Select(t => t.Text));
            if (!string.IsNullOrWhiteSpace(slideText))
            {
                elements.Add(new DocumentStructureElement("slide", slideText, PageNumber: i + 1));
            }
        }

        var plainText = string.Join('\n', elements.Select(e => e.Text));
        return WithMetadata(new DocumentTextExtractionResult(plainText, ToJson(elements), slideParts.Count), document);
    }

    private static string ToJson(List<DocumentStructureElement> elements) => JsonSerializer.Serialize(elements);
}

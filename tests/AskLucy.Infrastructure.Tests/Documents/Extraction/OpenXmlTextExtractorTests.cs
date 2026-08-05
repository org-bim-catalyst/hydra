using System.Text.Json;
using AskLucy.Domain.Documents;
using AskLucy.Infrastructure.Documents.Extraction;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;

namespace AskLucy.Infrastructure.Tests.Documents.Extraction;

/// <summary>
/// T063 — <see cref="OpenXmlTextExtractor"/> against a real DOCX built in-memory with
/// <c>DocumentFormat.OpenXml</c> (the same SDK the extractor itself uses), recovering a heading,
/// a paragraph, a list item, and a table (FR-022). Built in-code rather than a checked-in binary
/// fixture — fully reproducible and self-documenting.
/// </summary>
public sealed class OpenXmlTextExtractorTests
{
    private static MemoryStream CreateSampleDocx()
    {
        var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document, autoSave: false))
        {
            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new DocumentFormat.OpenXml.Wordprocessing.Document();
            var body = mainPart.Document.AppendChild(new Body());

            body.AppendChild(new Paragraph(
                new ParagraphProperties(new ParagraphStyleId { Val = "Heading1" }),
                new Run(new Text("Introduction"))));

            body.AppendChild(new Paragraph(new Run(new Text("This is body text."))));

            body.AppendChild(new Paragraph(
                new ParagraphProperties(new NumberingProperties(new NumberingId { Val = 1 })),
                new Run(new Text("First bullet point."))));

            body.AppendChild(new Table(
                new TableProperties(),
                new TableGrid(new GridColumn(), new GridColumn()),
                new TableRow(
                    new TableCell(new TableCellProperties(), new Paragraph(new Run(new Text("A1")))),
                    new TableCell(new TableCellProperties(), new Paragraph(new Run(new Text("B1")))))));

            mainPart.Document.Save();
        }

        stream.Position = 0;
        return stream;
    }

    [Fact]
    public async Task ExtractAsync_ShouldRecoverHeadingsParagraphsListsAndTables_FromARealDocx()
    {
        using var docx = CreateSampleDocx();
        var extractor = new OpenXmlTextExtractor();

        var result = await extractor.ExtractAsync(docx, DocumentFileType.Word, CancellationToken.None);

        result.PlainText.Should().Contain("Introduction");
        result.PlainText.Should().Contain("This is body text.");
        result.PlainText.Should().Contain("First bullet point.");
        result.PlainText.Should().Contain("A1 | B1");

        var elements = JsonSerializer.Deserialize<List<DocumentStructureElement>>(result.StructureJson!)!;
        elements.Should().Contain(e => e.Type == "heading" && e.Level == 1 && e.Text == "Introduction");
        elements.Should().Contain(e => e.Type == "paragraph" && e.Text == "This is body text.");
        elements.Should().Contain(e => e.Type == "list-item" && e.Text == "First bullet point.");
        elements.Should().Contain(e => e.Type == "table" && e.Text == "A1 | B1");
    }
}

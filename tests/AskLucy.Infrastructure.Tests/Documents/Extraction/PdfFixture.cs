using System.Globalization;
using System.Text;

namespace AskLucy.Infrastructure.Tests.Documents.Extraction;

/// <summary>
/// Builds a minimal, real, single-page PDF (uncompressed, correctly cross-referenced) containing
/// one line of Helvetica text — shared by <see cref="DocnetPdfTextExtractorTests"/> and (via
/// rasterization) <c>TesseractOcrEngineTests</c>. Built in-code rather than a checked-in binary
/// fixture — fully reproducible and small enough to read/verify at a glance.
/// </summary>
internal static class PdfFixture
{
    public static byte[] CreateSinglePagePdf(string text, int fontSize = 24)
    {
        using var stream = new MemoryStream();

        void Write(string s)
        {
            var bytes = Encoding.ASCII.GetBytes(s);
            stream.Write(bytes, 0, bytes.Length);
        }

        Write("%PDF-1.4\n");

        var offsets = new long[5];

        offsets[0] = stream.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        offsets[1] = stream.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        offsets[2] = stream.Position;
        Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /Resources << /Font << /F1 4 0 R >> >> "
            + "/MediaBox [0 0 612 792] /Contents 5 0 R >>\nendobj\n");

        offsets[3] = stream.Position;
        Write("4 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n");

        var contentStream = $"BT /F1 {fontSize} Tf 72 700 Td ({text}) Tj ET";
        offsets[4] = stream.Position;
        Write($"5 0 obj\n<< /Length {contentStream.Length} >>\nstream\n{contentStream}\nendstream\nendobj\n");

        var xrefOffset = stream.Position;
        Write("xref\n0 6\n");
        Write("0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            Write($"{offset:D10} 00000 n \n");
        }

        Write("trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n");
        Write(xrefOffset.ToString(CultureInfo.InvariantCulture));
        Write("\n%%EOF");

        return stream.ToArray();
    }
}

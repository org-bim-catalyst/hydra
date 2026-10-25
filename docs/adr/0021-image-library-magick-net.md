# ADR 0021: One Image Library — Magick.NET Replaces ImageSharp

**Status:** Accepted

**Date:** 2026-10-24

**Feature:** [specs/082-editable-health-retention](../../specs/082-editable-health-retention/spec.md) follow-up (dependency hygiene)

## Context

The backend used Six Labors ImageSharp 3.1.12 for every raster operation: uploaded-document
thumbnails (PNG/JPEG/TIFF/WebP), PDF page images, the static-map pixel analysis behind site
boundaries and rendered building footprints, the Gemini segmentation-mask resize/threshold, and
the outline diagnostic JPEG.

The 2026-10 NuGet audit flagged four advisories against 3.1.12 (two high, two moderate), all
reaching image decoding. No 3.x release is patched. The first patched line, 4.1.2+, moved to a
commercial licence: a Release build without a Six Labors key fails with
`No Six Labors license found`, so upgrading is not an option here.

The application decodes user-uploaded files on the thumbnail path, so the advisories are not
theoretical.

## Decision

Replace ImageSharp with **Magick.NET (Q8, AnyCPU)**, which is Apache-2.0 and covers every format
in use, including TIFF and WebP.

- `Infrastructure/Images/RasterImage.cs` is the single decode/encode helper. It exposes the
  RGBA bytes the boundary/footprint thresholding works on, plus JPEG/PNG encoding.
- `ImageThumbnailGenerator`, `PdfPreviewGenerator`, `PixelRingRenderer`,
  `GeminiSegmentationDiagnosticService` and the two map-image extractors call Magick directly
  for format-specific work (resize, raw BGRA ingest, JPEG output).
- Masks are resized with Magick's default filter and thresholded at 127, as before.

## Alternatives considered

- **Stay on 3.1.12** — keeps four unpatched advisories on an untrusted-input path. Rejected.
- **Buy a Six Labors licence and upgrade to 4.1.x** — smallest code change, but adds a paid
  dependency; the standing constraint is free-only libraries.
- **SkiaSharp (MIT)** — lighter, but has no TIFF decoder, and TIFF is an advertised upload
  format for thumbnails.
- **StbImageSharp/StbImageWriteSharp (MIT/public domain)** — pure managed, but no TIFF or WebP,
  and resize would have to be hand-written.

## Consequences

- One native image stack (ImageMagick delegates) ships with the app; the existing native-DLL
  isolation rule (`pdfium_native_dll_collision`) still applies — do not add a second wrapper for
  the same formats.
- Format coverage is a superset of before (TIFF/WebP confirmed by
  `ImageThumbnailGeneratorTests`). Decoder output can differ by a pixel at edges; the boundary
  thresholds are generous and the extracted rings remain within their existing tests.
- The NuGet audit is clean and the Release build has no licence gate.

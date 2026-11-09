# RtfBridge

> **Pure-managed RTF → HTML converter for .NET** — no WPF, no GDI, no native dependencies. Runs on Windows, Linux, and macOS.

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-4.6.2%20%7C%20Standard%202.0%20%7C%208%20%7C%2010-512BD4)](https://dotnet.microsoft.com)
[![Tests](https://img.shields.io/badge/tests-130%20passing-brightgreen)](#)

---

## Packages

| Package | Description | Targets |
|---|---|---|
| `RtfBridge` | Core converter | net462 · netstandard2.0 · net8.0 · net10.0 |
| `RtfBridge.AspNetCore` | DI extensions + minimal-API endpoint | net8.0 · net10.0 |
| `RtfBridge.SystemDrawing` | WMF/EMF vector image renderer (Windows) | net462 · net8.0-windows · net10.0-windows |

---

## Quick Start

### Basic conversion

```csharp
using RtfBridge;

var converter = new RtfBridgeConverter();

// From string
string html = converter.ConvertRtfToHtml(rtfText);

// From stream (e.g. uploaded file)
using var stream = File.OpenRead("document.rtf");
string html = converter.ConvertRtfToHtml(stream, RtfToHtmlOptions.Default);
```

### Fragment only (for embedding in an existing page)

```csharp
string fragment = converter.ConvertRtfToHtml(rtfText, RtfToHtmlOptions.FragmentOnly);
```

### Detailed result with warnings

```csharp
var result = converter.ConvertRtfToHtmlDetailed(rtfText, RtfToHtmlOptions.Default);
Console.WriteLine(result.Html);
foreach (var w in result.Warnings) Console.WriteLine(w);
```

---

## ASP.NET Core Integration

### 1. Register

```csharp
// Program.cs
builder.Services.AddRtfBridge(options =>
{
    options.Sanitization = HtmlSanitizationLevel.Strict;
    options.RenderFootnotesAsList = true;
});
```

### 2. Map the endpoint

```csharp
app.MapRtfToHtml();          // POST /convert/rtf-to-html
// or
app.MapRtfToHtml("/api/rtf"); // custom route
```

### 3. Call it

```bash
curl -X POST https://yourapp/convert/rtf-to-html \
     -H "Content-Type: application/rtf" \
     --data-binary @document.rtf
```

Add `?fullDocument=false` to receive an HTML fragment instead of a full document.

---

## Options

```csharp
var options = new RtfToHtmlOptions
{
    EmitFullDocument      = true,   // full <!DOCTYPE html> wrapper + <style> block
    PreserveListNesting   = true,   // nested <ul>/<ol> for \lsN \ilvlN
    RenderHeadersFooters  = false,  // emit <header>/<footer> elements
    RenderFootnotesAsList = true,   // footnotes as <ol> at document end
    RenderPageBreaks      = true,   // \page / \sect → CSS page-break
    DecodeHtmlEncapsulation = true, // extract original HTML from Outlook RTF (\fromhtml1)
    Sanitization          = HtmlSanitizationLevel.Basic,
    MaxImageBytes         = 20 * 1024 * 1024,
    RenderHiddenText      = false,  // \v hidden text
    DefaultCodePage       = 1252,
};
```

### Sanitization levels

| Level | Behaviour |
|---|---|
| `None` | Text HTML-encoded only; all URLs pass through |
| `Basic` *(default)* | Strips dangerous schemes (`javascript:`, `vbscript:`, `data:`, `file:`) |
| `Strict` | Allows only `http`, `https`, `mailto`, `tel`, and fragment (`#`) links |

---

## WMF / EMF Vector Images (Windows)

Install `RtfBridge.SystemDrawing` and pass its renderer:

```csharp
using RtfBridge.SystemDrawing;

var options = new RtfToHtmlOptions
{
    VectorImageRenderer = new SystemDrawingImageRenderer(),
};
string html = converter.ConvertRtfToHtml(rtfText, options);
```

Without a renderer, vector images emit a `[image]` placeholder.

---

## RTF Feature Coverage

### Text & Formatting
- [x] Bold, italic, underline, strikethrough
- [x] Superscript / subscript
- [x] All-caps, small-caps, hidden text (`\v`)
- [x] Font family and size (`\f`, `\fs`)
- [x] Foreground and highlight colour
- [x] Right-to-left text (`\rtlch`, `\rtlpar`)
- [x] Underline style variants — double, dotted, dashed, wave, thick

### Paragraphs
- [x] Alignment — left, centre, right, justify
- [x] Spacing before/after, indentation, first-line indent
- [x] Line height (`\sl`, `\slmult`)
- [x] Page break before (`\pagebb`), keep together (`\keep`)
- [x] Paragraph borders (all four sides, all RTF border styles)
- [x] Paragraph background shading
- [x] Headings (`\outlinelevel`, "heading N" stylesheet names → `<h1>`–`<h6>`)
- [x] Outline levels (H1–H6)

### Lists
- [x] Unordered and ordered lists (`\listtable`, `\listoverridetable`)
- [x] Nested list levels (`\ilvlN`) with optional flattening
- [x] Legacy `\pn` / `\pnlvlblt` bullet lists
- [x] Custom bullet symbols from symbol libraries
- [x] List inside table cell

### Tables
- [x] Single-level tables (`\trowd`, `\cellx`, `\cell`, `\row`)
- [x] Word's double-`\trowd` pattern (row definition repeated before `\row`)
- [x] Cell borders (per side, all RTF border styles)
- [x] Cell vertical alignment (top / centre / bottom)
- [x] Cell background colour
- [x] Cell width and colspan
- [x] Row height and horizontal alignment
- [x] Header rows (`\trhdr`) → `<thead>` / `<tbody>`
- [x] Nested tables (content preserved, inner table gracefully degraded)

### Images
- [x] PNG, JPEG, GIF embedded as `data:` URIs
- [x] `\pict` hex-encoded images
- [x] `\bin` binary image payloads
- [x] Image size (`\picwgoal`, `\pichgoal`)
- [x] Float / wrap mode (`\wrapmode`)
- [x] WMF / EMF via optional `IImageRenderer` plugin
- [x] Sub-group metadata isolation (GUID, shape properties never corrupt pixel data)

### Character Encoding
- [x] ANSI / Windows-1252 (`\'hh` escapes)
- [x] Double-byte code pages (`\ansicpg932`, Shift-JIS, etc.)
- [x] Per-font charset override (`\fcharset`)
- [x] Unicode (`\uN?`) including UTF-16 surrogate pairs / emoji

### Advanced
- [x] Hyperlinks (`\field` / `\fldinst HYPERLINK`) with URL sanitization
- [x] Bookmarks (`\bkmkstart`, `\bkmkend`) → `<a id="...">`
- [x] Footnotes (`\footnote`) → inline superscript + end-of-document list
- [x] Headers and footers (`\header`, `\footer`, `\headerr`, etc.)
- [x] Outlook RTF-encapsulated HTML extraction (`\fromhtml1`, MS-OXRTFEX)
- [x] Document language (`\deflang`, `\s0 \lang`) → `<html lang="…">`
- [x] Body defaults from Normal style → `<style>` block reduces span verbosity
- [x] Unknown `{\*\destination}` groups skipped cleanly (no content leakage)
- [x] Page breaks and section breaks → CSS `page-break-before:always`
- [x] Tab stops → `&emsp;`

### RTF Versions
- [x] RTF 1.8
- [x] RTF 1.9.1

---

## Architecture

```
RtfBridge/
├── Rtf/
│   ├── RtfTokenizer.cs       — byte-level lexer, handles \'hh / \bin / \u
│   ├── RtfParser.cs          — builds the document model from token stream
│   ├── PictParser.cs         — image hex/binary extraction
│   ├── RtfSymbolLibrary.cs   — bullet/numbering symbol maps
│   └── ListNumberingFormatter.cs
├── Html/
│   ├── HtmlEmitter.cs        — document model → HTML5 string
│   └── HtmlDeEncapsulator.cs — Outlook \fromhtml1 extraction
├── Abstractions/
│   ├── IRtfBridgeConverter.cs
│   ├── RtfToHtmlOptions.cs
│   ├── HtmlSanitizationLevel.cs
│   ├── RtfConversionResult.cs
│   ├── RtfParseException.cs
│   └── IImageRenderer.cs
└── RtfBridgeConverter.cs     — thread-safe public entry point
```

---

## Tests

130 tests across all target frameworks — net462, net8.0, net10.0.

```bash
dotnet test
```

Coverage includes: surrogate pairs, DBCS/Shift-JIS, `\fcharset` overrides, font tables, hyperlink sanitization, list nesting, headings, nested tables, footnotes, headers/footers, Outlook HTML encapsulation, `\pict` sub-group isolation, `\bin` images, RTF parse-exception propagation.

---

## License

MIT © [Amit Tiwari](https://github.com/AbbY2)

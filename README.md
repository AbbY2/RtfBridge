# RtfBridge

Pure-managed **RTF → HTML** conversion library for .NET.

Multi-targets `net462`, `netstandard2.0`, `net8.0`, and `net10.0`. No WPF, no `System.Drawing`, no temp files, no platform lock-in — the core library runs identically on Windows, Linux, and macOS.

Optional companion packages:

- **`RtfBridge.SystemDrawing`** — Windows-only WMF/EMF rasterizer plug-in.
- **`RtfBridge.AspNetCore`** — DI extensions and a ready-made HTTP endpoint for ASP.NET Core.

---

## Packages

| Package | Version | TFMs | Purpose |
|---|---|---|---|
| `RtfBridge` | 2.1.0 | `net462; netstandard2.0; net8.0; net10.0` | Core pure-managed RTF → HTML converter. |
| `RtfBridge.SystemDrawing` | 2.1.0 | `net462; net8.0-windows; net10.0-windows` | WMF/EMF vector image renderer via `System.Drawing.Common` (Windows). |
| `RtfBridge.AspNetCore` | 2.1.0 | `net8.0; net10.0` | `AddRtfBridge()` DI extension and `MapRtfToHtml()` minimal-API endpoint. |

## Install

```powershell
dotnet add package RtfBridge --version 2.1.0
# Optional:
dotnet add package RtfBridge.SystemDrawing --version 2.1.0
dotnet add package RtfBridge.AspNetCore   --version 2.1.0
```

## Quick start

```csharp
using RtfBridge;

var converter = new RtfBridgeConverter();
string html = converter.ConvertRtfToHtml(rtfString);
```

Fragment (body-only) output for email templates or CMS embedding:

```csharp
string fragment = converter.ConvertRtfToHtml(rtfString, RtfToHtmlOptions.FragmentOnly);
```

Detailed conversion with warnings:

```csharp
RtfConversionResult result = converter.ConvertRtfToHtmlDetailed(rtfString, RtfToHtmlOptions.Default);
Console.WriteLine(result.Html);
foreach (var w in result.Warnings) Console.WriteLine($"{w.Code}: {w.Message}");
```

Reading directly from a stream (preserves `\pict` binary/hex payloads exactly):

```csharp
using var fs = File.OpenRead("document.rtf");
string html = converter.ConvertRtfToHtml(fs, RtfToHtmlOptions.Default);
```

## Options

| Option | Default | Purpose |
|---|---|---|
| `EmitFullDocument` | `true` | Wrap output in `<!DOCTYPE html>…</html>`. |
| `InlineStyles` | `false` | Force inline `style="…"` (email-safe). |
| `PreserveListNesting` | `true` | Nest `<ul>`/`<ol>` for `\ilvl` levels. |
| `RenderHeadersFooters` | `false` | Emit `<header>` / `<footer>` sections. |
| `RenderFootnotesAsList` | `true` | Append `<ol class="footnotes">` at end. |
| `RenderPageBreaks` | `true` | Emit `<hr style="page-break-after:always">` for `\page`/`\sect`. |
| `DeterministicOutput` | `true` | Byte-identical HTML for identical input. |
| `DefaultCodePage` | `1252` | Fallback for `\'hh` when `\ansicpg` is missing. |
| `Sanitization` | `Basic` | HTML sanitization level. |
| `MaxImageBytes` | 20 MB | Skip and warn on larger images. |
| `VectorImageRenderer` | `null` | Plug-in for WMF/EMF (see below). |
| `RenderHiddenText` | `false` | Emit `\v` hidden runs as `display:none` spans instead of dropping them. |

Convenience presets: `RtfToHtmlOptions.Default`, `RtfToHtmlOptions.EmailSafe`, `RtfToHtmlOptions.FragmentOnly`.

## Feature matrix

| RTF feature | Status |
|---|---|
| Character formatting: bold, italic, underline, strike, super/sub, font, size, color, highlight | ✅ |
| All caps (`\caps`), small caps (`\scaps`) | ✅ CSS `text-transform` / `font-variant` |
| Hidden text (`\v`) | ✅ dropped by default, opt-in via `RenderHiddenText` |
| Underline variants (`\uld`, `\uldb`, `\uldash*`, `\uldot*`, `\ulwave`, `\ulhwave`, `\ulth*`, `\ulw`, `\ulnone`) | ✅ mapped to CSS `text-decoration` styles |
| Right-to-left (`\rtlpar`, `\rtlch`, `\ltrpar`, `\ltrch`) | ✅ emits `dir="rtl"` |
| Language (`\lang`, `\langfe`, `\langnp`) | ✅ LCID → BCP-47 `lang="…"` |
| Line spacing (`\sl`, `\slmult`) | ✅ CSS `line-height` |
| Keep-together / keep-with-next / page-break-before (`\keep`, `\keepn`, `\pagebb`) | ✅ CSS `page-break-*` |
| Paragraph formatting: alignment, indent (li/ri/fi), spacing (sb/sa) | ✅ |
| Paragraph borders (`\brdrt/r/b/l/box`) with styles (`\brdrs`, `\brdrdb`, `\brdrdot`, `\brdrdash`, `\brdrhair`, `\brdrth`), width (`\brdrw`), color (`\brdrcf`) | ✅ |
| Paragraph shading (`\cbpat`) | ✅ CSS `background-color` |
| Paragraph absolute positioning (`\posx`, `\posy`, `\posxl/c/r/i/o`, `\posyt/c/b`, `\pvpg/\pvmrg/\pvpara`, `\phpg/\phmrg/\phcol`) | ✅ |
| Baseline shift (`\up`, `\dn`) | ✅ CSS `vertical-align` |
| Unicode (`\uN?`), hex escapes (`\'hh`), `\binN` | ✅ |
| Font & color tables | ✅ |
| Special characters (`\emdash`, `\bullet`, quotes, spaces, NBSP, soft hyphen, …) | ✅ |
| Images: PNG, JPEG, DIB (wrapped as BMP), GIF | ✅ inline base64 |
| Image sizing (`\picwgoal`/`\pichgoal`, `\picw`/`\pich` fallback, `\picscalex`/`\picscaley`) | ✅ |
| Image wrap (`\shpwr`) | ✅ CSS `float` / `clear` |
| WMF / EMF | ✅ with `RtfBridge.SystemDrawing` (else placeholder + warning) |
| LibreOffice Symbol-font PUA bullets, shape (`\shp`) pib images, `\shppict`/`\nonshppict` alternates | ✅ |
| Lists: `\ls`/`\ilvl` with list-table + override resolution | ✅ nested |
| Tables: `\trowd`/`\cellx`/`\cell`/`\row`, merged cells | ✅ |
| Table rows: header rows (`\trhdr` → `<thead>`/`<th>`), row alignment (`\trql/qc/qr`), row height (`\trrh`), cell padding (`\trpaddl/t/r/b`) | ✅ |
| Table cells: vertical alignment (`\clvertalt/c/b`), explicit width (`\clwWidth`), shading (`\clcbpat`), per-side borders (`\clbrdrt/r/b/l` with style/width/color) | ✅ |
| Hyperlinks (`\field HYPERLINK`) | ✅ |
| Fields: PAGEREF, REF, DATE/TIME, PAGE/NUMPAGES | ✅ |
| Bookmarks (`\bkmkstart`) | ✅ `<a id>` |
| Footnotes (`\footnote`) | ✅ inline `<sup>` + `<ol class="footnotes">` |
| Headers / footers | ✅ opt-in |
| Sections / page breaks | ✅ |

## WMF / EMF rendering (`RtfBridge.SystemDrawing`)

Plug in the Windows-based rasterizer to embed vector images as inline PNG:

```csharp
using RtfBridge;
using RtfBridge.SystemDrawing;

var options = new RtfToHtmlOptions
{
    VectorImageRenderer = new SystemDrawingImageRenderer()
};
string html = new RtfBridgeConverter().ConvertRtfToHtml(rtfString, options);
```

Requires Windows and `System.Drawing.Common`. On non-Windows hosts the renderer degrades gracefully to the built-in placeholder + warning.

## ASP.NET Core integration (`RtfBridge.AspNetCore`)

```csharp
using RtfBridge.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRtfBridge(o => o.EmitFullDocument = true);

var app = builder.Build();
app.MapRtfToHtml(); // POST /convert/rtf-to-html  (body = raw RTF, response = text/html)
app.Run();
```

- Route pattern is customizable: `app.MapRtfToHtml("/api/rtf");`
- Per-request override: `?fullDocument=false` returns an HTML fragment instead of a full document.
- Request body is read as **bytes**, so `\pict` binary/hex payloads survive framework encoding.
- Content types accepted: `application/rtf`, `text/rtf`, `text/plain`.

## Determinism

Given identical input and options, output is byte-for-byte identical across platforms and runs. Verified by a dedicated golden-file test.

## Robustness

The parser is fully bounded: malformed RTF (unmatched braces, dangling escapes, truncated `\bin`, huge nesting) never hangs and never throws — it returns best-effort HTML plus warnings. Verified by fuzz tests.

## Testing

- `RtfBridge.Tests` — core parser/emitter (runs on `net462`, `net8.0`, `net10.0`).
- `RtfBridge.SystemDrawing.Tests` — WMF/EMF renderer (Windows).
- `RtfBridge.AspNetCore.Tests` — DI + endpoint (uses `Microsoft.AspNetCore.TestHost`).

Run the full suite:

```powershell
dotnet test
```

## Roadmap

- ✅ Phase 0–3 — parser, emitter, images, lists, tables, fields, footnotes, headers/footers.
- ✅ Phase 4 — GA `2.0.0`, docs, fuzz suite, Word-style compatibility tests.
- ✅ Phase 4b — `RtfBridge.SystemDrawing` (WMF/EMF via GDI+).
- ✅ Phase 5 — `net10.0` TFM, `RtfBridge.AspNetCore` package.
- ✅ Phase 6 — High-value syntax gap closure: caps/small caps, hidden text, RTL, language, line spacing, underline style variants, keep-together / page-break-before.
- ✅ Phase 7 — Baseline shift (`\up`/`\dn`), paragraph positioning (`\posx`/`\posy`/named anchors), image wrap (`\shpwr`).
- ✅ Phase 8 — Table row/cell formatting (header rows, row alignment/height, cell padding/width/vertical-align/shading, per-side cell borders) and paragraph borders + shading (`\brdr*`, `\cbpat`).
- ⏳ Phase 9 — Tab leaders (`\tqr\tqc\tldot`), tracked-changes (`\revised`, `\deleted`), extended LCID table, CI perf benchmarks + cross-OS golden-file parity.

## License

See repository root.

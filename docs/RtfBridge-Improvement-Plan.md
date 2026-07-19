# RtfBridge — RTF → HTML Improvement Plan (v2)

**Project goal:** Deliver a **single-purpose RTF → HTML conversion DLL** that:

1. Works from **any .NET consumer** — Console, ASP.NET Core, WinForms, WPF, Blazor Server, Windows Services, Azure Functions, Worker services, unit tests.
2. Runs on **every supported .NET target**: `.NET Framework 4.6.2+`, `.NET Standard 2.0`, `.NET 8`, `.NET 10`.
3. Covers **the full RTF specification (v1.9.1)** feature-set that has a reasonable HTML mapping — text formatting, paragraph formatting, lists (nested), tables (merged cells), all image blip types, Unicode, hyperlinks, fields, bookmarks, footnotes, headers/footers, page/section breaks, special characters.
4. Emits images **inline as base64 data-URIs** (no external image store, no file I/O, no temp files).
5. Ships as a **single NuGet package** with deterministic HTML output.

### Locked decisions (from stakeholder review)

| # | Decision | Rationale |
|---|---|---|
| Q1 | **HTML → RTF conversion is DROPPED in v2.** `HtmlToRtfConverter`, `HtmlToXamlConverter`, `HtmlParser`, `HtmlSchema`, `HtmlCssParser`, `HtmlLexicalAnalyzer`, `HtmlTokenType`, `HtmlFromXamlConverter` all removed. | Scope focus: RTF→HTML only. Removes ~5,000 lines of legacy Microsoft sample code and the entire WPF dependency. |
| Q2 | **Images always emitted as base64 `data:` URIs inline.** No `IImageStore`, no callbacks, no disk I/O. | Simplifies API, keeps output self-contained (single HTML file), no filesystem permissions needed on server hosts. |
| Q3 | **Support all .NET versions via multi-targeting**: `net462; netstandard2.0; net8.0; net10.0`. **No Windows-only TFMs. No WPF reference.** | User requirement: "support all .NET version". Because we drop WPF and write a pure managed parser, we no longer need `-windows` TFMs. `System.Drawing.Common` is replaced by **SkiaSharp** (cross-platform) for WMF/EMF rendering. |
| Q4 | **Deterministic HTML output** (stable attribute order, stable CSS property order, no timestamps). | Enables golden-file testing and safe caching. |
| Q5 | **Single unified NuGet package** `RtfBridge` (v2.0.0). | Simpler for consumers; multi-targeting handles per-framework needs. |

### Target framework matrix

| TFM | Consumers | Notes |
|---|---|---|
| `net462` | Legacy WinForms / ASP.NET (System.Web) / SharePoint | Minimum needed for `Span<T>`-friendly polyfills via `System.Memory` |
| `netstandard2.0` | Xamarin, Unity, Mono, other .NET Standard 2.0 hosts | Broadest compatibility umbrella |
| `net8.0` | Current LTS — ASP.NET Core, Blazor, Functions, Workers | Cross-platform (Windows/Linux/macOS) |
| `net10.0` | Next LTS (upon GA) | Cross-platform |

> **No `-windows` suffix on any TFM.** The DLL runs on Linux and macOS containers.
> Optional: an add-on package `RtfBridge.Skia` provides WMF/EMF → PNG rendering. If not referenced, WMF/EMF images fall back to a placeholder `<span class="rtf-image-unsupported">` or an inline SVG stub, and a warning is added to `RtfConversionResult.Warnings`.

---

## 1. Current State Summary

### 1.1 Project layout
```
RtfBridge/
├── RtfBridgeConverter.cs          (public API + RTF pre/post processing)
├── RtfToHtmlConverter.cs       (RTF → XAML via WPF TextRange → HTML)
├── HtmlFromXamlConverter.cs    (XAML → HTML, Microsoft sample)
├── HtmlToXamlConverter.cs      (HTML → XAML, Microsoft sample)
├── HtmlToRtfConverter.cs       (HTML → XAML → RTF via WPF RichTextBox)
├── HtmlParser.cs, HtmlSchema.cs, HtmlCssParser.cs, HtmlLexicalAnalyzer.cs, HtmlTokenType.cs
├── ImageConverter.cs           (dead SQL CLR decompiled code — must be removed)
└── Properties/AssemblyInfo.cs
```

### 1.2 Current pipeline
```
raw RTF
  → RtfBridge.ConvertRtfToHtml()      (manual string surgery on RTF)
  → RtfToHtmlConverter.ConvertRtfToHtml()   (WPF TextRange: RTF→XAML)
  → HtmlFromXamlConverter.ConvertXamlToHtml (XAML → HTML)
  → post-processing (entity unescape, style injection)
```

### 1.3 Critical problems (recap of code review)
| # | Severity | Issue |
|---|---|---|
| 1 | 🔴 Critical | Infinite-loop risk in image-extraction `do/while` (uses first `}` — ignores nested groups) |
| 2 | 🔴 Critical | `Encoding.ASCII` corrupts every non-ASCII character before it reaches WPF |
| 3 | 🔴 Critical | Double-wrapped `<FlowDocument>` produced by `FlowDocumentFormat` |
| 4 | 🔴 Critical | Broken CSS: `margin - left` (spaces around dash) |
| 5 | 🔴 Critical | JPEG / EMF images silently dropped (no `\jpegblip` / `\emfblip` handler) |
| 6 | 🟠 Major | WMF handling limited to 5 hardcoded pixel-byte patterns; all others dropped |
| 7 | 🟠 Major | `\line` → `<br />` replacement done in RTF **before** WPF parses it |
| 8 | 🟠 Major | Blind `&lt;` / `&gt;` → `<` / `>` unescape corrupts valid HTML |
| 9 | 🟠 Major | All list levels `\ls2..\ls7` flattened to `\ls1` (destroys nested lists) |
| 10 | 🟠 Major | RTF `\header` removal is a naïve string replace |
| 11 | 🟠 Major | `GetMessage()` leaks JPEGs into `C:\TempImg` on every call, swallows all exceptions |
| 12 | 🟠 Major | `ConvertRtfToImage()` concatenates **decimal byte digits** — output is garbage |
| 13 | 🟡 Quality | `ImageConverter.cs` is dead decompiled SQL-CLR code |
| 14 | 🟡 Quality | Hundreds of lines of commented-out dead code |
| 15 | 🟡 Quality | `ToBinary()` has a commented-out null throw (silent NRE risk) |
| 16 | 🟡 Quality | No null / empty input validation on public API |
| 17 | 🟡 Quality | Unused `using System.Data.SqlTypes;` |
| 18 | 🟡 Quality | Legacy non-SDK-style `.csproj`, targets `.NET Framework 4.8` only |

### 1.4 RTF feature coverage — current

| Feature | Status | Notes |
|---|---|---|
| Bold / Italic / Underline | ✅ | Via WPF |
| Strikethrough | ✅ | Via WPF |
| Superscript / Subscript | ✅ | Via WPF |
| Font family / size | ✅ | Via WPF |
| Fore / background color | ✅ | Via WPF |
| Alignment (left/center/right/justify) | ✅ | Via WPF |
| Paragraph spacing / indent | ✅ | Via WPF |
| Tables (basic) | ✅ | Via WPF (styles may need cleanup) |
| Hyperlinks (`\field HYPERLINK`) | ⚠️ | WPF may or may not preserve — needs test |
| Bulleted / numbered lists | ⚠️ | Nested levels collapsed |
| PNG (`\pngblip`) images | ⚠️ | Works but brace-matching fragile |
| JPEG (`\jpegblip`) images | ❌ | Dropped |
| EMF (`\emfblip`) images | ❌ | Dropped |
| WMF (`\wmetafile8`) images | ❌ | Only 5 hardcoded icons handled |
| DIB (`\dibitmap`) images | ❌ | Dropped |
| Unicode (`\uN?`) | ❌ | Broken by ASCII encoding |
| Special characters (`\'hh`) | ⚠️ | Codepage-dependent |
| Non-breaking space (`\~`) | ⚠️ | WPF handles most |
| Optional hyphen (`\-`) / non-breaking hyphen (`\_`) | ⚠️ | Loss possible |
| Tabs (`\tab`) | ⚠️ | Collapse to spaces |
| Page breaks (`\page`) | ❌ | Lost in HTML |
| Section breaks (`\sect`) | ❌ | Lost in HTML |
| Headers / Footers (`\header` / `\footer`) | ❌ | Stripped naively |
| Footnotes (`\footnote`) | ❌ | Lost |
| Endnotes | ❌ | Lost |
| Bookmarks (`\bkmkstart` / `\bkmkend`) | ❌ | Lost |
| Comments / annotations | ❌ | Lost |
| Fields (TOC, date, page number) | ❌ | Result-text sometimes preserved |
| Text boxes / shapes | ❌ | Not supported |
| RTL / bidi text | ❌ | Not handled |
| Encrypted / password RTF | ❌ | Not supported |

---

## 2. Target Architecture

### 2.1 Solution restructure
```
RtfBridge.sln
├── src/
│   └── RtfBridge/                       (net462; netstandard2.0; net8.0; net10.0)
│       ├── Abstractions/       IRtfBridgeConverter, options, DTOs, warnings
│       ├── Rtf/                Tokenizer, parser, AST (pure managed, no WPF)
│       ├── Rtf/Model/          RtfDocument, RtfParagraph, RtfRun, RtfTable, …
│       ├── Images/             PNG / JPEG passthrough, DIB decoder, WMF/EMF stub
│       ├── Html/               HTML5 emitter, CSS builder, encoder
│       └── RtfBridgeConverter.cs  Public facade implementing IRtfBridgeConverter
├── src/
│   └── RtfBridge.Skia/                  (netstandard2.0; net8.0; net10.0) OPTIONAL
│       └── SkiaImageRenderer   WMF/EMF → PNG via SkiaSharp; registered via
│                                RtfBridgeOptions.UseSkiaImageRenderer()
├── tests/
│   ├── RtfBridge.Tests/                 (net8.0, net48; xUnit)
│   └── RtfBridge.Skia.Tests/            (net8.0; xUnit)
└── samples/
	├── ConsoleSample/                         (net8.0) — reads .rtf, writes .html
	└── AspNetCoreSample/                      (net8.0) — POST endpoint
```

Key points:
- **Single core package**, no WPF, no `System.Drawing`, no Windows-only TFMs.
- Optional `RtfBridge.Skia` adds WMF/EMF rasterization; when absent, WMF/EMF images are emitted as an inline SVG placeholder plus a warning.
- The old files (`HtmlFromXamlConverter.cs`, `HtmlToXamlConverter.cs`, `HtmlToRtfConverter.cs`, `HtmlParser.cs`, `HtmlSchema.cs`, `HtmlCssParser.cs`, `HtmlLexicalAnalyzer.cs`, `HtmlTokenType.cs`, `ImageConverter.cs`, `RtfToHtmlConverter.cs`, current `RtfBridgeConverter.cs`) are **all deleted** in v2.

### 2.2 Public API surface

```csharp
namespace RtfBridge;

public interface IRtfBridgeConverter
{
	// Backwards-compatible signature (uses default options).
	string ConvertRtfToHtml(string rtfText);

	// Overloads for full control.
	string ConvertRtfToHtml(string rtfText, RtfToHtmlOptions options);
	string ConvertRtfToHtml(Stream rtfStream, RtfToHtmlOptions options);

	// Diagnostic-rich variant returning warnings and extracted image inventory.
	RtfConversionResult ConvertRtfToHtmlDetailed(string rtfText, RtfToHtmlOptions options);
}

public sealed class RtfToHtmlOptions
{
	public bool   EmitFullDocument      { get; set; } = true;
	public bool   InlineStyles          { get; set; } = false;   // false = single <style> block; true = style="..." per element
	public bool   PreserveListNesting   { get; set; } = true;
	public bool   RenderHeadersFooters  { get; set; } = false;
	public bool   RenderFootnotesAsList { get; set; } = true;
	public bool   RenderPageBreaks      { get; set; } = true;
	public bool   DeterministicOutput   { get; set; } = true;    // stable attribute + CSS ordering
	public int    DefaultCodePage       { get; set; } = 1252;    // overridden by \ansicpg header
	public HtmlSanitizationLevel Sanitization { get; set; } = HtmlSanitizationLevel.Basic;

	// Image rendering (base64 inline is the only mode)
	public IImageRenderer? VectorImageRenderer { get; set; }    // set to SkiaImageRenderer to enable WMF/EMF
	public int    MaxImageBytes         { get; set; } = 20 * 1024 * 1024; // 20 MB per image safety cap

	public static RtfToHtmlOptions Default { get; } = new();
	public static RtfToHtmlOptions EmailSafe { get; } = new() { EmitFullDocument = false, InlineStyles = true };
	public static RtfToHtmlOptions FragmentOnly { get; } = new() { EmitFullDocument = false };
}

public sealed class RtfConversionResult
{
	public string Html { get; init; } = "";
	public IReadOnlyList<RtfConversionWarning> Warnings { get; init; } = Array.Empty<RtfConversionWarning>();
	public IReadOnlyList<RtfEmbeddedImage>    Images   { get; init; } = Array.Empty<RtfEmbeddedImage>();
}

public sealed record RtfConversionWarning(string Code, string Message, int? RtfPosition);
public sealed record RtfEmbeddedImage(string MediaType, int Width, int Height, byte[] Bytes);

public enum HtmlSanitizationLevel { None, Basic, Strict }
```

Notes:
- No `ConvertHtmlToRtf`, no `ConvertHtmlToXaml`, no `ConvertXamlToHtml`, no `ConvertRtfToImage` in v2 (dropped per Q1).
- Images are always base64 inline; the `Images` collection is provided for diagnostics/logging only.
- `IImageRenderer` is a small interface used only when a WMF/EMF image is encountered:
  ```csharp
  public interface IImageRenderer
  {
	  RenderedImage Render(ReadOnlySpan<byte> vectorBytes, RtfImageKind kind);
  }
  ```

### 2.3 Internal RTF model (AST)
```
RtfDocument
├── FontTable, ColorTable, ListTable, StyleSheet, InfoGroup
└── Body: IList<RtfBlock>
	   ├── RtfParagraph
	   │    ├── ParagraphFormat (align, indent, spacing, listId, listLevel)
	   │    └── Runs: IList<RtfRun>
	   │           ├── RtfTextRun          (text + CharFormat)
	   │           ├── RtfLineBreak
	   │           ├── RtfImageRun         (bytes + kind + width/height)
	   │           ├── RtfHyperlinkRun     (target + inner runs)
	   │           ├── RtfFieldRun         (field type + result runs)
	   │           ├── RtfBookmarkRun
	   │           └── RtfFootnoteRun
	   ├── RtfTable
	   │    └── Rows → Cells → Blocks
	   ├── RtfList
	   ├── RtfPageBreak
	   ├── RtfSectionBreak
	   └── RtfHeader / RtfFooter
```
The AST decouples parsing from emission and makes each RTF feature independently testable.

### 2.4 Rendering
- `IHtmlEmitter` walks the AST and emits HTML5.
- CSS strategy configurable: **inline styles** (email-safe) or **`<style>` block with classes**.
- Images: always `data:` URIs (base64 inline). No external store, no callbacks — per locked decision Q2.

---

## 3. Framework Migration Plan (all .NET versions, no WPF)

### 3.1 Convert to SDK-style project
Replace the legacy `.csproj` with:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
	<TargetFrameworks>net462;netstandard2.0;net8.0;net10.0</TargetFrameworks>
	<LangVersion>latest</LangVersion>
	<Nullable>enable</Nullable>
	<ImplicitUsings>enable</ImplicitUsings>
	<GeneratePackageOnBuild>true</GeneratePackageOnBuild>
	<PackageId>RtfBridge</PackageId>
	<Version>2.0.0</Version>
	<Description>Pure-managed RTF → HTML converter. No WPF, no System.Drawing, works on Windows/Linux/macOS.</Description>
	<RepositoryUrl>…</RepositoryUrl>
	<PackageLicenseExpression>MIT</PackageLicenseExpression>
	<IncludeSymbols>true</IncludeSymbols>
	<SymbolPackageFormat>snupkg</SymbolPackageFormat>
	<ContinuousIntegrationBuild Condition="'$(CI)' == 'true'">true</ContinuousIntegrationBuild>
  </PropertyGroup>

  <!-- Polyfills for older TFMs -->
  <ItemGroup Condition="'$(TargetFramework)' == 'net462' or '$(TargetFramework)' == 'netstandard2.0'">
	<PackageReference Include="System.Memory"                    Version="4.5.5" />
	<PackageReference Include="System.Text.Encoding.CodePages"   Version="8.0.0" />
	<PackageReference Include="System.Buffers"                   Version="4.5.1" />
	<PackageReference Include="IndexRange"                       Version="1.0.3" /> <!-- Range/Index -->
	<PackageReference Include="Nullable"                         Version="1.3.1" PrivateAssets="all" />
  </ItemGroup>

  <ItemGroup Condition="'$(TargetFramework)' == 'net8.0' or '$(TargetFramework)' == 'net10.0'">
	<PackageReference Include="System.Text.Encoding.CodePages"   Version="8.0.0" />
  </ItemGroup>
</Project>
```

**No** `System.Drawing.Common`, **no** `PresentationFramework`, **no** `WindowsBase`, **no** `UseWPF`, **no** `-windows` TFMs.

### 3.2 Framework-specific concerns

| Concern | net462 | netstandard2.0 | net8.0 | net10.0 |
|---|---|---|---|---|
| `Span<T>` / `ReadOnlySpan<T>` | via `System.Memory` polyfill | via polyfill | ✅ built-in | ✅ built-in |
| `Encoding.GetEncoding(1252)` | ✅ built-in | needs `CodePagesEncodingProvider` | needs provider | needs provider |
| Nullable reference types | via `Nullable` package | via `Nullable` package | ✅ built-in | ✅ built-in |
| `Index` / `Range` operators | via `IndexRange` | via `IndexRange` | ✅ | ✅ |
| Cross-platform (Linux/macOS) | Windows-only runtime | depends on host | ✅ | ✅ |

### 3.3 Codepage registration bootstrap
The parser needs codepages 1250–1258, MacRoman, and shift-JIS to decode `\'hh` escapes according to `\ansicpg`. Register the provider once, statically:
```csharp
internal static class EncodingBootstrap
{
	private static int _initialized;
	public static void EnsureRegistered()
	{
		if (Interlocked.Exchange(ref _initialized, 1) == 0)
		{
			Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
		}
	}
}
```
Invoked from the static constructor of `RtfBridge` and each public entry point (idempotent, thread-safe).

### 3.4 Optional WMF/EMF renderer package
`RtfBridge.Skia` (separate NuGet, opt-in) targets `netstandard2.0; net8.0; net10.0` and pulls in `SkiaSharp` + `SkiaSharp.NativeAssets.*`. Consumers enable it with:
```csharp
var opts = RtfToHtmlOptions.Default;
opts.VectorImageRenderer = new SkiaImageRenderer();
```
Without it, WMF/EMF payloads become an inline SVG placeholder plus an `RtfConversionWarning("IMG_VECTOR_UNSUPPORTED", …)`. **Raster images (PNG/JPEG/DIB) work everywhere with no extra package.**

---

## 4. Bug-Fix Backlog

> In v2 the old code is **deleted wholesale**, so most "bug fixes" become "do not reproduce these bugs in the new implementation." The list below is the checklist the new parser and emitter must satisfy.

### 4.1 Behaviors that MUST NOT reappear in v2
- **BF-01** No `IndexOf("}", …)` for RTF group boundaries. Always use a proper depth-tracking scanner honoring `\{`, `\}`, `\\` escapes and `{\*\destination …}` optional-destination groups.
- **BF-02** No hand-written CSS strings with spacing errors (`margin - left`). All CSS emitted through a typed `CssDeclaration` writer that validates property names.
- **BF-03** No blind HTML-entity unescaping (`&lt;` → `<`). Encoding happens in the emitter exactly once.
- **BF-04** No pre-parse RTF string surgery. Line breaks (`\line`), tabs (`\tab`), non-breaking space (`\~`), etc. are handled as first-class tokens in the parser.
- **BF-05** No list-level flattening (`\ls2..\ls7` → `\ls1`). `\lsN` and `\ilvlN` map to real nesting depth in the AST → emitted as nested `<ul>`/`<ol>`.
- **BF-06** No blunt `\header` string replace. The tokenizer recognizes `\header`, `\headerl`, `\headerr`, `\headerf`, `\footer`, `\footerl`, `\footerr`, `\footerf` as destinations and either drops or captures them per `RenderHeadersFooters`.
- **BF-07** All public entry points validate arguments (`ArgumentNullException` for null, empty-string returns empty HTML deterministically).
- **BF-08** Delete `GetMessage()` and `ConvertRtfToImage()` in current form — replace with new `IImageDecoder` pipeline (see §5.3).
- **BF-09** Remove `using System.Data.SqlTypes;` and other unused usings.
- **BF-10** Remove `using RtfBridge;` self-import.
- **BF-11** Add null / empty guards + `ArgumentNullException` on all public methods.
- **BF-08** Every RTF byte-stream read uses `Encoding.GetEncoding(codePage)` (default 1252, override from `\ansicpg`). **Never `Encoding.ASCII`.**
- **BF-09** No double-wrapping of markup fragments. Emitter emits exactly one root element.
- **BF-10** No `System.Data.SqlTypes`, no SQL-CLR `[SqlProcedure]`, no `Microsoft.SqlServer.Server` reference anywhere.
- **BF-11** No temp files, no `C:\TempImg`, no `File.Delete`, no exception-swallowing `try { … } catch { }`. Warnings surface via `RtfConversionResult.Warnings`.
- **BF-12** No commented-out code checked in. Source control preserves history.

### 4.2 Files deleted from the repo in v2
The following files are removed entirely (they are either dead code, HTML→RTF/XAML code we're dropping, or bug-riddled legacy paths superseded by the new parser):

- `RtfBridge/RtfBridgeConverter.cs` — replaced by new facade under new folder layout.
- `RtfBridge/RtfToHtmlConverter.cs` — replaced by pure-managed parser + emitter.
- `RtfBridge/HtmlFromXamlConverter.cs` — HTML→RTF path dropped (Q1).
- `RtfBridge/HtmlToXamlConverter.cs` — HTML→RTF path dropped (Q1).
- `RtfBridge/HtmlToRtfConverter.cs` — HTML→RTF path dropped (Q1).
- `RtfBridge/HtmlParser.cs`, `HtmlSchema.cs`, `HtmlCssParser.cs`, `HtmlLexicalAnalyzer.cs`, `HtmlTokenType.cs` — only used by HTML→XAML.
- `RtfBridge/ImageConverter.cs` — dead decompiled SQL-CLR code.
- `RtfBridge/Properties/AssemblyInfo.cs` — replaced by SDK-generated attributes.
- `RtfBridge/RtfBridgeConverter.csproj` — replaced by SDK-style csproj.

Net effect: ~6,000+ lines removed; core replaced by an estimated ~3,500 lines of focused, tested code.

---

## 5. RTF Feature Coverage Roadmap

Each feature has: **Parser support** (tokenizer + AST) → **Emitter support** (HTML) → **Test fixtures**.

### 5.1 Text formatting (character run properties)
| Control word | HTML target | Notes |
|---|---|---|
| `\b` / `\b0` | `<strong>` / `font-weight:bold` | |
| `\i` / `\i0` | `<em>` / `font-style:italic` | |
| `\ul` / `\ulnone` | `<u>` / `text-decoration:underline` | |
| `\strike` | `<s>` / `text-decoration:line-through` | |
| `\super` / `\sub` | `<sup>` / `<sub>` | |
| `\scaps` / `\caps` | `text-transform:uppercase` | |
| `\fN` (font ref) | `font-family:<from font table>` | |
| `\fsN` (half-points) | `font-size:<N/2>pt` | |
| `\cfN` / `\highlightN` | `color:` / `background-color:` from color table | |
| `\lang` | `<span lang=…>` | Optional |

### 5.2 Paragraph formatting
| Control word | HTML target |
|---|---|
| `\pard` | Reset paragraph props |
| `\ql`,`\qc`,`\qr`,`\qj` | `text-align:` left/center/right/justify |
| `\li`,`\ri`,`\fi` | `margin-left`, `margin-right`, `text-indent` (twips → px) |
| `\sb`,`\sa`,`\sl` | `margin-top`, `margin-bottom`, `line-height` |
| `\keep`,`\keepn` | `page-break-inside:avoid` |
| `\brdrt`,`\brdrb`,`\brdrl`,`\brdrr` | `border-*` |

### 5.3 Images
Detection: the tokenizer recognizes the `\pict` destination and captures:
- `\pngblip`, `\jpegblip`, `\emfblip`, `\wmetafile8`, `\dibitmap`, `\wbitmap`
- Dimension hints: `\picw`, `\pich`, `\picwgoal`, `\pichgoal`, `\picscalex`, `\picscaley`

Decoder plug-in interface:
```csharp
public interface IImageDecoder
{
	bool CanDecode(RtfImageKind kind);
	ImageResult Decode(RtfImageData raw); // returns bytes + mime
}
```
Built-in decoders:
- **`PngPassthroughDecoder`** — write hex → bytes → `image/png`.
- **`JpegPassthroughDecoder`** — hex → bytes → `image/jpeg`.
- **`WmfEmfConvertingDecoder`** — hex → `Metafile` → PNG via `System.Drawing.Common`.
- **`DibDecoder`** — DIB header + pixel array → PNG.

Emitter:
- Default: emit `<img src="data:image/…;base64,…" width=… height=…>`.
- If `IImageStore` configured: call `Store(bytes, mime)` and use returned URL.

### 5.4 Lists
- Read `\listtable` and `\listoverridetable` in header once.
- Track `\lsN` (list id) and `\ilvlN` (level) per paragraph.
- Emit properly nested `<ul>` / `<ol>` using the AST — never collapse levels.
- Recognize numbered list types (Arabic, Roman, alphabetic) via `\levelnfcN` and emit `list-style-type`.

### 5.5 Tables
- Track `\trowd`, `\cellx`, `\cell`, `\row`, `\intbl`.
- Handle merged cells (`\clvmgf` / `\clvmrg` / `\clmgf` / `\clmrg`) → `rowspan` / `colspan`.
- Cell borders (`\clbrdr…`) → CSS border on `<td>`.
- Column widths from `\cellx` deltas.

### 5.6 Hyperlinks & fields
- `{\field {\*\fldinst HYPERLINK "url"} {\fldrslt display text}}` → `<a href="url">display text</a>`.
- `PAGE`, `DATE`, `TIME`, `TOC` fields: emit result text (`fldrslt`) into a `<span class="rtf-field" data-field="PAGE">…</span>`.

### 5.7 Bookmarks / anchors
- `{\*\bkmkstart name}…{\*\bkmkend name}` → wrap in `<a id="name">…</a>`.

### 5.8 Footnotes / endnotes
- `{\footnote …}` → emit numeric superscript link + collect footnote texts into an `<ol class="footnotes">` at bottom (when `RenderFootnotesAsList=true`).

### 5.9 Headers / footers
- Off by default; when `RenderHeadersFooters=true`, emit as `<header>` / `<footer>` blocks preceding / following body.

### 5.10 Unicode & special characters
- `\uN?` — parse the signed 16-bit code point and emit the char (skip N following ANSI fallback bytes per `\uc` count).
- `\'hh` — decode using the current codepage from `\ansicpgN` header (default 1252).
- `\~` → `&nbsp;`, `\-` → soft hyphen `&shy;`, `\_` → non-breaking hyphen `&#8209;`, `\bullet` → `•`, `\endash` → `–`, `\emdash` → `—`, `\lquote`/`\rquote`/`\ldblquote`/`\rdblquote`, `\tab` → tab char rendered via CSS or converted to spaces.

### 5.11 Sections & page breaks
- `\page` → `<div style="page-break-before:always"></div>`.
- `\sect` → new `<section>` if `EmitSections=true`, else same as `\page`.

### 5.12 Non-features / out of scope
- Text-boxes with absolute positioning (shape drawings).
- Encrypted RTF.
- OLE embedded objects (parse-through only, no rendering).

---

## 6. HTML Output Strategy

### 6.1 Structure
```html
<!DOCTYPE html>
<html lang="en">
  <head>
	<meta charset="utf-8">
	<style>/* generated styles when InlineStyles=false */</style>
  </head>
  <body>
	<!-- generated content -->
  </body>
</html>
```
When `EmitFullDocument=false`, only the body inner HTML is returned (usable in existing pages).

### 6.2 CSS
- Twips → `pt` or `px` conversion (1 twip = 1/1440 in ≈ 0.0521 mm; 20 twips = 1 pt).
- Emit percentage widths for tables when column widths sum ≈ `\trleft` + `\trright`.
- Use `white-space: pre-wrap` only for explicit `\pre`-like blocks — not globally as today.

### 6.3 Sanitization
- Default `HtmlSanitizationLevel.Basic`: encode all text via `WebUtility.HtmlEncode`; strip control chars.
- `.Strict`: additionally drop any URL scheme not in `{http, https, mailto, #}`.

---

## 7. Testing Strategy

### 7.1 Test project setup
- `RtfBridge.Tests` (xUnit) multi-targeting `net48; net8.0` — parser + emitter unit tests, all pure managed.
- `RtfBridge.Skia.Tests` (xUnit, `net8.0`) — WMF/EMF rasterization via SkiaSharp.
- Golden-file tests: `Fixtures/<category>/<name>.rtf` ⇄ `Fixtures/<category>/<name>.expected.html`.
- Golden files are byte-identical thanks to `DeterministicOutput=true`.

### 7.2 Fixture matrix (minimum)
| Category | Fixtures |
|---|---|
| Formatting | bold, italic, underline, strike, sub/super, color, font-size, mixed |
| Paragraph | alignment, indent, spacing, borders, keep-together |
| Lists | bulleted, numbered (arabic/roman/alpha), nested (3+ levels), mixed, numbering restart |
| Tables | simple, merged rows, merged cols, borderless, nested tables, wide |
| Images | png, jpeg, dib, wmf (with Skia), emf (with Skia), resized, multiple per paragraph |
| Unicode | `\uN?`, emoji, Cyrillic, CJK, RTL Hebrew/Arabic, surrogate pairs |
| Special chars | quotes, en/em dash, `\tab`, `\~`, `\-`, `\_`, `\'hh` per codepage 1250/1251/1252/932/936 |
| Hyperlinks | plain link, mailto, anchor, hyperlink inside list, hyperlink in table |
| Fields | HYPERLINK, PAGE, DATE, TOC, unknown-field |
| Sections | page break, section break, mixed |
| Headers/Footers | on & off toggling |
| Footnotes | multiple, footnote inside list |
| Bookmarks | forward reference, multiple |
| Regression | one file per historical bug reproduced |

### 7.3 Fuzz / robustness
- Malformed RTF (unbalanced braces, truncated pict, garbage bytes, deeply-nested groups, invalid `\uN`) → **must not throw uncaught**; must return best-effort HTML + warnings.
- Very large RTF (>50 MB) → streaming tokenizer; peak memory bounded (< 5× input size).
- Adversarial: zip-bomb-style `\bin1000000000 …` → parser rejects with a `RtfParseException` early.

### 7.4 CI
GitHub Actions matrix:
- Build **all TFMs** (`net462`, `netstandard2.0`, `net8.0`, `net10.0`).
- Run unit tests on `net48` (Windows) and `net8.0` (Linux + Windows + macOS).
- Run Skia tests on `net8.0` (Linux + Windows).
- Pack NuGet on tagged commits; publish to nuget.org via trusted publisher.

---

## 8. Public API — Compatibility Notes (v1 → v2)

Because HTML→RTF, HTML→XAML, and XAML→HTML paths are dropped (Q1), v2 is a **hard break** from v1 for those APIs.

| v1 method | v2 status |
|---|---|
| `ConvertRtfToHtml(string)` | ✅ kept — same signature, improved output |
| `ConvertRtfToHtml(string, RtfToHtmlOptions)` | ✅ new overload |
| `ConvertHtmlToRtf(string)` | ❌ removed |
| `ConvertHtmlToXaml(string)` | ❌ removed |
| `ConvertXamlToHtml(string)` | ❌ removed |
| `ConvertRtfToImage(string)` | ❌ removed (replaced by `ConvertRtfToHtmlDetailed().Images`) |

Consumers of the removed methods must either stay on v1.x or migrate to a different library (e.g., OpenXML / a dedicated HTML→RTF tool) — this is documented in `docs/migration-v1-to-v2.md`.

Major version bump: **1.x → 2.0.0**.

---

## 9. Packaging & Distribution

- **Main package** `RtfBridge` (v2.0.0) multi-targets `net462; netstandard2.0; net8.0; net10.0`.
- **Optional add-on** `RtfBridge.Skia` for WMF/EMF rendering.
- `GenerateDocumentationFile=true` → XML docs in package.
- `README.md`, `CHANGELOG.md`, `LICENSE` embedded in package.
- Deterministic build (`ContinuousIntegrationBuild=true` in CI).
- SourceLink + snupkg symbol packages.
- Strong-name signed (SNK checked into repo, no secrets).
- SBOM generated at pack time.

---

## 10. Documentation Deliverables

1. `README.md` — quick start, install, usage samples for Console / ASP.NET Core / WinForms / Azure Function / WPF.
2. `docs/rtf-support-matrix.md` — full feature coverage table (§5).
3. `docs/api-reference.md` — generated from XML docs.
4. `docs/migration-v1-to-v2.md` — mapping table for API and behavior changes; explicit note that HTML→RTF is dropped.
5. `samples/ConsoleSample` — minimal working example: reads `.rtf`, writes `.html`.
6. `samples/AspNetCoreSample` — endpoint `POST /convert` returning HTML.

---

## 11. Risks & Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Writing a full RTF parser from scratch is nontrivial | Schedule slip | Feature-drive incremental delivery in Phases 2–4; each phase releases a preview build that already beats v1 for the covered features |
| Consumers depending on v1 HTML→RTF break at v2 upgrade | Migration friction | Keep v1.x branch alive for security fixes for 12 months; document workaround in migration guide |
| WMF/EMF rendering across OS with Skia | Rendering fidelity may vary | Ship as opt-in add-on; document limitations; fall back to SVG placeholder without warnings terminating conversion |
| HTML output diff churn breaks downstream diff tests | User-side test failures | v2 explicitly documents a golden-output reset; `DeterministicOutput=true` guarantees stability going forward |
| `.NET 10` GA date uncertainty | Cannot ship immediately | Add `net10.0` TFM the day the SDK is available; until then ship `net462; netstandard2.0; net8.0` and cut v2.0.1 later to add it |
| Legacy consumers on `net461` or older | Cannot install v2 | Documented minimum: `net462`. Older consumers stay on v1 |

---

## 12. Delivery Phases

### Phase 0 — Foundations (1–2 days)
- Replace legacy `.csproj` with SDK-style multi-target (`net462; netstandard2.0; net8.0; net10.0`).
- Delete all files listed in §4.2.
- Add `.editorconfig`, enable `Nullable`, add `Nullable` and polyfill NuGet packages.
- Scaffold `RtfBridge.Tests` xUnit project + first passing "empty RTF → empty HTML body" test.
- Set up CI pipeline building all four TFMs on Windows, Linux, macOS.

### Phase 1 — RTF core parser MVP (1–2 weeks)
- Implement RTF tokenizer (control words, groups, hex escapes, `\uN?`, `\bin`, destination groups, optional `\*` destinations).
- Implement AST + parser producing `RtfDocument` covering **§5.1 (character formatting)** and **§5.2 (paragraph formatting)** and **§5.10 (unicode + special chars)**.
- Implement HTML emitter for those features.
- ~15 golden-file fixtures green.
- Ship as `2.0.0-alpha.1`.

### Phase 2 — Images, lists, tables, hyperlinks (2 weeks)
- Add PNG / JPEG / DIB passthrough decoders → base64 data URIs.
- Add WMF/EMF stub emitter (placeholder + warning; real rendering ships in `RtfBridge.Skia` in Phase 4).
- Add list parser (nested `\lsN` / `\ilvlN` with list-table awareness).
- Add table parser (`\trowd`, `\cellx`, `\cell`, `\row`, merged cells).
- Add hyperlink field parser.
- Ship as `2.0.0-beta.1`.

### Phase 3 — Sections, headers/footers, footnotes, bookmarks, fields (1 week)
- Parse `\page`, `\sect`, `\header*`, `\footer*`, `\footnote`, `\bkmkstart/end`, remaining `\field` types.
- Emit per configured options.
- Full fixture matrix green.
- Ship as `2.0.0-rc.1`.

### Phase 4 — Skia renderer + docs + GA (3–5 days)
- Ship `RtfBridge.Skia` package with `SkiaImageRenderer` for WMF/EMF.
- Finalize README, samples, migration guide, feature matrix.
- Fuzz suite + performance benchmarks in CI.
- Ship `RtfBridge 2.0.0` and `RtfBridge.Skia 2.0.0`.

### Phase 5 — Post-GA maintenance
- Add `net10.0` TFM the moment the SDK is GA (if not already in Phase 0).
- Address bug reports; keep golden fixtures.
- Consider `RtfBridge.AspNetCore` helper package (middleware, DI extensions).

---

## 13. Acceptance Criteria

- Library builds cleanly on `net462`, `netstandard2.0`, `net8.0`, and (upon GA) `net10.0`.
- **Zero** references to `PresentationFramework`, `PresentationCore`, `WindowsBase`, `System.Windows.*`, `System.Drawing.*`, `Microsoft.SqlServer.*` in the main package.
- Sample Console app runs on **Linux** and **macOS** and produces identical HTML byte-for-byte as on Windows (verified in CI).
- Golden-file test suite ≥ 95% green on all fixtures in §7.2.
- No hard-coded file paths (`C:\TempImg` etc.) anywhere in the code.
- No infinite-loop paths on malformed RTF (fuzz suite passes 10⁶ iterations).
- All non-ASCII Unicode fixtures round-trip correctly (Cyrillic, CJK, emoji, RTL).
- PNG, JPEG, DIB images embedded as base64 `data:` URIs.
- WMF/EMF images embedded as PNG base64 when `RtfBridge.Skia` is referenced; placeholder + warning otherwise.
- Public API fully documented via XML comments; NuGet package produced with `dotnet pack`.
- Deterministic HTML: two runs on the same input produce byte-identical output.

---

## 14. Locked answers to previously open questions

1. ✅ **HTML→RTF dropped in v2.** All HTML→RTF / HTML→XAML / XAML→HTML code deleted.
2. ✅ **Base64 inline is the only image mode.** No callbacks, no external store, no disk I/O.
3. ✅ **Multi-target all major .NET versions**: `net462; netstandard2.0; net8.0; net10.0` (no `-windows`).
4. ✅ **Deterministic HTML output** on by default (stable ordering, no timestamps).
5. ✅ **Single unified NuGet package** `RtfBridge`, plus optional `RtfBridge.Skia` for WMF/EMF rendering.

---

*Prepared for the RtfBridge v2 modernization initiative.*

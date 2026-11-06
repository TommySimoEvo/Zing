# Zing

A lightweight, fast Markdown viewer for Windows. Double-click a `.md` file and read it rendered — GitHub-style formatting, Mermaid diagrams, `[[wikilinks]]`, a folder tree and a table of contents — without opening an editor or a browser.

Zing is a viewer, not an editor: it reloads automatically when the file changes, so it works well next to your editor of choice.

> The user interface is currently in Italian.

## Download and install

Download `Zing-<version>-x64.msi` from the [Releases](https://github.com/TommySimoEvo/Zing/releases) page and run it.

- **No admin rights needed.** Zing installs for the current user only, in `%LOCALAPPDATA%\Programs\Zing`, adds a Start menu shortcut and registers itself for `.md` and `.markdown` files.
- **No prerequisites** on Windows 11: the .NET runtime is bundled. The only requirement is the [WebView2 runtime](https://developer.microsoft.com/microsoft-edge/webview2/), which ships with Windows 11 and is usually present on up-to-date Windows 10.
- **Make it the default app**: Windows does not let programs set themselves as default. Right-click a `.md` file → *Open with* → *Choose another app* → *Zing* → *Always*.
- **Upgrade**: install the new MSI over the old one.
- **Uninstall**: *Settings → Apps*. Preferences and cache in `%LOCALAPPDATA%\Zing` are left behind; delete the folder if you want a clean removal.
- **Unsigned installer**: the MSI is not code-signed, so Windows SmartScreen may show a warning ("Windows protected your PC" → *More info* → *Run anyway*).

## Features

- **GitHub-flavored Markdown**: tables, task lists, footnotes, emoji, auto-links. YAML front matter is shown in a collapsed "Metadata" block. Files without a BOM are read as UTF-8, falling back to Windows-1252 when they are not valid UTF-8.
- **Mermaid diagrams**, rendered offline with a bundled copy of Mermaid.
- **PlantUML diagrams**, rendered by the public `plantuml.com` server **only on request**: each diagram has a "show diagram" button, because rendering sends the diagram source to that server. You can choose to always allow it (see [Settings](#settings)).
- **Obsidian-style `[[wikilinks]]`**, including `[[note|alias]]`, `[[note#Heading]]` and `![[image.png]]`:
  - targets are resolved relative to the current file, then to each parent folder up to the project root (the nearest folder containing `.git` or `.obsidian`), then by file name anywhere under that root;
  - heading anchors match the heading text, so accents and numbering work;
  - `![[note]]` becomes a link (embedding a note's content is not supported);
  - unresolved links are shown in dashed red.
- **Folder tree** (`Ctrl+B`): Markdown files of the current project, loaded lazily and refreshed when files change. Change the root with the `…` button or the right-click menu.
- **Table of contents** (`T`) built from the headings.
- **Themes**: automatic (follows Windows), light, dark (`D` to cycle).
- **Live reload** when the file changes on disk, keeping the scroll position.
- **Links**: relative links between Markdown files and local images just work; web links open in your default browser.

### Keyboard shortcuts

| Key | Action |
|---|---|
| `Ctrl+O` | Open a file |
| `Ctrl+F` | Find in page |
| `Ctrl+B` | Show/hide the folder tree |
| `T` | Show/hide the table of contents |
| `D` | Cycle theme (auto / light / dark) |
| `Alt+←` | Back |
| `Ctrl+mouse wheel` | Zoom |
| `Ctrl+P` | Print |

## Security model

Zing treats every Markdown file as **untrusted**, since you may open files you did not write:

- **No scripts from the document**: a Content Security Policy with a per-page nonce blocks any script embedded in the Markdown. Only Zing's own scripts run.
- **No remote content**: remote images (`https://…`) are blocked, so opening a file cannot make requests to servers chosen by its author. Images can be local files, `data:` URIs or PlantUML (on request).
- **No silent launches**: web links open only on click. Other local files open with their associated app only on click, only for an allow-list of document types (Office, PDF, images, text) and only after a confirmation. Executables and other types are never launched — Zing offers to show them in File Explorer instead.
- **Contained wikilinks**: absolute paths, drive letters, UNC paths and `..` are rejected.
- **Network shares**: Zing reads only from the share (`\\server\share`) of the file you opened, not from others.

The `samples/security/` folder contains test documents for these cases.

## Settings

Preferences are stored in `%LOCALAPPDATA%\Zing\settings.json` and are set from the UI (window size, folder tree visibility and width, PlantUML choice). The PlantUML choice can be reset by hand:

```json
{ "PlantUml": "ask" }
```

Use `"always"` to render PlantUML diagrams without asking.

## Command line

```
Zing.exe [file.md]          Open a file
Zing.exe --register         Register Zing for .md/.markdown (current user, no MSI)
Zing.exe --unregister       Remove that registration
Zing.exe --selftest <file.md[#heading]> <output-prefix>
                            Render a file headlessly and write a JSON report plus screenshots
```

`--register` writes the same per-user registry keys as the MSI; use it if you run Zing from a folder instead of installing it. If the MSI is installed, uninstall from *Settings → Apps* instead.

## Build from source

Requirements: Windows, the [.NET 10 SDK](https://dotnet.microsoft.com/download). WiX 5 is pulled in as a NuGet SDK, nothing else to install.

```powershell
# Run from source
dotnet run --project src\Zing -- samples\test.md

# Build the MSI installer into artifacts\
.\build-installer.ps1
```

The installer is a self-contained publish (about 115 MB on disk, ~40 MB compressed MSI): larger than a framework-dependent build, but with no runtime to install.

### End-to-end test

```powershell
Zing.exe --selftest samples\test.md $env:TEMP\zt
```

This writes:

- `zt.json` — startup timings, page title, number of Mermaid diagrams rendered, PlantUML diagrams pending/loaded, resolved and missing wikilinks, scroll position, JavaScript errors and folder tree state;
- `zt.png` — screenshot of the page;
- `zt-window.png` — screenshot of the whole window (or `zt-screenshot-error.txt` if the screen cannot be captured).

Set `ZING_PLANTUML=always` to render PlantUML diagrams during a self-test without the prompt.

Test documents in `samples/security/`:

- `meta.md` — a meta refresh to `marker.bat` (must **not** create `%TEMP%\zing-pwned.txt`), plus a UNC image, a remote image and absolute/`..` wikilinks;
- `ansi.md` — a Windows-1252 file;
- `ancore.md` — heading anchors with accents and numbering.

## How it works

- **WinForms + WebView2**, with [Markdig](https://github.com/xoofx/markdig) for Markdown parsing.
- **Virtual host**: files are served from `https://zing.invalid/f/<path>` through WebView2's `WebResourceRequested`, read and rendered on a background thread. Relative links and the back button work with no extra code. The reserved `.invalid` TLD fails DNS resolution immediately; an earlier `.local` host triggered mDNS lookups that added about 2 seconds to every page load.
- **Wikilinks are converted before Markdig**, skipping fenced code blocks and inline code spans. Indented (4-space) code blocks are not skipped, because doing so would break wikilinks inside nested lists.
- **Native folder tree** (`TreeView` in a `SplitContainer`), so its state survives navigation between files; its theme follows the page.

| File | Responsibility |
|---|---|
| `src/Zing/MainForm.cs` | Window, WebView2 setup, navigation, link handling |
| `src/Zing/MarkdownRenderer.cs` | Markdown → HTML, front matter, encoding detection |
| `src/Zing/WikiLinks.cs` | Wikilink parsing and resolution |
| `src/Zing/LinkPolicy.cs` | What may be opened, and how |
| `src/Zing/LocalUrl.cs` | Mapping between file paths and the virtual host |
| `src/Zing/FolderTree.cs` | Folder tree panel |
| `src/Zing/FileAssociation.cs` | `--register` / `--unregister` |
| `src/Zing/assets/` | Page CSS/JS and the bundled Mermaid library |
| `installer/` | WiX 5 MSI project |

## License

[MIT](LICENSE). Bundled third-party components keep their own licenses: [Mermaid](https://github.com/mermaid-js/mermaid) (MIT), [Markdig](https://github.com/xoofx/markdig) (BSD-2-Clause), [WebView2 SDK](https://www.nuget.org/packages/Microsoft.Web.WebView2) (Microsoft license).

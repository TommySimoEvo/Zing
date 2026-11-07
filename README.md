# Zing

A lightweight, fast Markdown viewer for Windows. Double-click a `.md` file and read it rendered — GitHub-style formatting, Mermaid diagrams, `[[wikilinks]]`, a folder tree and a table of contents — without opening an editor or a browser. It also shows HTML pages and Mermaid and PlantUML diagram files, and keeps to one window: a file opened from Explorer replaces the one on screen.

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
- **Other file types**, listed in the tree and opened like Markdown files (live reload, back navigation):
  - **HTML** (`.html`, `.htm`): shown with its own styles, local images and fonts, but **none of its scripts run** and nothing remote is loaded (see [Security model](#security-model)). Pages that need JavaScript may look incomplete.
  - **Mermaid** (`.mmd`, `.mermaid`): the whole file is one diagram, rendered offline; an invalid diagram shows its source and the error.
  - **PlantUML** (`.puml`, `.plantuml`, `.pu`, `.iuml`): the whole file is one diagram, with the same "show diagram" consent as PlantUML blocks in Markdown.
- **One window**: a file opened from Explorer, *Open with* or the command line while Zing is running goes to the open window, which comes to the front; `Alt+←` returns to the previous file. Ctrl+click on a link still opens a new window.
- **Folder tree** (`Ctrl+B`): documents of the current project, loaded lazily and refreshed when files change. Go to the parent folder with the `↑` button, the right-click menu or `Alt+↑` (up to the drive or share root); change the root with the `…` button.
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
| `Alt+↑` | Folder tree: go to the parent folder (tree focused) |
| `T` | Show/hide the table of contents |
| `D` | Cycle theme (auto / light / dark) |
| `Alt+←` | Back |
| `Ctrl+mouse wheel` | Zoom |
| `Ctrl+P` | Print |

On an HTML page only `Ctrl+O`, `Ctrl+B`, `Ctrl+F`, `Ctrl+P`, `Alt+←` and `Ctrl+mouse wheel` apply: `T` and `D` belong to Zing's own page.

## Security model

Zing treats every Markdown file as **untrusted**, since you may open files you did not write:

- **No scripts from the document**: a Content Security Policy with a per-page nonce blocks any script embedded in the Markdown. Only Zing's own scripts run. An HTML file is served with `script-src 'none'`: no `<script>`, no event-handler attribute and no `javascript:` link runs, and forms, frames and objects are blocked.
- **No remote content**: remote images (`https://…`) are blocked, so opening a file cannot make requests to servers chosen by its author. Images can be local files, `data:` URIs or PlantUML (on request). Below the page, Zing's browser engine resolves no host name except `www.plantuml.com`, and Zing answers every other request itself, so not even a frame or a meta refresh opens a connection.
- **No silent launches**: web links open only on click. Other local files open with their associated app only on click, only for an allow-list of document types (Office, PDF, images, text) and only after a confirmation. Executables and other types are never launched — Zing offers to show them in File Explorer instead.
- **Contained wikilinks**: absolute paths, drive letters, UNC paths and `..` are rejected.
- **Network shares**: Zing reads only from the share (`\\server\share`) of the file you opened, not from others. The folder tree stops at the share root.
- **One window, one user**: a file reaches the open window over a named pipe that only the same Windows user can connect to, and only as a full path, exactly like a command-line argument.

The `samples/security/` folder contains test documents for these cases.

## Settings

Preferences are stored in `%LOCALAPPDATA%\Zing\settings.json` and are set from the UI (window size, folder tree visibility and width, PlantUML choice). The PlantUML choice can be reset by hand:

```json
{ "PlantUml": "ask" }
```

Use `"always"` to render PlantUML diagrams without asking.

## Command line

```
Zing.exe [file]             Open a file (in the open Zing window, if there is one)
Zing.exe -- <file>          Open a file in a new window
Zing.exe --register         Register Zing for .md/.markdown (current user, no MSI)
Zing.exe --unregister       Remove that registration
Zing.exe --selftest <file[#heading]> <output-prefix>
                            Render a file headlessly and write a JSON report plus screenshots
```

Started with no file while a window is open, Zing brings that window to the front. If the open window does not answer within about 3 seconds, a new one opens instead, so a file is never lost.

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

### Unit tests

```powershell
dotnet run --project tests\Zing.Tests
```

A plain console runner (no test framework): one line per failure, exit code 0 only when every check passes. It covers file-type classification, the parent-folder rule, HTML encoding detection and policy, diagram pages, window titles, and the single-window hand-off over a real named pipe.

### End-to-end test

```powershell
Zing.exe --selftest samples\test.md $env:TEMP\zt
```

This writes:

- `zt.json` — the document `kind` (`markdown`, `html`, `mermaid`, `plantuml`), startup timings, page title, number of Mermaid diagrams rendered or invalid, PlantUML diagrams pending/loaded, resolved and missing wikilinks, scroll position, JavaScript errors, folder tree state, and `ctrlBToggles` (a `Ctrl+B` dispatched twice inside the page must reach the window twice). For an HTML file the page fields are measured by Zing from outside the page: title, images and how many loaded, links;
- `zt.png` — screenshot of the page;
- `zt-window.png` — screenshot of the whole window (or `zt-screenshot-error.txt` if the screen cannot be captured).

Set `ZING_PLANTUML=always` to render PlantUML diagrams during a self-test without the prompt, and `ZING_THEME=light` or `dark` to force the page theme without touching the one you chose.

`samples/types/` has one file per document type (`page.html`, `diagram.mmd`, `broken.mmd`, `diagram.puml`). Test documents in `samples/security/`:

- `meta.md` — a meta refresh to `marker.bat` (must **not** create `%TEMP%\zing-pwned.txt`), plus a UNC image, a remote image and absolute/`..` wikilinks;
- `script.html` — inline, external and event-handler scripts, a `javascript:` link, a meta refresh to `marker.bat`, remote stylesheet, image, frame, object and form: the title must stay "Script bloccati" and nothing may be fetched or launched;
- `ansi.md` — a Windows-1252 file;
- `ancore.md` — heading anchors with accents and numbering.

## How it works

- **WinForms + WebView2**, with [Markdig](https://github.com/xoofx/markdig) for Markdown parsing.
- **Virtual host**: files are served from `https://zing.invalid/f/<path>` through WebView2's `WebResourceRequested`, read and rendered on a background thread. Relative links and the back button work with no extra code. The reserved `.invalid` TLD fails DNS resolution immediately; an earlier `.local` host triggered mDNS lookups that added about 2 seconds to every page load.
- **Wikilinks are converted before Markdig**, skipping fenced code blocks and inline code spans. Indented (4-space) code blocks are not skipped, because doing so would break wikilinks inside nested lists.
- **Native folder tree** (`TreeView` in a `SplitContainer`), so its state survives navigation between files; its theme follows the page.
- **Document kinds**: Markdown, Mermaid and PlantUML files become Zing pages (a diagram file is a single code block of its language, rendered by the same `viewer.js`); an HTML file is served as it is, under its own script-free policy. Design decisions are in [docs/decision-log.md](docs/decision-log.md).
- **One window**: the first Zing of a session owns a named mutex and listens on a per-user named pipe; later launches hand over their file and exit.

| File | Responsibility |
|---|---|
| `src/Zing/MainForm.cs` | Window, WebView2 setup, navigation, link handling |
| `src/Zing/MarkdownRenderer.cs` | Markdown and diagram files → HTML, front matter, encoding detection, page policies |
| `src/Zing/SingleInstance.cs` | One window: mutex, named pipe, hand-off messages |
| `src/Zing/WikiLinks.cs` | Wikilink parsing and resolution |
| `src/Zing/LinkPolicy.cs` | What may be opened, and how |
| `src/Zing/LocalUrl.cs` | Mapping between file paths and the virtual host, document kinds |
| `src/Zing/FolderTree.cs` | Folder tree panel |
| `src/Zing/FileAssociation.cs` | `--register` / `--unregister` |
| `src/Zing/assets/` | Page CSS/JS and the bundled Mermaid library |
| `tests/Zing.Tests/` | Unit tests (plain console runner) |
| `installer/` | WiX 5 MSI project |

## License

[MIT](LICENSE). Bundled third-party components keep their own licenses: [Mermaid](https://github.com/mermaid-js/mermaid) (MIT), [Markdig](https://github.com/xoofx/markdig) (BSD-2-Clause), [WebView2 SDK](https://www.nuget.org/packages/Microsoft.Web.WebView2) (Microsoft license).

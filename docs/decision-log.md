# Decision log

Architecture decisions for Zing, newest last. Each records the context, the
options considered, the decision and its consequences.

## ADR-001 — One window through a named mutex and a per-user named pipe

Date: 2026-11-07. Status: accepted.

**Context.** Opening a file from Explorer while Zing is running started a
second window each time. The file must go to the open window instead, and a
local channel that accepts file paths must not let another user, or a
malicious document, make Zing open anything.

**Options.**
1. Named mutex + named pipe (standard library). The first instance owns
   `Local\Zing-<SID>-<session>` and serves a pipe of the same name; a later
   launch connects, sends the full path, waits for an acknowledgement and
   exits.
2. Window messages (`WM_COPYDATA` to a window found by class/title).
   Simple, but any process on the desktop can send them, and finding the
   window by title is fragile.
3. A single-instance library or the Visual Basic `WindowsFormsApplicationBase`.
   Extra dependency (1) or a heavyweight base class for one feature (2).

**Decision.** Option 1. Both ends use `PipeOptions.CurrentUserOnly`: the
server's pipe admits only the same user, and the client refuses a pipe
created by another user (who could otherwise squat the name). The message is
a length-prefixed UTF-8 `open\n<full path>` or `activate`; anything else,
a relative path, invalid path characters or a body over 64 KB is dropped
without an acknowledgement. The launch waits at most 3 seconds and then
opens its own window, so a hung or closing window never loses a file. A
window opened with `--` (Ctrl+click on a link) is a deliberate second window
and neither sends nor serves. `--selftest`, `--register` and `--unregister`
never take part.

**Consequences.** A second launch exits in about 150 ms without starting
WebView2. If the first window closes, the next Explorer launch becomes the
new first window; windows opened with `--` do not take over. Another user
squatting the pipe name only causes a 3-second delay and a separate window.

## ADR-002 — HTML files shown as they are, without any script

Date: 2026-11-07. Status: accepted (owner's answer 1 at CHARTER-001 approval).

**Context.** HTML files must be viewable, and Zing treats every document as
untrusted. Running the page's JavaScript would let a downloaded file do
anything a web page can inside Zing, including calling
`chrome.webview.postMessage`.

**Options.**
1. Serve the file unchanged with a Content Security Policy that forbids all
   scripts and every remote origin.
2. Sanitise the HTML (strip scripts, handlers, remote URLs). A parser and a
   rule set to maintain, and a sanitiser missing one case is a hole; the CSP
   is enforced by the browser engine.
3. Run scripts in a sandboxed frame. Pages work fully, but the untrusted
   code runs.

**Decision.** Option 1: `default-src 'none'; script-src 'none'`, styles,
images, fonts and media only from Zing's own origin (`'unsafe-inline'`
styles allowed, `data:`/`blob:` images and fonts), and `connect-src`,
`frame-src`, `object-src`, `base-uri`, `form-action` all `'none'`. Links go
through the existing link policy, so a meta refresh or any navigation not
started by a click never reaches the browser or the shell. The encoding is
the page's own (BOM or `<meta charset>`), UTF-8 otherwise. Zing does not
inject its toolbar, table of contents or theme. `Ctrl+O` and `Ctrl+B` are
kept by a small script WebView2 runs itself on Zing's HTML pages
(`AddScriptToExecuteOnDocumentCreatedAsync`, outside the page's CSP), which
posts the same two commands `viewer.js` posts.

**Consequences.** Pages that build their content with JavaScript look
incomplete; this was accepted at approval. Nothing in an HTML file can
reach the network or launch a program.

## ADR-003 — Unit tests in a plain console project, no test framework

Date: 2026-11-07. Status: accepted.

**Context.** The workspace definition of done requires automated tests; the
project had only `--selftest`. A test framework (xUnit, NUnit, MSTest) is a
third-party dependency, which needs an ADR approved by the owner.

**Options.** A framework (richer reporting, IDE integration) or a console
runner with a few assertion helpers.

**Decision.** `tests/Zing.Tests`, a console project referencing Zing
(`InternalsVisibleTo`), run with `dotnet run --project tests\Zing.Tests`,
exit code 0 only when every check passes. End-to-end behaviour stays with
`--selftest` and the black-box acceptance checks.

**Consequences.** No dependency to approve or update. Should the suite grow
large enough that a framework pays for itself, that is a new ADR for the
owner.

## ADR-004 — WebView2's WPF reference dropped at build time

Date: 2026-11-07. Status: accepted.

**Context.** The WebView2 package references its WPF control in every
project, and that assembly's dependency on WindowsBase 5.0 conflicts with
the WindowsBase 4.0 reference of a WinForms .NET 10 project: warning
MSB3277 on every build. The definition of done requires zero warnings.

**Decision.** A target in `Directory.Build.targets` removes the
`Microsoft.Web.WebView2.Wpf` reference before assembly resolution, for Zing
and every project that references it. Zing never uses WPF.

**Consequences.** Zero-warning builds. If a future WebView2 package stops
adding that reference, the target simply matches nothing.

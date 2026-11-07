using System.Text;
using Zing;

namespace Zing.Tests;

/// <summary>Unit and integration checks for Zing's pure logic. Prints one line per failure.</summary>
internal static class Program
{
    private static int _passed, _failed;

    [STAThread]
    private static int Main()
    {
        DocumentKinds();
        ParentFolder();
        HtmlCharset();
        HtmlPolicy();
        DiagramPage();
        WindowTitle();
        HandOffMessages();
        HandOffChannel();

        Console.WriteLine($"{_passed} passed, {_failed} failed");
        return _failed == 0 ? 0 : 1;
    }

    private static void Check(string name, bool ok)
    {
        if (ok) _passed++;
        else { _failed++; Console.WriteLine("FAIL " + name); }
    }

    private static void Equal<T>(string name, T expected, T actual)
    {
        bool ok = EqualityComparer<T>.Default.Equals(expected, actual);
        Check(name + $" (expected {expected}, got {actual})", ok);
    }

    // ---------- FA-001 §1: document families ----------

    private static void DocumentKinds()
    {
        foreach (string f in new[] { "a.md", "a.markdown", "a.mdown", "a.mkd", "A.MD" })
            Equal("kind " + f, DocumentKind.Markdown, LocalUrl.KindOf(f));
        foreach (string f in new[] { "a.html", "a.htm", "A.HTML", @"C:\x y\page.Htm" })
            Equal("kind " + f, DocumentKind.Html, LocalUrl.KindOf(f));
        foreach (string f in new[] { "a.mmd", "a.mermaid", "A.MMD" })
            Equal("kind " + f, DocumentKind.Mermaid, LocalUrl.KindOf(f));
        foreach (string f in new[] { "a.puml", "a.plantuml", "a.pu", "a.iuml", "A.PUML" })
            Equal("kind " + f, DocumentKind.PlantUml, LocalUrl.KindOf(f));
        foreach (string f in new[] { "a.txt", "a.bat", "a.exe", "a", "a.md.bak", "a.xhtml", "a.html.lnk", @"folder.md\x" })
            Check("not a document: " + f, !LocalUrl.IsDocument(f));

        Check("every dialog extension is a document", LocalUrl.DocumentExtensions.All(e => LocalUrl.IsDocument("x" + e)));
        Equal("dialog lists 12 extensions", 12, LocalUrl.DocumentExtensions.Length);
        Check("markdown only for IsMarkdown", LocalUrl.IsMarkdown("a.md") && !LocalUrl.IsMarkdown("a.html") && !LocalUrl.IsMarkdown("a.mmd"));
        Check("documents are browser-viewable", LocalUrl.IsBrowserViewable("a.puml") && LocalUrl.IsBrowserViewable("a.htm"));
        Check("executables are not browser-viewable", !LocalUrl.IsBrowserViewable("a.bat") && !LocalUrl.IsBrowserViewable("a.exe"));
    }

    // ---------- FA-001 §3: parent folder ----------

    private static void ParentFolder()
    {
        Equal("parent of C:\\", null, FolderTree.ParentOf(@"C:\"));
        Equal("parent of C:\\dev", @"C:\", FolderTree.ParentOf(@"C:\dev"));
        Equal("parent of C:\\dev\\zing\\", @"C:\dev", FolderTree.ParentOf(@"C:\dev\zing\"));
        Equal("parent of share root", null, FolderTree.ParentOf(@"\\server\share"));
        Equal("parent of share root with slash", null, FolderTree.ParentOf(@"\\server\share\"));
        Equal("parent inside share", @"\\server\share", FolderTree.ParentOf(@"\\server\share\docs"));
        Equal("parent deep inside share", @"\\server\share\docs", FolderTree.ParentOf(@"\\server\share\docs\a"));
    }

    // ---------- FA-001 R1.10: HTML encoding ----------

    private static void HtmlCharset()
    {
        static byte[] B(string s) => Encoding.Latin1.GetBytes(s);
        Check("meta charset", MarkdownRenderer.DeclaresCharset(B("<!doctype html><meta charset=\"windows-1252\"><p>x")));
        Check("meta charset upper case", MarkdownRenderer.DeclaresCharset(B("<HTML><HEAD><META CHARSET=UTF-8>")));
        Check("http-equiv content-type", MarkdownRenderer.DeclaresCharset(B("<meta http-equiv=\"Content-Type\" content=\"text/html; charset=iso-8859-1\">")));
        Check("UTF-8 BOM", MarkdownRenderer.DeclaresCharset(new byte[] { 0xEF, 0xBB, 0xBF, (byte)'<' }));
        Check("UTF-16 BOM", MarkdownRenderer.DeclaresCharset(new byte[] { 0xFF, 0xFE, (byte)'<', 0 }));
        Check("no declaration", !MarkdownRenderer.DeclaresCharset(B("<!doctype html><title>x</title><p>charset= in text")));
        Check("declaration after 1024 bytes ignored", !MarkdownRenderer.DeclaresCharset(B(new string(' ', 1100) + "<meta charset=utf-8>")));
        Check("empty file", !MarkdownRenderer.DeclaresCharset(ReadOnlySpan<byte>.Empty));
    }

    // ---------- FA-001 R1.6–R1.7: no script, nothing remote ----------

    private static void HtmlPolicy()
    {
        string csp = MarkdownRenderer.HtmlFileContentSecurityPolicy;
        Check("script-src 'none'", csp.Contains("script-src 'none'"));
        Check("default-src 'none'", csp.StartsWith("default-src 'none'"));
        foreach (string d in new[] { "connect-src 'none'", "frame-src 'none'", "object-src 'none'", "base-uri 'none'", "form-action 'none'" })
            Check("policy has " + d, csp.Contains(d));
        Check("no nonce in HTML policy", !csp.Contains("nonce"));
        Check("no unsafe-eval", !csp.Contains("unsafe-eval"));
        // the only host is Zing's own origin: nothing remote, not even plantuml.com
        var hosts = csp.Split(' ', ';').Where(t => t.Contains("://")).Distinct().ToList();
        Check("only Zing's origin in the policy: " + string.Join(",", hosts), hosts.All(h => h == LocalUrl.Origin));
    }

    // ---------- FA-001 R1.12–R1.15: diagram files ----------

    private static void DiagramPage()
    {
        string dir = Path.Combine(Path.GetTempPath(), "zing-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string mmd = Path.Combine(dir, "d.mmd");
            File.WriteAllText(mmd, "flowchart LR\r\n  A[\"<script>alert(1)</script>\"] --> B & C\r\n");
            var (html, nonce) = MarkdownRenderer.RenderDiagramPage(mmd, DocumentKind.Mermaid, "ask");
            Check("mermaid code block", html.Contains("<pre><code class=\"language-mermaid\">flowchart LR\n"));
            Check("mermaid source encoded", html.Contains("&lt;script&gt;alert(1)&lt;/script&gt;") && !html.Contains("<script>alert"));
            Check("ampersand encoded", html.Contains("B &amp; C"));
            Check("mermaid library loaded with nonce", html.Contains($"nonce=\"{nonce}\" src=\"{LocalUrl.AppPrefix}mermaid.min.js\""));
            Check("viewer loaded", html.Contains("viewer.js"));
            Check("title is file name", html.Contains("<title>d.mmd</title>"));

            string puml = Path.Combine(dir, "d.puml");
            File.WriteAllText(puml, "@startuml\nA -> B\n@enduml\n");
            var (phtml, _) = MarkdownRenderer.RenderDiagramPage(puml, DocumentKind.PlantUml, "ask");
            Check("plantuml code block", phtml.Contains("<pre><code class=\"language-plantuml\">@startuml\nA -&gt; B\n@enduml</code></pre>"));
            Check("plantuml asks by default", phtml.Contains("data-plantuml=\"ask\""));
            Check("plantuml page has no mermaid", !phtml.Contains("mermaid.min.js"));
            var (always, _) = MarkdownRenderer.RenderDiagramPage(puml, DocumentKind.PlantUml, "always");
            Check("plantuml always", always.Contains("data-plantuml=\"always\""));

            string ansi = Path.Combine(dir, "a.mmd");
            File.WriteAllBytes(ansi, Encoding.GetEncoding(1252).GetBytes("flowchart LR\n  A[Città] --> B\n"));
            Check("Windows-1252 diagram decoded", MarkdownRenderer.RenderDiagramPage(ansi, DocumentKind.Mermaid, "ask").Html.Contains("Citt&#224;"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    private static void WindowTitle()
    {
        Equal("page title", "Pagina", MainForm.WindowTitle(" Pagina ", "https://zing.invalid/f/C:/x/p.html"));
        Equal("no title: file name", "p.html", MainForm.WindowTitle("zing.invalid/f/C:/x/p.html", "https://zing.invalid/f/C:/x/p.html"));
        Equal("empty title: file name", "p.html", MainForm.WindowTitle("", "https://zing.invalid/f/C:/x/p.html"));
        Equal("no file: Zing", "Zing", MainForm.WindowTitle("", "about:blank"));
    }

    // ---------- FA-001 §2: single window ----------

    private static void HandOffMessages()
    {
        static byte[] Body(string? path) => SingleInstance.Encode(path)[4..];

        Check("activate round trip", SingleInstance.TryDecode(Body(null), out string? p0) && p0 is null);
        Check("path round trip", SingleInstance.TryDecode(Body(@"C:\dev\città\a b.md"), out string? p1) && p1 == @"C:\dev\città\a b.md");
        Check("UNC round trip", SingleInstance.TryDecode(Body(@"\\server\share\a.md"), out string? p2) && p2 == @"\\server\share\a.md");
        Equal("length prefix", Encoding.UTF8.GetByteCount("activate"), BitConverter.ToInt32(SingleInstance.Encode(null), 0));

        foreach (string bad in new[] { "a.md", @"dir\a.md", @"C:a.md", @"\a.md", "C:\\a\0.md", "C:\\a|b.md", "" })
            Check("rejects path " + bad.Replace("\0", "\\0"), !SingleInstance.TryDecode(Encoding.UTF8.GetBytes("open\n" + bad), out _));
        Check("rejects two lines", !SingleInstance.TryDecode(Encoding.UTF8.GetBytes("open\nC:\\a.md\nC:\\b.md"), out _));
        Check("rejects unknown command", !SingleInstance.TryDecode(Encoding.UTF8.GetBytes("run\nC:\\a.exe"), out _));
        Check("rejects empty", !SingleInstance.TryDecode(ReadOnlySpan<byte>.Empty, out _));
        Check("rejects invalid UTF-8", !SingleInstance.TryDecode(new byte[] { 0x6F, 0x70, 0xFF, 0xFE }, out _));
        Check("rejects over-long path", !SingleInstance.TryDecode(Encoding.UTF8.GetBytes("open\nC:\\" + new string('a', 33_000)), out _));
    }

    private static void HandOffChannel()
    {
        string channel = "Zing-test-" + Guid.NewGuid().ToString("N");

        var clock = System.Diagnostics.Stopwatch.StartNew();
        Check("no primary: send fails", !SingleInstance.TrySend(channel, @"C:\a.md", TimeSpan.FromMilliseconds(600)));
        Check("no primary: gives up within budget", clock.ElapsedMilliseconds < 2500);

        using var primary = SingleInstance.TryBecomePrimary(channel);
        Check("first becomes primary", primary is not null);
        var second = SingleInstance.TryBecomePrimary(channel);
        Check("second is not primary", second is null);
        second?.Dispose();

        var received = new System.Collections.Concurrent.BlockingCollection<string?>();
        bool accept = true;
        primary!.Listen(path => { if (!accept) return false; received.Add(path); return true; });

        Check("hand-off confirmed", SingleInstance.TrySend(channel, @"C:\dev\a.md", TimeSpan.FromSeconds(3)));
        Check("path delivered", received.TryTake(out string? got, 2000) && got == @"C:\dev\a.md");
        Check("activate confirmed", SingleInstance.TrySend(channel, null, TimeSpan.FromSeconds(3)));
        Check("activate delivered", received.TryTake(out string? none, 2000) && none is null);

        // a garbage client gets no confirmation and does not stop the server
        using (var raw = new System.IO.Pipes.NamedPipeClientStream(".", channel, System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.PipeOptions.CurrentUserOnly))
        {
            raw.Connect(2000);
            byte[] junk = SingleInstance.Encode(null);
            junk[0] = 0xFF; junk[1] = 0xFF; junk[2] = 0xFF; junk[3] = 0x7F; // claims 2 GB
            var reply = new byte[1];
            int n;
            // the server drops the connection as soon as it reads the length: writing may already fail
            try { raw.Write(junk); n = raw.Read(reply, 0, 1); } catch (IOException) { n = 0; }
            Check("oversized message not confirmed", n == 0);
        }
        Check("server still serves after garbage", SingleInstance.TrySend(channel, @"C:\b.md", TimeSpan.FromSeconds(3)));
        received.TryTake(out _, 2000);

        accept = false;
        Check("window not ready: not confirmed", !SingleInstance.TrySend(channel, @"C:\c.md", TimeSpan.FromSeconds(1)));
        Check("window not ready: nothing delivered", !received.TryTake(out _, 300));
    }
}

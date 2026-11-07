using System.Net;
using System.Security.Cryptography;
using Markdig;
using Markdig.Extensions.Yaml;
using Markdig.Syntax;

namespace Zing;

internal static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()      // tabelle, task list, note a piè di pagina, id dei titoli, diagrammi, ...
        .UseYamlFrontMatter()
        .UseEmojiAndSmiley(enableSmileys: false)
        .Build();

    static MarkdownRenderer() => System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

    private static readonly System.Text.Encoding StrictUtf8 = new System.Text.UTF8Encoding(false, throwOnInvalidBytes: true);

    /// <summary>UTF-8 (con o senza BOM) e UTF-16 con BOM; se i byte non sono UTF-8 valido, Windows-1252.</summary>
    public static string ReadText(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 2 && (bytes[0] == 0xFF && bytes[1] == 0xFE || bytes[0] == 0xFE && bytes[1] == 0xFF))
            return File.ReadAllText(path); // BOM UTF-16: riconosciuto da ReadAllText
        int skip = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        try { return StrictUtf8.GetString(bytes, skip, bytes.Length - skip); }
        catch (System.Text.DecoderFallbackException) { return System.Text.Encoding.GetEncoding(1252).GetString(bytes); }
    }

    /// <summary>
    /// Self-test only (ZING_THEME=light|dark): pages open in that theme instead of the one remembered
    /// in the viewer's storage, which is the user's and is not touched.
    /// </summary>
    public static string? ThemeOverride { get; set; }

    public static (string Html, string Nonce) RenderPage(string path, string plantUmlMode)
    {
        string source = ReadText(path);
        string md = WikiLinks.Preprocess(source, path);
        MarkdownDocument doc = Markdown.Parse(md, Pipeline);

        string frontMatter = "";
        if (doc.Descendants<YamlFrontMatterBlock>().FirstOrDefault() is { } yaml)
        {
            string yamlText = string.Join("\n", yaml.Lines.Lines.Take(yaml.Lines.Count).Select(l => l.ToString()));
            frontMatter = $"<details class=\"front-matter\"><summary>Metadati</summary><pre>{WebUtility.HtmlEncode(yamlText)}</pre></details>";
        }

        string body = doc.ToHtml(Pipeline);
        bool needsMermaid = body.Contains("class=\"mermaid\"") || body.Contains("language-mermaid");
        return Page(path, frontMatter + "\n" + body, needsMermaid, plantUmlMode);
    }

    /// <summary>
    /// A Mermaid or PlantUML file is one diagram: its source becomes a single code block of that
    /// language, which viewer.js renders exactly like a fenced block in Markdown.
    /// </summary>
    public static (string Html, string Nonce) RenderDiagramPage(string path, DocumentKind kind, string plantUmlMode)
    {
        string language = kind == DocumentKind.Mermaid ? "mermaid" : "plantuml";
        string source = ReadText(path).Replace("\r\n", "\n").Trim('\n');
        string body = $"<pre><code class=\"language-{language}\">{WebUtility.HtmlEncode(source)}</code></pre>";
        return Page(path, body, kind == DocumentKind.Mermaid, plantUmlMode);
    }

    private static (string Html, string Nonce) Page(string path, string content, bool needsMermaid, string plantUmlMode)
    {
        string nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        string title = WebUtility.HtmlEncode(Path.GetFileName(path));
        string mermaidTag = needsMermaid ? $"<script nonce=\"{nonce}\" src=\"{LocalUrl.AppPrefix}mermaid.min.js\"></script>" : "";
        string themeScript = ThemeOverride is "light" or "dark"
            ? $"document.documentElement.dataset.theme = '{ThemeOverride}';"
            : "document.documentElement.dataset.theme = (function () { try { return localStorage.getItem('mdv-theme') || 'auto' } catch (e) { return 'auto' } })();";

        string html = $$"""
            <!doctype html>
            <html lang="it" data-plantuml="{{(plantUmlMode == "always" ? "always" : "ask")}}">
            <head>
            <meta charset="utf-8">
            <title>{{title}}</title>
            <link rel="stylesheet" href="{{LocalUrl.AppPrefix}}viewer.css">
            <script nonce="{{nonce}}">{{themeScript}}</script>
            </head>
            <body>
            <div id="toolbar">
              <button id="btn-toc" title="Indice (T)">☰</button>
              <button id="btn-theme" title="Tema chiaro/scuro (D)">◐</button>
            </div>
            <nav id="toc" hidden></nav>
            <main id="content" class="markdown-body">
            {{content}}
            </main>
            {{mermaidTag}}
            <script nonce="{{nonce}}" src="{{LocalUrl.AppPrefix}}viewer.js"></script>
            </body>
            </html>
            """;
        return (html, nonce);
    }

    /// <summary>
    /// Policy for an HTML file shown as it is: its own look (inline and local styles, local images
    /// and fonts) and nothing else. No script runs, not even inline handlers or javascript: URLs, and
    /// nothing is fetched from outside the PC. Zing's own scripts are not injected either.
    /// </summary>
    public static readonly string HtmlFileContentSecurityPolicy =
        "default-src 'none'; script-src 'none'; " +
        $"style-src {LocalUrl.Origin} 'unsafe-inline'; " +
        $"img-src {LocalUrl.Origin} data: blob:; " +
        $"font-src {LocalUrl.Origin} data:; " +
        $"media-src {LocalUrl.Origin}; " +
        "connect-src 'none'; frame-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'";

    /// <summary>True when the first bytes of an HTML file declare its encoding (BOM or meta charset).</summary>
    public static bool DeclaresCharset(ReadOnlySpan<byte> head)
    {
        if (head.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }) || head.StartsWith(new byte[] { 0xFF, 0xFE }) || head.StartsWith(new byte[] { 0xFE, 0xFF }))
            return true;
        string text = System.Text.Encoding.Latin1.GetString(head[..Math.Min(head.Length, 1024)]);
        return System.Text.RegularExpressions.Regex.IsMatch(text, @"<meta\b[^>]*charset\s*=", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    public static string ContentSecurityPolicy(string nonce) =>
        "default-src 'none'; " +
        $"script-src 'nonce-{nonce}'; " +
        $"style-src {LocalUrl.Origin} 'unsafe-inline'; " +
        // Immagini solo locali, data:/blob: e il server PlantUML: un .md non può far partire richieste verso
        // server scelti da lui (tracciamento, o CSS che prova l'esistenza di file e il nome utente).
        $"img-src {LocalUrl.Origin} data: blob: https://{NetworkPolicy.PlantUmlHost}; " +
        $"font-src {LocalUrl.Origin} data:; " +
        "connect-src 'none'; frame-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'";
}

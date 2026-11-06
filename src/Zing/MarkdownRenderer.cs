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
        string nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        string title = WebUtility.HtmlEncode(Path.GetFileName(path));
        string mermaidTag = needsMermaid ? $"<script nonce=\"{nonce}\" src=\"{LocalUrl.AppPrefix}mermaid.min.js\"></script>" : "";

        string html = $$"""
            <!doctype html>
            <html lang="it" data-plantuml="{{(plantUmlMode == "always" ? "always" : "ask")}}">
            <head>
            <meta charset="utf-8">
            <title>{{title}}</title>
            <link rel="stylesheet" href="{{LocalUrl.AppPrefix}}viewer.css">
            <script nonce="{{nonce}}">document.documentElement.dataset.theme = (function () { try { return localStorage.getItem('mdv-theme') || 'auto' } catch (e) { return 'auto' } })();</script>
            </head>
            <body>
            <div id="toolbar">
              <button id="btn-toc" title="Indice (T)">☰</button>
              <button id="btn-theme" title="Tema chiaro/scuro (D)">◐</button>
            </div>
            <nav id="toc" hidden></nav>
            <main id="content" class="markdown-body">
            {{frontMatter}}
            {{body}}
            </main>
            {{mermaidTag}}
            <script nonce="{{nonce}}" src="{{LocalUrl.AppPrefix}}viewer.js"></script>
            </body>
            </html>
            """;
        return (html, nonce);
    }

    public static string ContentSecurityPolicy(string nonce) =>
        "default-src 'none'; " +
        $"script-src 'nonce-{nonce}'; " +
        $"style-src {LocalUrl.Origin} 'unsafe-inline'; " +
        // Immagini solo locali, data:/blob: e il server PlantUML: un .md non può far partire richieste verso
        // server scelti da lui (tracciamento, o CSS che prova l'esistenza di file e il nome utente).
        $"img-src {LocalUrl.Origin} data: blob: https://www.plantuml.com; " +
        $"font-src {LocalUrl.Origin} data:; " +
        "connect-src 'none'; frame-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'";
}

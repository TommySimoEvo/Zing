namespace Zing;

/// <summary>A file Zing displays itself, by family.</summary>
internal enum DocumentKind { None, Markdown, Html, Mermaid, PlantUml }

/// <summary>
/// Mappa i percorsi locali su URL del finto host https://zing.invalid/f/...,
/// così i link relativi tra file (../altro.md, img/x.png) si risolvono da soli.
/// </summary>
internal static class LocalUrl
{
    public const string Origin = "https://zing.invalid";
    public const string FilePrefix = Origin + "/f/";
    public const string AppPrefix = Origin + "/__app/";

    public static string FromPath(string fullPath)
    {
        string p = fullPath.Replace('\\', '/');
        // UNC: //server/share/... -> /f/UNC/server/share/...
        if (p.StartsWith("//")) p = "UNC/" + p[2..];
        return FilePrefix + string.Join("/", p.Split('/').Select(Uri.EscapeDataString));
    }

    /// <summary>Restituisce il percorso locale se l'URL è un /f/..., altrimenti null.</summary>
    public static string? ToPath(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme == Uri.UriSchemeFile) return uri.LocalPath;
        if (!url.StartsWith(FilePrefix, StringComparison.OrdinalIgnoreCase)) return null;
        // "/f/../x" is normalised by Uri to "/x": no longer under /f/
        if (!uri.AbsolutePath.StartsWith("/f/", StringComparison.OrdinalIgnoreCase)) return null;

        string rel = uri.AbsolutePath["/f/".Length..];
        string p = Uri.UnescapeDataString(rel);
        if (p.StartsWith("UNC/", StringComparison.OrdinalIgnoreCase)) p = "//" + p[4..];
        return p.Replace('/', '\\');
    }

    /// <summary>Extensions of every document family, for the open dialog filter.</summary>
    public static readonly string[] DocumentExtensions =
        [".md", ".markdown", ".mdown", ".mkd", ".html", ".htm", ".mmd", ".mermaid", ".puml", ".plantuml", ".pu", ".iuml"];

    public static DocumentKind KindOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".md" or ".markdown" or ".mdown" or ".mkd" => DocumentKind.Markdown,
        ".html" or ".htm" => DocumentKind.Html,
        ".mmd" or ".mermaid" => DocumentKind.Mermaid,
        ".puml" or ".plantuml" or ".pu" or ".iuml" => DocumentKind.PlantUml,
        _ => DocumentKind.None,
    };

    public static bool IsMarkdown(string path) => KindOf(path) == DocumentKind.Markdown;

    /// <summary>A file Zing displays itself: Markdown, HTML, Mermaid or PlantUML.</summary>
    public static bool IsDocument(string path) => KindOf(path) != DocumentKind.None;

    public static bool IsBrowserViewable(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return IsDocument(path) || ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".svg" or ".webp" or ".bmp" or ".txt";
    }

    public static string MimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".svg" => "image/svg+xml",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        ".ico" => "image/x-icon",
        ".css" => "text/css",
        ".js" => "text/javascript",
        ".woff" => "font/woff",
        ".woff2" => "font/woff2",
        ".ttf" => "font/ttf",
        ".otf" => "font/otf",
        ".txt" => "text/plain; charset=utf-8",
        _ => "application/octet-stream",
    };
}

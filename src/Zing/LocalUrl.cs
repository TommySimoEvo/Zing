namespace Zing;

/// <summary>
/// Mappa i percorsi locali su URL del finto host https://zing.invalid/f/...,
/// cosÃ¬ i link relativi tra file (../altro.md, img/x.png) si risolvono da soli.
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

    /// <summary>Restituisce il percorso locale se l'URL Ã¨ un /f/..., altrimenti null.</summary>
    public static string? ToPath(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme == Uri.UriSchemeFile) return uri.LocalPath;
        if (!url.StartsWith(FilePrefix, StringComparison.OrdinalIgnoreCase)) return null;

        string rel = uri.AbsolutePath["/f/".Length..];
        string p = Uri.UnescapeDataString(rel);
        if (p.StartsWith("UNC/", StringComparison.OrdinalIgnoreCase)) p = "//" + p[4..];
        return p.Replace('/', '\\');
    }

    public static bool IsMarkdown(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".md" or ".markdown" or ".mdown" or ".mkd";
    }

    public static bool IsBrowserViewable(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return IsMarkdown(path) || ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".svg" or ".webp" or ".bmp" or ".txt";
    }

    public static string MimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".svg" => "image/svg+xml",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        ".css" => "text/css",
        ".js" => "text/javascript",
        ".txt" => "text/plain; charset=utf-8",
        _ => "application/octet-stream",
    };
}

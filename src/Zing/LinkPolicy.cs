namespace Zing;

/// <summary>
/// Regole per i "punti di uscita" verso il sistema: i file .md possono venire da fonti non fidate,
/// quindi niente apertura di file con la shell senza clic e conferma, mai eseguibili, e niente
/// accesso a percorsi di rete diversi da quello del file aperto dall'utente.
/// </summary>
internal static class LinkPolicy
{
    /// <summary>Tipi che si possono aprire con l'app predefinita (dopo conferma). Tutto il resto no.</summary>
    private static readonly HashSet<string> OpenableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".odt", ".ods", ".odp", ".vsdx", ".drawio",
        ".csv", ".txt", ".log", ".json", ".xml", ".yaml", ".yml",
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".svg",
        ".msg", ".eml", ".zip",
    };

    public static bool IsOpenable(string path) => OpenableExtensions.Contains(Path.GetExtension(path));

    /// <summary>Percorso di rete (\\server\share) o di dispositivo (\\?\, \\.\).</summary>
    public static bool IsNetworkOrDevice(string path) => path.StartsWith(@"\\") || path.StartsWith("//");

    /// <summary>\\?\, \\.\ and their slash forms: the server name of a share never starts with ? or .</summary>
    private static bool IsDevice(string path) => path.Length >= 3 && path[2] is '?' or '.';

    /// <summary>
    /// An allow-list, not a block-list: a drive path (<c>C:\…</c>) is allowed; a network path only
    /// inside the share (\\server\share) of the file the user opened. Anything else — NT paths such
    /// as <c>\??\UNC\host\share</c>, device paths, relative or drive-relative paths — is refused,
    /// so a document cannot make Windows open an SMB connection to a host its author chose.
    /// </summary>
    public static bool IsPathAllowed(string path, string? trustedNetworkRoot)
    {
        if (path.IndexOf('\0') >= 0) return false;
        if (IsDrivePath(path)) return true;
        if (!IsNetworkOrDevice(path) || IsDevice(path) || trustedNetworkRoot is null) return false;
        return string.Equals(NetworkRoot(path), trustedNetworkRoot, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary><c>X:\</c> or <c>X:/</c> followed by anything: a fully qualified path on a drive letter.</summary>
    private static bool IsDrivePath(string path) =>
        path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/');

    /// <summary>\\server\share di un percorso di rete, altrimenti null.</summary>
    public static string? NetworkRoot(string path)
    {
        if (!IsNetworkOrDevice(path) || IsDevice(path)) return null;
        try { return Path.GetPathRoot(path.Replace('/', '\\'))?.TrimEnd('\\'); }
        catch (ArgumentException) { return null; }
    }
}

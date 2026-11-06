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

    private static bool IsDevice(string path) => path.StartsWith(@"\\?\") || path.StartsWith(@"\\.\");

    /// <summary>
    /// Un percorso locale è sempre ammesso; uno di rete solo se sta nella stessa condivisione
    /// (\\server\share) del file che l'utente ha aperto. Così un .md non fa partire connessioni
    /// SMB verso host scelti da lui.
    /// </summary>
    public static bool IsPathAllowed(string path, string? trustedNetworkRoot)
    {
        if (path.IndexOf('\0') >= 0) return false;
        if (!IsNetworkOrDevice(path)) return true;
        if (IsDevice(path) || trustedNetworkRoot is null) return false;
        return string.Equals(NetworkRoot(path), trustedNetworkRoot, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>\\server\share di un percorso di rete, altrimenti null.</summary>
    public static string? NetworkRoot(string path)
    {
        if (!IsNetworkOrDevice(path) || IsDevice(path)) return null;
        try { return Path.GetPathRoot(path.Replace('/', '\\'))?.TrimEnd('\\'); }
        catch (ArgumentException) { return null; }
    }
}

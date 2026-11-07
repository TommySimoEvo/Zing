using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Zing;

/// <summary>
/// Converte i [[wikilink]] (stile Obsidian/workbrain) in link Markdown standard
/// prima del parsing, saltando blocchi di codice e code span.
/// </summary>
internal static partial class WikiLinks
{
    // gruppo "code": code span `...`; gruppo "embed": "!" di ![[...]]; gruppo "target": contenuto
    [GeneratedRegex(@"(?<code>(`+)[^`].*?\1)|(?<embed>!?)\[\[(?<target>[^\[\]\r\n]+?)\]\]")]
    private static partial Regex InlineRegex();

    [GeneratedRegex(@"^\s{0,3}(`{3,}|~{3,})")]
    private static partial Regex FenceRegex();

    private static readonly string[] SkipDirs = ["node_modules", ".git", "bin", "obj", ".obsidian", "dist", ".vs", ".angular", ".idea"];

    public static string Preprocess(string markdown, string currentFile)
    {
        if (!markdown.Contains("[[")) return markdown;

        var cache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var sb = new StringBuilder(markdown.Length + 256);
        string? openFence = null;

        foreach (string line in markdown.Split('\n'))
        {
            var fence = FenceRegex().Match(line);
            if (fence.Success)
            {
                string marker = fence.Groups[1].Value;
                if (openFence is null) openFence = marker;
                // chiude solo una riga di soli caratteri di fence (```js dentro un blocco non lo chiude)
                else if (marker[0] == openFence[0] && marker.Length >= openFence.Length && line.Trim().Trim(marker[0]).Length == 0) openFence = null;
                sb.Append(line).Append('\n');
                continue;
            }

            if (openFence is not null || !line.Contains("[["))
            {
                sb.Append(line).Append('\n');
                continue;
            }

            sb.Append(InlineRegex().Replace(line, m => m.Groups["code"].Success
                ? m.Value
                : Convert(m.Groups["target"].Value, m.Groups["embed"].Value == "!", currentFile, cache)));
            sb.Append('\n');
        }

        sb.Length--; // rimuove l'ultimo \n aggiunto in più
        return sb.ToString();
    }

    private static string Convert(string raw, bool embed, string currentFile, Dictionary<string, string?> cache)
    {
        string target = raw, label = raw;
        int pipe = raw.IndexOf('|');
        if (pipe >= 0) { target = raw[..pipe].Trim(); label = raw[(pipe + 1)..].Trim(); }

        string anchor = "";
        int hash = target.IndexOf('#');
        if (hash >= 0) { anchor = target[hash..]; target = target[..hash].Trim(); }
        if (pipe < 0) label = raw.Trim();

        if (!cache.TryGetValue(target, out string? resolved))
            cache[target] = resolved = target.Length == 0 ? currentFile : Resolve(target, currentFile);

        if (resolved is null)
            return $"<span class=\"wikilink-missing\" title=\"File non trovato: {WebUtility.HtmlEncode(target)}\">{WebUtility.HtmlEncode(label)}</span>";

        // l'ancora porta il testo del titolo: viewer.js lo confronta con i titoli della pagina (accenti, numeri)
        string url = LocalUrl.FromPath(resolved) + (anchor.Length > 1 ? "#" + Uri.EscapeDataString(anchor[1..].Trim()) : "");
        string text = label.Replace("[", "\\[").Replace("]", "\\]");
        // ![[nota.md]] (embed di una nota) diventa un link: l'inclusione del contenuto non è supportata
        return embed && !LocalUrl.IsDocument(resolved) ? $"![{text}]({url})" : $"[{text}]({url})";
    }

    /// <summary>
    /// Risolve un target: prima relativo alla cartella del file e poi a ogni cartella antenata
    /// (così [[me/come-lavoro]] funziona da qualsiasi sottocartella del workbrain), infine per
    /// nome file dentro la radice del progetto (cartella con .git o .obsidian).
    /// </summary>
    public static string? Resolve(string target, string currentFile)
    {
        // niente percorsi assoluti, unità, UNC o risalite: un .md non fidato non deve poter sondare il disco
        if (target.Contains(':') || target.StartsWith("//") || target.StartsWith(@"\\") ||
            target.Replace('\\', '/').Split('/').Any(seg => seg.Trim() == ".."))
            return null;
        string rel = target.Replace('/', '\\').TrimStart('\\');
        bool hasExt = Path.HasExtension(rel) && !LooksLikeVersion(rel);
        string withMd = hasExt ? rel : rel + ".md";

        DirectoryInfo? dir = new FileInfo(currentFile).Directory;
        DirectoryInfo? projectRoot = null;
        for (var d = dir; d is not null; d = d.Parent)
        {
            foreach (string cand in new[] { withMd, rel })
            {
                string full = Path.Combine(d.FullName, cand);
                if (File.Exists(full)) return full;
            }
            if (projectRoot is null && IsProjectRoot(d))
            {
                projectRoot = d;
                break; // non risale oltre la radice del progetto
            }
        }

        var searchRoot = projectRoot ?? dir;
        return searchRoot is null ? null : FindByName(searchRoot, Path.GetFileName(withMd));
    }

    /// <summary>Prima cartella antenata (inclusa quella data) che contiene .git o .obsidian, o null.</summary>
    public static DirectoryInfo? FindProjectRoot(DirectoryInfo? dir)
    {
        for (var d = dir; d is not null; d = d.Parent)
            if (IsProjectRoot(d))
                return d;
        return null;
    }

    /// <summary>A folder with .obsidian, or with .git as a folder or as a file (git worktrees and submodules).</summary>
    private static bool IsProjectRoot(DirectoryInfo d)
    {
        string git = Path.Combine(d.FullName, ".git");
        return Directory.Exists(git) || File.Exists(git) || Directory.Exists(Path.Combine(d.FullName, ".obsidian"));
    }

    public static bool IsExcludedDir(string name) => SkipDirs.Contains(name, StringComparer.OrdinalIgnoreCase);

    private static bool LooksLikeVersion(string s) => Regex.IsMatch(Path.GetExtension(s), @"^\.\d+$");

    private static string? FindByName(DirectoryInfo root, string fileName)
    {
        var queue = new Queue<DirectoryInfo>();
        queue.Enqueue(root);
        int visited = 0;
        while (queue.Count > 0 && visited++ < 3000)
        {
            var d = queue.Dequeue();
            try
            {
                string candidate = Path.Combine(d.FullName, fileName);
                if (File.Exists(candidate)) return candidate;
                foreach (var sub in d.EnumerateDirectories())
                    if (!SkipDirs.Contains(sub.Name, StringComparer.OrdinalIgnoreCase) && !sub.Attributes.HasFlag(FileAttributes.ReparsePoint))
                        queue.Enqueue(sub);
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }
        return null;
    }
}

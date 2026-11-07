using Microsoft.Web.WebView2.Core;

namespace Zing;

/// <summary>
/// What Zing's pages may reach on the network: nothing but the PlantUML server, and that only for
/// diagram images (FA-001 R1.7). The Content-Security-Policy stops a page from loading remote
/// content, but not every connection Chromium makes on its own: an iframe's origin is preconnected
/// and a meta refresh is requested before Zing can cancel the navigation. Two guards close that gap
/// below the page: name resolution fails for every other host, and every request that reaches the
/// network layer is checked here.
/// </summary>
internal static class NetworkPolicy
{
    public const string PlantUmlHost = "www.plantuml.com";
    private const string PlantUmlImagePrefix = "https://" + PlantUmlHost + "/plantuml/";

    /// <summary>
    /// Chromium resolves no host name but the PlantUML server's, so no lookup, TCP handshake or
    /// request leaves the PC for any other host, whatever started it.
    /// </summary>
    public static readonly string BrowserArguments =
        $"--host-resolver-rules=\"MAP * ~NOTFOUND, EXCLUDE {PlantUmlHost}\"";

    /// <summary>Zing's own origin always; the PlantUML server only for images; nothing else.</summary>
    public static bool IsAllowed(string uri, CoreWebView2WebResourceContext context)
    {
        if (uri.StartsWith(LocalUrl.Origin + "/", StringComparison.OrdinalIgnoreCase)) return true;
        return context == CoreWebView2WebResourceContext.Image &&
               uri.StartsWith(PlantUmlImagePrefix, StringComparison.OrdinalIgnoreCase);
    }
}

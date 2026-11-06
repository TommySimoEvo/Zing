using System.Diagnostics;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Zing;

internal sealed class MainForm : Form
{
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    private readonly FolderTree _folderTree = new() { Dock = DockStyle.Fill };
    private readonly SplitContainer _split = new() { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, SplitterWidth = 4 };
    private readonly Settings _settings = Settings.Load();
    private readonly string _initialFile;
    private readonly string? _selfTestOutput;
    private readonly System.Windows.Forms.Timer _reloadTimer = new() { Interval = 300 };
    private FileSystemWatcher? _watcher;
    private string? _currentFile;
    private string? _trustedNetworkRoot;
    private bool _dark;

    // indicatori di caricamento: pannello all'avvio (WebView2 impiega ~0,6 s a partire) e barra sottile
    // in alto per le navigazioni che durano più di 200 ms
    private readonly Panel _startupOverlay = new() { Dock = DockStyle.Fill };
    private readonly Label _startupLabel = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
    private readonly ProgressBar _startupBar = new() { Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 25, Height = 4, Dock = DockStyle.Bottom };
    private readonly ProgressBar _navBar = new() { Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 25, Height = 3, Dock = DockStyle.Top, Visible = false };
    private readonly System.Windows.Forms.Timer _navDelay = new() { Interval = 200 };

    public MainForm(string file, string? selfTestOutput = null)
    {
        _initialFile = file;
        _selfTestOutput = selfTestOutput;
        Text = "Zing";
        Width = _settings.WindowWidth;
        Height = _settings.WindowHeight;
        if (_settings.Maximized && selfTestOutput is null) WindowState = FormWindowState.Maximized;
        StartPosition = FormStartPosition.WindowsDefaultLocation;
        KeyPreview = true;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { /* icona di default */ }

        _split.Panel1.Controls.Add(_folderTree);
        _split.Panel2.Controls.Add(_web);
        _split.Panel2.Controls.Add(_navBar);
        _startupLabel.Text = $"Apertura di {Path.GetFileName(file)}…";
        _startupLabel.Font = new Font(Font.FontFamily, 11f);
        var barHolder = new Panel { Dock = DockStyle.Bottom, Height = 4 };
        barHolder.Controls.Add(_startupBar);
        _startupOverlay.Controls.Add(_startupLabel);
        _startupOverlay.Controls.Add(barHolder);
        _split.Panel2.Controls.Add(_startupOverlay);
        _startupOverlay.BringToFront();
        _navDelay.Tick += (_, _) => { _navDelay.Stop(); _navBar.Visible = true; _navBar.BringToFront(); };
        Controls.Add(_split);
        ApplyTheme(_settings.Dark ?? SystemUsesDarkTheme());
        _folderTree.FileActivated += path =>
        {
            if (!string.Equals(path, _currentFile, StringComparison.OrdinalIgnoreCase)) Navigate(path);
        };

        _reloadTimer.Tick += (_, _) => { _reloadTimer.Stop(); _ = ReloadPreservingScrollAsync(); };
        Load += async (_, _) =>
        {
            Timing.Mark("form-load");
            _split.Panel1MinSize = LogicalToDeviceUnits(150);
            _split.SplitterDistance = Math.Clamp(_settings.TreeWidth, _split.Panel1MinSize, Math.Max(_split.Panel1MinSize, Width / 2));
            _split.Panel1Collapsed = !_settings.TreeVisible;
            await InitAsync();
        };
        FormClosing += (_, _) => SaveSettings();
    }

    private void SaveSettings()
    {
        if (_selfTestOutput is not null) return;
        bool treeVisible = !_split.Panel1Collapsed;
        int treeWidth = _split.SplitterDistance;
        bool maximized = WindowState == FormWindowState.Maximized;
        bool normal = WindowState == FormWindowState.Normal;
        int w = Width, h = Height;
        // rilegge il file e cambia solo i campi della finestra: non sovrascrive scelte fatte in altre finestre
        Settings.Update(s =>
        {
            s.TreeVisible = treeVisible;
            if (treeVisible) s.TreeWidth = treeWidth;
            s.Maximized = maximized;
            if (normal) { s.WindowWidth = w; s.WindowHeight = h; }
        });
    }

    private void ToggleTree()
    {
        _split.Panel1Collapsed = !_split.Panel1Collapsed;
        if (!_split.Panel1Collapsed && _currentFile is not null) _folderTree.Reveal(_currentFile);
    }

    private void ShowOpenDialog()
    {
        using var dlg = CreateOpenDialog(_currentFile);
        if (dlg.ShowDialog(this) == DialogResult.OK) Navigate(dlg.FileName);
    }

    /// <summary>Scorciatoie quando il fuoco è sull'albero (dentro la pagina le gestisce viewer.js).</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.B) { ToggleTree(); e.Handled = e.SuppressKeyPress = true; }
        else if (e.Control && e.KeyCode == Keys.O) { ShowOpenDialog(); e.Handled = e.SuppressKeyPress = true; }
        base.OnKeyDown(e);
    }

    private void ApplyTheme(bool dark)
    {
        _dark = dark;
        _folderTree.ApplyTheme(dark);
        _split.BackColor = dark ? Color.FromArgb(0x3d, 0x44, 0x4d) : Color.FromArgb(0xd1, 0xd9, 0xe0);
        Color pageBack = dark ? Color.FromArgb(0x0d, 0x11, 0x17) : Color.White;
        _web.DefaultBackgroundColor = pageBack;   // niente lampo bianco tra una pagina e l'altra
        _startupOverlay.BackColor = pageBack;
        _startupLabel.ForeColor = dark ? Color.FromArgb(0x91, 0x98, 0xa1) : Color.FromArgb(0x59, 0x63, 0x6e);
        ApplyTitleBarTheme();
    }

    private void ApplyTitleBarTheme()
    {
        int value = _dark ? 1 : 0;
        if (IsHandleCreated) DwmSetWindowAttribute(Handle, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref value, sizeof(int));
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyTitleBarTheme();
    }

    private static bool SystemUsesDarkTheme()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
    }

    /// <summary>Il tema della pagina è cambiato: aggiorna finestra e albero e lo ricorda per il prossimo avvio.</summary>
    private void OnPageTheme(bool dark)
    {
        if (dark != _dark) ApplyTheme(dark);
        if (_selfTestOutput is null && _settings.Dark != dark)
        {
            _settings.Dark = dark;
            Settings.Update(s => s.Dark = dark);
        }
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static OpenFileDialog CreateOpenDialog(string? currentFile) => new()
    {
        Title = "Apri file Markdown",
        Filter = "Markdown (*.md;*.markdown)|*.md;*.markdown;*.mdown;*.mkd|Tutti i file (*.*)|*.*",
        InitialDirectory = currentFile is null ? null : Path.GetDirectoryName(currentFile),
    };

    private async Task InitAsync()
    {
        string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Zing", "WebView2");
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: dataDir);
            Timing.Mark("webview-env");
            await _web.EnsureCoreWebView2Async(env);
            Timing.Mark("webview-ready");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Impossibile avviare WebView2: " + ex.Message, "Zing", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
            return;
        }

        var core = _web.CoreWebView2;
        core.Settings.IsStatusBarEnabled = true;
        core.Settings.AreDevToolsEnabled = Debugger.IsAttached;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.AddWebResourceRequestedFilter(LocalUrl.Origin + "/*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += OnWebResourceRequested;
        core.NavigationStarting += OnNavigationStarting;
        core.NavigationStarting += (_, e) => { if (!e.Cancel) { _navDelay.Stop(); _navDelay.Start(); } };
        core.NewWindowRequested += OnNewWindowRequested;
        core.NavigationCompleted += OnNavigationCompleted;
        core.DocumentTitleChanged += (_, _) => Text = core.DocumentTitle + " - Zing";
        core.WebMessageReceived += OnWebMessageReceived;

        // "file.md#Titolo" (usato dal self-test per provare le ancore)
        int hash = _initialFile.LastIndexOf('#');
        if (!File.Exists(_initialFile) && hash > 0 && File.Exists(_initialFile[..hash]))
        {
            Navigate(_initialFile[..hash]);
            core.Navigate(LocalUrl.FromPath(_initialFile[..hash]) + "#" + Uri.EscapeDataString(_initialFile[(hash + 1)..]));
        }
        else Navigate(_initialFile);
    }

    /// <summary>Apertura decisa dall'utente (riga di comando, Ctrl+O, albero, trascinamento): la sua condivisione di rete diventa fidata.</summary>
    private void Navigate(string path)
    {
        if (LinkPolicy.NetworkRoot(path) is { } root) _trustedNetworkRoot = root;
        _web.CoreWebView2.Navigate(LocalUrl.FromPath(path));
    }

    /// <summary>"always" = diagrammi PlantUML inviati a plantuml.com senza chiedere; "ask" = su richiesta.</summary>
    private string PlantUmlMode =>
        _selfTestOutput is not null && Environment.GetEnvironmentVariable("ZING_PLANTUML") == "always" ? "always"
        : _settings.PlantUml == "always" ? "always" : "ask";

    /// <summary>Comandi inviati da viewer.js (le scorciatoie da tastiera nascono dentro la pagina).</summary>
    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!e.Source.StartsWith(LocalUrl.Origin + "/", StringComparison.OrdinalIgnoreCase)) return;
        switch (e.TryGetWebMessageAsString())
        {
            case "open": ShowOpenDialog(); break;
            case "toggle-tree": ToggleTree(); break;
            case "theme:dark": OnPageTheme(true); break;
            case "theme:light": OnPageTheme(false); break;
            case "plantuml:always":
                _settings.PlantUml = "always";
                if (_selfTestOutput is null) Settings.Update(s => s.PlantUml = "always");
                break;
        }
    }

    // ---------- risorse servite dal finto host https://zing.invalid ----------

    private const long MaxServedFileBytes = 50L * 1024 * 1024;

    private sealed record ResourceResult(Stream? Content, int Status, string Reason, string Headers);

    /// <summary>
    /// Lettura e rendering avvengono in background (deferral), così un file grande o una cartella
    /// di rete lenta non bloccano la finestra; la risposta WebView2 si crea poi sul thread UI.
    /// </summary>
    private async void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var env = _web.CoreWebView2.Environment;
        var deferral = e.GetDeferral();
        try
        {
            string url = e.Request.Uri;
            string? trustedRoot = _trustedNetworkRoot;
            string plantUml = PlantUmlMode;
            var sw = Stopwatch.StartNew();
            var r = await Task.Run(() => BuildResource(url, trustedRoot, plantUml));
            if (url.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) { Timing.Mark("md-served"); Timing.Mark($"md-build-ms={sw.ElapsedMilliseconds}"); }
            // la finestra può essere stata chiusa mentre si leggeva il file
            if (!IsDisposed && _web.CoreWebView2 is not null)
                e.Response = env.CreateWebResourceResponse(r.Content, r.Status, r.Reason, r.Headers);
        }
        catch (Exception)
        {
            // WebView2 già chiuso o richiesta annullata: niente da rispondere (async void non deve lanciare)
        }
        finally
        {
            try { deferral.Complete(); } catch (Exception) { }
        }
    }

    private static ResourceResult BuildResource(string url, string? trustedNetworkRoot, string plantUmlMode)
    {
        try
        {
            if (url.StartsWith(LocalUrl.AppPrefix, StringComparison.OrdinalIgnoreCase))
            {
                string name = new Uri(url).AbsolutePath["/__app/".Length..];
                var stream = typeof(MainForm).Assembly.GetManifestResourceStream("assets/" + name);
                return stream is null
                    ? new ResourceResult(null, 404, "Not Found", "")
                    : new ResourceResult(stream, 200, "OK", $"Content-Type: {LocalUrl.MimeType(name)}\r\nCache-Control: max-age=3600");
            }

            string? path = LocalUrl.ToPath(url);
            if (path is null)
                return HtmlResult(404, ErrorPage("File non trovato", url));

            // controllo prima di qualsiasi accesso al file system (niente SMB verso host esterni)
            if (!LinkPolicy.IsPathAllowed(path, trustedNetworkRoot))
                return HtmlResult(403, ErrorPage("Percorso non consentito", path + " — Zing non accede a percorsi di rete diversi da quello del file aperto."));

            if (!File.Exists(path))
                return HtmlResult(404, ErrorPage("File non trovato", path));

            if (LocalUrl.IsMarkdown(path))
            {
                var (html, nonce) = MarkdownRenderer.RenderPage(path, plantUmlMode);
                return new ResourceResult(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(html)), 200, "OK",
                    "Content-Type: text/html; charset=utf-8\r\nCache-Control: no-store\r\n" +
                    "Content-Security-Policy: " + MarkdownRenderer.ContentSecurityPolicy(nonce));
            }

            if (new FileInfo(path).Length > MaxServedFileBytes)
                return HtmlResult(413, ErrorPage("File troppo grande", path));

            byte[] bytes;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                bytes = new byte[fs.Length];
                fs.ReadExactly(bytes);
            }
            return new ResourceResult(new MemoryStream(bytes), 200, "OK",
                $"Content-Type: {LocalUrl.MimeType(path)}\r\nCache-Control: no-store\r\nContent-Security-Policy: default-src 'none'; img-src 'self'; style-src 'unsafe-inline'");
        }
        catch (Exception ex)
        {
            return HtmlResult(500, ErrorPage("Errore di lettura", ex.Message));
        }
    }

    private static ResourceResult HtmlResult(int status, string html) =>
        new(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(html)), status,
            status switch { 403 => "Forbidden", 404 => "Not Found", 413 => "Too Large", _ => "Error" },
            "Content-Type: text/html; charset=utf-8\r\nCache-Control: no-store\r\nContent-Security-Policy: default-src 'none'; style-src 'unsafe-inline'");

    private static string ErrorPage(string title, string detail) =>
        $"<!doctype html><meta charset=utf-8><title>{title}</title><body style=\"font-family:Segoe UI,sans-serif;padding:2em\"><h2>{title}</h2><p>{System.Net.WebUtility.HtmlEncode(detail)}</p><p>Alt+← per tornare indietro.</p></body>";

    // ---------- navigazione: i link locali restano qui, quelli esterni vanno al browser ----------

    // Regole (vedi LinkPolicy): un .md non fidato non deve poter lanciare programmi né aprire
    // connessioni di rete. Le aperture esterne richiedono un clic dell'utente (IsUserInitiated):
    // un <meta refresh> o un redirect non bastano.
    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        string uri = e.Uri;
        if (uri.StartsWith(LocalUrl.Origin + "/", StringComparison.OrdinalIgnoreCase) && !uri.StartsWith(LocalUrl.FilePrefix, StringComparison.OrdinalIgnoreCase))
            return;

        string? path = LocalUrl.ToPath(uri);
        if (path is not null)
        {
            if (!LinkPolicy.IsPathAllowed(path, _trustedNetworkRoot))
            {
                e.Cancel = true;
                return;
            }
            if (uri.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                // file trascinato nella finestra o link file:///
                e.Cancel = true;
                if (LocalUrl.IsMarkdown(path)) { if (e.IsUserInitiated) Navigate(path); }
                else TryOpenExternalFile(path, e.IsUserInitiated);
                return;
            }
            if (!LocalUrl.IsBrowserViewable(path))
            {
                e.Cancel = true;
                TryOpenExternalFile(path, e.IsUserInitiated);
            }
            return;
        }

        e.Cancel = true;
        if (e.IsUserInitiated && IsExternalWebLink(uri)) OpenWithShell(uri);
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (!e.IsUserInitiated) return;

        string? path = LocalUrl.ToPath(e.Uri);
        if (path is null)
        {
            if (IsExternalWebLink(e.Uri)) OpenWithShell(e.Uri);
            return;
        }
        if (!LinkPolicy.IsPathAllowed(path, _trustedNetworkRoot)) return;

        if (LocalUrl.IsMarkdown(path))
        {
            if (!File.Exists(path) || path.Contains('"')) return;
            var psi = new ProcessStartInfo(Application.ExecutablePath) { UseShellExecute = false };
            psi.ArgumentList.Add("--");   // tutto ciò che segue è un percorso, mai un'opzione
            psi.ArgumentList.Add(path);
            Process.Start(psi);
        }
        else TryOpenExternalFile(path, userInitiated: true);
    }

    private static bool IsExternalWebLink(string uri) =>
        uri.StartsWith("http:", StringComparison.OrdinalIgnoreCase) ||
        uri.StartsWith("https:", StringComparison.OrdinalIgnoreCase) ||
        uri.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);

    /// <summary>Apre un file non Markdown con l'app predefinita: solo da clic, solo tipi ammessi, sempre con conferma.</summary>
    private void TryOpenExternalFile(string path, bool userInitiated)
    {
        if (!userInitiated || !File.Exists(path)) return;

        if (!LinkPolicy.IsOpenable(path))
        {
            var answer = MessageBox.Show(this,
                $"Per sicurezza Zing non apre file di tipo \"{Path.GetExtension(path)}\".\n\n{path}\n\nVuoi mostrarlo in Esplora file?",
                "Zing", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (answer == DialogResult.Yes) Process.Start("explorer.exe", $"/select,\"{path}\"");
            return;
        }

        if (MessageBox.Show(this, $"Aprire il file con l'applicazione predefinita?\n\n{path}", "Zing",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            OpenWithShell(path);
    }

    private static void OpenWithShell(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show($"Impossibile aprire {target}: {ex.Message}", "Zing"); }
    }

    // ---------- ricarica automatica alla modifica del file ----------

    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        string? path = LocalUrl.ToPath(_web.CoreWebView2.Source);
        bool rendered = e.IsSuccess && e.HttpStatusCode == 200; // una pagina 404/403 non sposta albero né watcher
        if (rendered && path is not null && LocalUrl.IsMarkdown(path) && !string.Equals(path, _currentFile, StringComparison.OrdinalIgnoreCase))
        {
            _currentFile = path;
            WatchFile(path);
            var swTree = Stopwatch.StartNew();
            _folderTree.Reveal(path);
            Timing.Mark($"tree-reveal-ms={swTree.ElapsedMilliseconds}");
        }

        Timing.Mark("nav-completed");
        _navDelay.Stop();
        _navBar.Visible = false;
        if (_startupOverlay.Visible) { _startupOverlay.Visible = false; _startupBar.MarqueeAnimationSpeed = 0; }
        if (_selfTestOutput is not null) await RunSelfTestAsync();
    }

    private void WatchFile(string path)
    {
        _watcher?.Dispose();
        _watcher = null;
        string? dir = Path.GetDirectoryName(path);
        if (dir is null || !Directory.Exists(dir)) return;

        _watcher = new FileSystemWatcher(dir, Path.GetFileName(path))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            SynchronizingObject = this,
        };
        FileSystemEventHandler restart = (_, _) => { _reloadTimer.Stop(); _reloadTimer.Start(); };
        _watcher.Changed += restart;
        _watcher.Created += restart;
        _watcher.Renamed += (_, _) => { _reloadTimer.Stop(); _reloadTimer.Start(); };
        _watcher.EnableRaisingEvents = true;
    }

    private async Task ReloadPreservingScrollAsync()
    {
        try { await _web.CoreWebView2.ExecuteScriptAsync("window.__mdvSaveScroll && window.__mdvSaveScroll()"); } catch { }
        _web.CoreWebView2.Reload();
    }

    // ---------- self-test: report JSON + screenshot ----------

    private async Task RunSelfTestAsync()
    {
        string prefix = _selfTestOutput!;
        string report = "null";
        for (int i = 0; i < 60; i++)
        {
            report = await _web.CoreWebView2.ExecuteScriptAsync("window.__mdvReport ? JSON.stringify(window.__mdvReport()) : null");
            if (report != "null")
            {
                using var json = JsonDocument.Parse(JsonDocument.Parse(report).RootElement.GetString()!);
                if (json.RootElement.GetProperty("ready").GetBoolean()) break;
            }
            await Task.Delay(250);
        }
        string page = report == "null" ? "{\"error\":\"viewer.js non caricato\"}" : JsonDocument.Parse(report).RootElement.GetString()!;
        await Task.Delay(300); // lascia applicare il tema all'albero prima dello screenshot
        Timing.Mark("page-ready");
        string tree = JsonSerializer.Serialize(new { info = _folderTree.Describe(), timing = Timing.Snapshot() });
        await File.WriteAllTextAsync(prefix + ".json", $"{{\"page\":{page},\"tree\":{tree},\"dark\":{(_dark ? "true" : "false")}}}");
        try
        {
            Activate();
            await Task.Delay(200);
            using (var bmp = new Bitmap(Width, Height))
            {
                using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(Location, Point.Empty, Size);
                bmp.Save(prefix + "-window.png");
            }
            await using (var png = File.Create(prefix + ".png"))
                await _web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, png);
        }
        catch (Exception ex)
        {
            // es. sessione bloccata: lo schermo non si può catturare, il report JSON resta valido
            await File.WriteAllTextAsync(prefix + "-screenshot-error.txt", ex.Message);
        }
        finally
        {
            Close();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _watcher?.Dispose(); _reloadTimer.Dispose(); _navDelay.Dispose(); }
        base.Dispose(disposing);
    }
}

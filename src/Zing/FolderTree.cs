using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Zing;

/// <summary>
/// Albero di cartelle e file Markdown con caricamento pigro: una cartella si legge solo quando
/// la si espande. Un FileSystemWatcher sulla radice aggiorna solo le cartelle già caricate.
/// </summary>
internal sealed class FolderTree : Panel
{
    private const string Placeholder = "…";
    private const int IconFolder = 0, IconFile = 1;

    private readonly TreeView _tree;
    private readonly Panel _header;
    private readonly Label _rootLabel;
    private readonly Button _rootButton;
    private readonly ToolTip _tip = new();
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 500 };
    private readonly HashSet<string> _dirty = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _watcher;
    private bool _suppressActivate;
    private bool _dark;

    /// <summary>L'utente ha scelto un file Markdown nell'albero.</summary>
    public event Action<string>? FileActivated;

    public string? RootPath { get; private set; }

    public FolderTree()
    {
        _tree = new TreeView
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            HideSelection = false,
            ShowLines = false,
            FullRowSelect = true,
            HotTracking = true,
            ItemHeight = 22,
            ImageList = CreateIcons(),
        };
        _tree.BeforeExpand += (_, e) => EnsureLoaded(e.Node!);
        _tree.AfterSelect += OnAfterSelect;
        _tree.KeyDown += OnTreeKeyDown;
        _tree.NodeMouseClick += (_, e) => { if (e.Button == MouseButtons.Right) _tree.SelectedNode = e.Node; };
        _tree.ContextMenuStrip = CreateContextMenu();
        _tree.HandleCreated += (_, _) => ApplyNativeTheme();

        _rootLabel = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Padding = new Padding(6, 0, 0, 0), Font = new Font(Font, FontStyle.Bold) };
        _rootButton = new Button { Dock = DockStyle.Right, Width = 32, Text = "…", FlatStyle = FlatStyle.Flat, TabStop = false };
        _rootButton.FlatAppearance.BorderSize = 0;
        _rootButton.Click += (_, _) => ChooseRoot();
        _tip.SetToolTip(_rootButton, "Cambia cartella radice");
        _header = new Panel { Dock = DockStyle.Top, Height = 30 };
        _header.Controls.Add(_rootLabel);
        _header.Controls.Add(_rootButton);

        Controls.Add(_header);
        Controls.Add(_tree);
        _tree.BringToFront();

        _refreshTimer.Tick += (_, _) => FlushRefresh();
        ApplyTheme(false);
    }

    // ---------- radice ----------

    /// <summary>Mostra il file nell'albero: se è fuori dalla radice corrente, la radice diventa il suo progetto.</summary>
    public void Reveal(string file)
    {
        if (RootPath is null || !IsUnder(file, RootPath))
        {
            var dir = new FileInfo(file).Directory;
            SetRoot((WikiLinks.FindProjectRoot(dir) ?? dir)!.FullName);
        }

        var node = FindNode(file, loadIfNeeded: true);
        if (node is null) return;
        _suppressActivate = true;
        try { _tree.SelectedNode = node; node.EnsureVisible(); }
        finally { _suppressActivate = false; }
    }

    public void SetRoot(string dir)
    {
        RootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir));
        _rootLabel.Text = Path.GetFileName(RootPath) is { Length: > 0 } name ? name : RootPath;
        _tip.SetToolTip(_rootLabel, RootPath);

        _tree.BeginUpdate();
        _tree.Nodes.Clear();
        AddChildren(_tree.Nodes, RootPath);
        _tree.EndUpdate();
        StartWatcher();
    }

    private void ChooseRoot()
    {
        using var dlg = new FolderBrowserDialog { Description = "Cartella radice dell'albero", UseDescriptionForTitle = true, InitialDirectory = RootPath ?? "" };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        string? selected = (_tree.SelectedNode?.Tag as string);
        SetRoot(dlg.SelectedPath);
        if (selected is not null && IsUnder(selected, RootPath!)) Reveal(selected);
    }

    // ---------- caricamento pigro ----------

    private static void AddChildren(TreeNodeCollection nodes, string dir)
    {
        DirectoryInfo info = new(dir);
        try
        {
            foreach (var d in info.EnumerateDirectories().Where(IsVisibleDir).OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var n = new TreeNode(d.Name, IconFolder, IconFolder) { Tag = d.FullName, Name = d.Name };
                n.Nodes.Add(new TreeNode(Placeholder));
                nodes.Add(n);
            }
            foreach (var f in info.EnumerateFiles().Where(f => LocalUrl.IsMarkdown(f.Name)).OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase))
                nodes.Add(new TreeNode(f.Name, IconFile, IconFile) { Tag = f.FullName, Name = f.Name });
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            nodes.Add(new TreeNode("(non accessibile)") { ForeColor = Color.Gray });
        }
    }

    private static bool IsVisibleDir(DirectoryInfo d) =>
        !WikiLinks.IsExcludedDir(d.Name) && !d.Name.StartsWith('.') &&
        (d.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0;

    private static bool IsDirNode(TreeNode n) => n.Tag is string p && Directory.Exists(p);
    private static bool IsLoaded(TreeNode n) => !(n.Nodes.Count == 1 && n.Nodes[0].Tag is null && n.Nodes[0].Text == Placeholder);

    private void EnsureLoaded(TreeNode node)
    {
        if (IsLoaded(node) || node.Tag is not string dir) return;
        _tree.BeginUpdate();
        node.Nodes.Clear();
        AddChildren(node.Nodes, dir);
        _tree.EndUpdate();
    }

    /// <summary>Trova il nodo di un percorso sotto la radice, caricando le cartelle intermedie se richiesto.</summary>
    private TreeNode? FindNode(string path, bool loadIfNeeded)
    {
        if (RootPath is null) return null;
        string rel = Path.GetRelativePath(RootPath, path);
        if (rel == ".") return null;
        TreeNodeCollection level = _tree.Nodes;
        TreeNode? node = null;
        foreach (string seg in rel.Split(Path.DirectorySeparatorChar))
        {
            if (node is not null)
            {
                if (!IsLoaded(node)) { if (!loadIfNeeded) return null; EnsureLoaded(node); }
                if (loadIfNeeded) node.Expand();
                level = node.Nodes;
            }
            node = level.Cast<TreeNode>().FirstOrDefault(n => string.Equals(n.Name, seg, StringComparison.OrdinalIgnoreCase));
            if (node is null) return null;
        }
        return node;
    }

    // ---------- aggiornamento quando i file cambiano ----------

    private void StartWatcher()
    {
        _watcher?.Dispose();
        _watcher = null;
        if (RootPath is null || !Directory.Exists(RootPath)) return;
        try
        {
            _watcher = new FileSystemWatcher(RootPath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                InternalBufferSize = 64 * 1024,
                SynchronizingObject = this,
            };
            _watcher.Created += (_, e) => MarkDirty(e.FullPath);
            _watcher.Deleted += (_, e) => MarkDirty(e.FullPath);
            _watcher.Renamed += (_, e) => { MarkDirty(e.OldFullPath); MarkDirty(e.FullPath); };
            _watcher.Error += (_, _) => { _dirty.Add(RootPath); _refreshTimer.Stop(); _refreshTimer.Start(); };
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            _watcher = null; // es. cartella di rete senza notifiche: l'albero resta statico
        }
    }

    private void MarkDirty(string path)
    {
        if (RootPath is null) return;
        string rel = Path.GetRelativePath(RootPath, path);
        if (rel.Split(Path.DirectorySeparatorChar).Any(s => WikiLinks.IsExcludedDir(s) || s.StartsWith('.'))) return;
        if (Path.HasExtension(path) && !LocalUrl.IsMarkdown(path) && !Directory.Exists(path)) return;
        string? parent = Path.GetDirectoryName(path);
        if (parent is null) return;
        _dirty.Add(parent);
        _refreshTimer.Stop();
        _refreshTimer.Start();
    }

    private void FlushRefresh()
    {
        _refreshTimer.Stop();
        string? selected = _tree.SelectedNode?.Tag as string;
        _tree.BeginUpdate();
        foreach (string dir in _dirty.ToList())
        {
            if (string.Equals(dir, RootPath, StringComparison.OrdinalIgnoreCase))
                Reload(_tree.Nodes, dir);
            else if (FindNode(dir, loadIfNeeded: false) is { } node && IsLoaded(node))
                Reload(node.Nodes, dir);
        }
        _dirty.Clear();
        _tree.EndUpdate();

        if (selected is not null && FindNode(selected, loadIfNeeded: false) is { } sel)
        {
            _suppressActivate = true;
            try { _tree.SelectedNode = sel; } finally { _suppressActivate = false; }
        }
    }

    /// <summary>Rilegge una cartella conservando le sottocartelle espanse.</summary>
    private void Reload(TreeNodeCollection nodes, string dir)
    {
        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectExpanded(nodes, expanded);
        nodes.Clear();
        AddChildren(nodes, dir);
        RestoreExpanded(nodes, expanded);
    }

    private static void CollectExpanded(TreeNodeCollection nodes, HashSet<string> into)
    {
        foreach (TreeNode n in nodes)
            if (n.IsExpanded && n.Tag is string p) { into.Add(p); CollectExpanded(n.Nodes, into); }
    }

    private void RestoreExpanded(TreeNodeCollection nodes, HashSet<string> expanded)
    {
        foreach (TreeNode n in nodes)
            if (n.Tag is string p && expanded.Contains(p))
            {
                EnsureLoaded(n);
                n.Expand();
                RestoreExpanded(n.Nodes, expanded);
            }
    }

    // ---------- interazione ----------

    private void OnAfterSelect(object? sender, TreeViewEventArgs e)
    {
        if (_suppressActivate || e.Action != TreeViewAction.ByMouse) return;
        Activate(e.Node);
    }

    private void OnTreeKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter) return;
        e.Handled = e.SuppressKeyPress = true;
        Activate(_tree.SelectedNode);
    }

    private void Activate(TreeNode? node)
    {
        if (node?.Tag is string p && File.Exists(p)) FileActivated?.Invoke(p);
        else node?.Toggle();
    }

    private ContextMenuStrip CreateContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Mostra in Esplora file", null, (_, _) =>
        {
            if (_tree.SelectedNode?.Tag is string p) Process.Start("explorer.exe", $"/select,\"{p}\"");
        });
        menu.Items.Add("Copia percorso", null, (_, _) =>
        {
            if (_tree.SelectedNode?.Tag is string p) Clipboard.SetText(p);
        });
        menu.Items.Add("Usa come radice", null, (_, _) =>
        {
            if (_tree.SelectedNode?.Tag is string p && Directory.Exists(p)) SetRoot(p);
        });
        menu.Items.Add("Risali di una cartella", null, (_, _) =>
        {
            if (RootPath is not null && Path.GetDirectoryName(RootPath) is { } parent) SetRoot(parent);
        });
        menu.Opening += (_, _) => menu.Items[2].Enabled = _tree.SelectedNode is { } n && IsDirNode(n);
        return menu;
    }

    private static bool IsUnder(string path, string root)
    {
        string rel = Path.GetRelativePath(root, path);
        return rel != ".." && !rel.StartsWith(".." + Path.DirectorySeparatorChar) && !Path.IsPathRooted(rel);
    }

    // ---------- tema e icone ----------

    public void ApplyTheme(bool dark)
    {
        _dark = dark;
        Color back = dark ? Color.FromArgb(0x15, 0x1b, 0x23) : Color.FromArgb(0xf6, 0xf8, 0xfa);
        Color fore = dark ? Color.FromArgb(0xe6, 0xed, 0xf3) : Color.FromArgb(0x1f, 0x23, 0x28);
        BackColor = _tree.BackColor = _header.BackColor = _rootButton.BackColor = back;
        ForeColor = _tree.ForeColor = _rootLabel.ForeColor = _rootButton.ForeColor = fore;
        ApplyNativeTheme();
    }

    private void ApplyNativeTheme()
    {
        if (_tree.IsHandleCreated) SetWindowTheme(_tree.Handle, _dark ? "DarkMode_Explorer" : "Explorer", null);
    }

    private ImageList CreateIcons()
    {
        int size = LogicalToDeviceUnits(16);
        var list = new ImageList { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(size, size) };
        list.Images.Add(StockIcon(3 /* SIID_FOLDER */) ?? SystemIcons.Application.ToBitmap());
        list.Images.Add(StockIcon(0 /* SIID_DOCNOASSOC */) ?? SystemIcons.Application.ToBitmap());
        return list;
    }

    private static Bitmap? StockIcon(int id)
    {
        var info = new SHSTOCKICONINFO { cbSize = (uint)Marshal.SizeOf<SHSTOCKICONINFO>() };
        if (SHGetStockIconInfo(id, 0x100 | 0x1 /* SHGSI_ICON | SHGSI_SMALLICON */, ref info) != 0) return null;
        try { using var icon = Icon.FromHandle(info.hIcon); return icon.ToBitmap(); }
        finally { DestroyIcon(info.hIcon); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHSTOCKICONINFO
    {
        public uint cbSize;
        public IntPtr hIcon;
        public int iSysImageIndex;
        public int iIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szPath;
    }

    [DllImport("shell32.dll")] private static extern int SHGetStockIconInfo(int siid, uint flags, ref SHSTOCKICONINFO info);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] private static extern int SetWindowTheme(IntPtr hwnd, string? app, string? idList);

    // ---------- self-test ----------

    public object Describe() => new
    {
        root = RootPath,
        selected = _tree.SelectedNode?.Tag as string,
        topLevelNodes = _tree.Nodes.Count,
        visible = Visible,
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _watcher?.Dispose(); _refreshTimer.Dispose(); _tip.Dispose(); }
        base.Dispose(disposing);
    }
}

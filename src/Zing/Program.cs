namespace Zing;

internal static class Program
{
    /// <summary>How long a later launch waits for the open window before opening its own (FA-001 R2.7).</summary>
    private static readonly TimeSpan HandOffBudget = TimeSpan.FromSeconds(3);

    [STAThread]
    private static int Main(string[] args)
    {
        Timing.Mark("main");
        ApplicationConfiguration.Initialize();

        // "--" separa le opzioni dal percorso: tutto ciò che segue è un file (usato quando Zing si rilancia da un link).
        // A window opened that way is a deliberate second window: it neither hands over nor receives files.
        if (args.Length >= 1 && args[0] == "--")
            return Open(args.Length >= 2 ? args[1] : null, singleWindow: false);

        bool quiet = args.Length == 2 && args[1] == "--quiet";
        if (args.Length is 1 or 2 && args[0] is "--register" or "--unregister" && (args.Length == 1 || quiet))
        {
            string msg = args[0] == "--register" ? FileAssociation.Register() : FileAssociation.Unregister();
            if (!quiet) MessageBox.Show(msg, "Zing", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        // --selftest <file.md> <prefisso-output>: apre il file, attende il rendering,
        // salva <prefisso>.json (report) e <prefisso>.png / -window.png (screenshot) ed esce.
        if (args.Length == 3 && args[0] == "--selftest")
        {
            MarkdownRenderer.ThemeOverride = Environment.GetEnvironmentVariable("ZING_THEME");
            Application.Run(new MainForm(Path.GetFullPath(args[1]), Path.GetFullPath(args[2])));
            return 0;
        }

        return Open(args.FirstOrDefault(a => !a.StartsWith("--")), singleWindow: true);
    }

    private static int Open(string? file, bool singleWindow)
    {
        string? fullPath = file is null ? null : Path.GetFullPath(file);
        SingleInstance? primary = null;
        if (singleWindow)
        {
            string channel = SingleInstance.DefaultChannel;
            primary = SingleInstance.TryBecomePrimary(channel);
            // another window is open: hand the file over and exit; if it does not answer, open our own
            if (primary is null && SingleInstance.TrySend(channel, fullPath, HandOffBudget)) return 0;
        }

        try
        {
            if (fullPath is null)
            {
                using var dlg = MainForm.CreateOpenDialog(null);
                if (dlg.ShowDialog() != DialogResult.OK) return 0;
                fullPath = dlg.FileName;
            }
            Application.Run(new MainForm(fullPath, selfTestOutput: null, primary));
            return 0;
        }
        finally
        {
            primary?.Dispose();
        }
    }
}

namespace Zing;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // "--" separa le opzioni dal percorso: tutto ciò che segue è un file (usato quando Zing si rilancia da un link).
        if (args.Length >= 1 && args[0] == "--")
            return Open(args.Length >= 2 ? args[1] : null);

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
            Application.Run(new MainForm(Path.GetFullPath(args[1]), Path.GetFullPath(args[2])));
            return 0;
        }

        return Open(args.FirstOrDefault(a => !a.StartsWith("--")));
    }

    private static int Open(string? file)
    {
        if (file is null)
        {
            using var dlg = MainForm.CreateOpenDialog(null);
            if (dlg.ShowDialog() != DialogResult.OK) return 0;
            file = dlg.FileName;
        }
        Application.Run(new MainForm(Path.GetFullPath(file)));
        return 0;
    }
}

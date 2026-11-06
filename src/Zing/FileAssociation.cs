using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Zing;

/// <summary>
/// Registrazione per il solo utente corrente (HKCU, nessun privilegio amministrativo).
/// Windows protegge la scelta del programma predefinito (UserChoice): l'app si registra
/// come candidata e la scelta finale la fa l'utente in Impostazioni o con "Apri con".
/// </summary>
internal static class FileAssociation
{
    private const string ProgId = "Zing.Markdown";
    private const string AppKey = @"Software\Zing";
    private static readonly string[] Extensions = [".md", ".markdown"];

    public static string Register()
    {
        string exe = Application.ExecutablePath;
        string command = $"\"{exe}\" \"%1\"";

        using (var prog = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}"))
        {
            prog.SetValue("", "Documento Markdown");
            using (var icon = prog.CreateSubKey("DefaultIcon")) icon.SetValue("", $"\"{exe}\",0");
            using (var cmd = prog.CreateSubKey(@"shell\open\command")) cmd.SetValue("", command);
        }

        using (var app = Registry.CurrentUser.CreateSubKey(@"Software\Classes\Applications\Zing.exe"))
        {
            app.SetValue("FriendlyAppName", "Zing");
            using (var cmd = app.CreateSubKey(@"shell\open\command")) cmd.SetValue("", command);
            using var types = app.CreateSubKey("SupportedTypes");
            foreach (string ext in Extensions) types.SetValue(ext, "");
        }

        foreach (string ext in Extensions)
        {
            using var owp = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ext}\OpenWithProgids");
            owp.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
        }

        using (var caps = Registry.CurrentUser.CreateSubKey($@"{AppKey}\Capabilities"))
        {
            caps.SetValue("ApplicationName", "Zing");
            caps.SetValue("ApplicationDescription", "Visualizzatore leggero di file Markdown");
            using var fa = caps.CreateSubKey("FileAssociations");
            foreach (string ext in Extensions) fa.SetValue(ext, ProgId);
        }
        using (var reg = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
            reg.SetValue("Zing", $@"{AppKey}\Capabilities");

        NotifyShell();
        return "Zing è registrato per .md e .markdown.\n\n" +
               "Per renderlo il programma predefinito: tasto destro su un file .md → Apri con → Scegli un'altra app → Zing → Sempre.";
    }

    public static string Unregister()
    {
        // Installato con l'MSI: le chiavi le gestisce l'installer, rimuoverle qui lo lascerebbe incoerente.
        using (var msi = Registry.CurrentUser.OpenSubKey($@"{AppKey}\Installer"))
            if (msi is not null)
                return "Zing è stato installato con il pacchetto MSI: per rimuoverlo usa Impostazioni → App.";

        Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{ProgId}", false);
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Applications\Zing.exe", false);
        Registry.CurrentUser.DeleteSubKeyTree($@"{AppKey}\Capabilities", false);
        using (var app = Registry.CurrentUser.OpenSubKey(AppKey))
            if (app is not null && app.SubKeyCount == 0 && app.ValueCount == 0)
                Registry.CurrentUser.DeleteSubKey(AppKey, false);
        foreach (string ext in Extensions)
            using (var owp = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ext}\OpenWithProgids", writable: true))
                owp?.DeleteValue(ProgId, false);
        using (var reg = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications", writable: true))
            reg?.DeleteValue("Zing", false);

        NotifyShell();
        return "Registrazione di Zing rimossa.";
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

    private static void NotifyShell() => SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0, IntPtr.Zero, IntPtr.Zero);
}

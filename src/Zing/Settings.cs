using System.Text.Json;

namespace Zing;

/// <summary>Preferenze dell'utente, in %LOCALAPPDATA%\Zing\settings.json.</summary>
internal sealed class Settings
{
    public bool TreeVisible { get; set; } = true;
    public int TreeWidth { get; set; } = 280;
    public int WindowWidth { get; set; } = 1200;
    public int WindowHeight { get; set; } = 850;
    public bool Maximized { get; set; }

    /// <summary>"ask" (predefinito): i diagrammi PlantUML si inviano a plantuml.com solo su richiesta; "always": sempre.</summary>
    public string PlantUml { get; set; } = "ask";

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Zing", "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch (Exception) { /* file corrotto: si riparte dai valori predefiniti */ }
        return new Settings();
    }

    /// <summary>Rilegge il file, applica la modifica e salva: più finestre aperte non si sovrascrivono le scelte.</summary>
    public static void Update(Action<Settings> change)
    {
        var current = Load();
        change(current);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(current, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception) { /* preferenze non salvate: non blocca l'uso */ }
    }
}

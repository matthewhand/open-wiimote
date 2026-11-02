using System.Text.Json;
using OpenWiimote.Core.Storage;

namespace OpenWiimote.Core.Settings;

public sealed class SettingsStore
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public SettingsStore(string baseDirectory)
    {
        BaseDirectory = baseDirectory;
        System.IO.Directory.CreateDirectory(BaseDirectory);
        System.IO.Directory.CreateDirectory(ProfilesDirectory);
        System.IO.Directory.CreateDirectory(LogsDirectory);
    }

    public static SettingsStore CreateDefault()
    {
        return new SettingsStore(AppDataPaths.ResolveOpenWiimoteDirectory());
    }

    public string BaseDirectory { get; }
    public string SettingsPath => Path.Combine(BaseDirectory, "settings.json");
    public string ProfilesDirectory => Path.Combine(BaseDirectory, "Profiles");
    public string LogsDirectory => Path.Combine(BaseDirectory, "Logs");

    public AppSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return new AppSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), _jsonOptions)
                   ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var temp = SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var json = JsonSerializer.Serialize(settings, _jsonOptions);
        try
        {
            File.WriteAllText(temp, json);
            File.Move(temp, SettingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}

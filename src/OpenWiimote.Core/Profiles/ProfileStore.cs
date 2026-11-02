using System.Text.Json;
using OpenWiimote.Core.Models;
using OpenWiimote.Core.Storage;

namespace OpenWiimote.Core.Profiles;

public sealed class ProfileStore
{
    private const int MaxNameAttempts = 1000;

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public ProfileStore(string directory, bool ensureDefaultProfile = true)
    {
        Directory = directory;
        System.IO.Directory.CreateDirectory(Directory);
        if (ensureDefaultProfile)
        {
            EnsureDefaultProfile();
        }
    }

    public string Directory { get; }

    public static ProfileStore CreateDefault()
    {
        var appData = AppDataPaths.Resolve();
        var store = new ProfileStore(
            Path.Combine(appData, "OpenWiimote", "Profiles"),
            ensureDefaultProfile: false);
        store.MigrateFrom(Path.Combine(appData, "WiimoteManager", "Profiles"));
        store.EnsureDefaultProfile();
        return store;
    }

    public int MigrateFrom(string sourceDirectory)
    {
        if (!System.IO.Directory.Exists(sourceDirectory))
        {
            return 0;
        }

        var copied = 0;
        foreach (var source in System.IO.Directory.EnumerateFiles(sourceDirectory, "*.json"))
        {
            var target = Path.Combine(Directory, Path.GetFileName(source));
            if (File.Exists(target))
            {
                continue;
            }

            try
            {
                File.Copy(source, target);
                copied++;
            }
            catch
            {
            }
        }

        return copied;
    }

    public IReadOnlyList<string> GetProfileNames()
    {
        var names = new List<string>();
        foreach (var file in EnumerateProfileFiles())
        {
            var profile = TryLoadFile(file);
            var name = profile?.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                name = Path.GetFileNameWithoutExtension(file);
            }

            if (!names.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                names.Add(name);
            }
        }

        return names
            .OrderBy(name => name.Equals("Default", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public MappingProfile? LoadProfile(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        foreach (var file in EnumerateProfileFiles())
        {
            var profile = TryLoadFile(file);
            if (profile is not null &&
                (string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(Path.GetFileNameWithoutExtension(file), name, StringComparison.OrdinalIgnoreCase)))
            {
                return profile;
            }
        }

        return null;
    }

    public void SaveProfile(MappingProfile profile) => SaveProfile(profile, overwrite: true);

    private void SaveProfile(MappingProfile profile, bool overwrite)
    {
        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            throw new ArgumentException("Profile name cannot be empty.", nameof(profile));
        }

        profile.ModifiedAt = DateTime.Now;
        profile.Version = MappingProfile.CurrentVersion;
        var path = Path.Combine(Directory, SanitizeFileName(profile.Name) + ".json");
        // Each save needs its own temporary file: overlapping dashboard requests
        // otherwise write and move the same path, corrupting or failing a save.
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var json = JsonSerializer.Serialize(profile, _jsonOptions);
        try
        {
            File.WriteAllText(temp, json);
            File.Move(temp, path, overwrite);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    /// <summary>
    /// Updates a single mapping in a profile, validating the target and button.
    /// </summary>
    public bool UpdateMapping(string profileName, string target, string? button)
    {
        var profile = LoadProfile(profileName)
            ?? throw new ArgumentException($"Profile '{profileName}' was not found.", nameof(profileName));

        var mapping = FindMapping(profile, target)
            ?? throw new ArgumentException($"Target '{target}' was not found.", nameof(target));

        WiimoteButton? mappedButton = null;
        if (!string.IsNullOrWhiteSpace(button) && !string.Equals(button, "Unmapped", StringComparison.OrdinalIgnoreCase) && !string.Equals(button, "None", StringComparison.OrdinalIgnoreCase))
        {
            if (!Enum.TryParse<WiimoteButton>(button, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
            {
                throw new ArgumentException($"Button '{button}' is not valid.", nameof(button));
            }

            if (parsed == WiimoteButton.None)
            {
                mappedButton = null;
            }
            else
            {
                mappedButton = parsed;
            }
        }

        mapping.WiimoteButton = mappedButton;
        SaveProfile(profile);
        return true;
    }

    /// <summary>
    /// Updates the Nunchuk and Classic Controller buttons of a mapping, validating both.
    /// </summary>
    public bool UpdateExtensionMapping(
        string profileName,
        string target,
        string? nunchukButton,
        string? classicButton)
    {
        var profile = LoadProfile(profileName)
            ?? throw new ArgumentException($"Profile '{profileName}' was not found.", nameof(profileName));

        var mapping = FindMapping(profile, target)
            ?? throw new ArgumentException($"Target '{target}' was not found.", nameof(target));

        mapping.NunchukButton = ParseButton<NunchukButton>(nunchukButton, nameof(nunchukButton));
        mapping.ClassicButton = ParseButton<ClassicButton>(classicButton, nameof(classicButton));
        SaveProfile(profile);
        return true;
    }

    /// <summary>
    /// Updates the target stick of one extension stick source.
    /// </summary>
    /// <param name="source">"Nunchuk", "ClassicLeft" or "ClassicRight".</param>
    public bool UpdateExtensionStick(string profileName, string source, string? target)
    {
        var profile = LoadProfile(profileName)
            ?? throw new ArgumentException($"Profile '{profileName}' was not found.", nameof(profileName));

        var mapping = profile.ExtensionStick ??= new ExtensionStickMapping();
        var normalized = string.IsNullOrWhiteSpace(target)
            ? ExtensionStickMapping.UnmappedTarget
            : ExtensionStickMapping.Targets.FirstOrDefault(candidate =>
                string.Equals(candidate, target.Trim(), StringComparison.OrdinalIgnoreCase))
              ?? throw new ArgumentException($"Stick target '{target}' is not valid.", nameof(target));

        switch (source?.Trim().ToLowerInvariant())
        {
            case "nunchuk":
                mapping.NunchukTarget = normalized;
                break;
            case "classicle" or "classicleft":
                mapping.ClassicLeftTarget = normalized;
                break;
            case "classicright" or "classicr":
                mapping.ClassicRightTarget = normalized;
                break;
            default:
                throw new ArgumentException($"Stick source '{source}' is not valid.", nameof(source));
        }

        SaveProfile(profile);
        return true;
    }

    private static TEnum? ParseButton<TEnum>(string? value, string parameterName) where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value) ||
            string.Equals(value, "Unmapped", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "None", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
        {
            throw new ArgumentException($"Button '{value}' is not valid.", parameterName);
        }

        return EqualityComparer<TEnum>.Default.Equals(parsed, default) ? null : parsed;
    }

    private static ControlMapping? FindMapping(MappingProfile profile, string target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return null;
        }

        foreach (var mapping in profile.AllMappings)
        {
            if (string.Equals(mapping.TargetName, target, StringComparison.OrdinalIgnoreCase))
            {
                return mapping;
            }
        }

        // Also try matching by property name
        var prop = typeof(MappingProfile).GetProperty(target, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
        if (prop?.PropertyType == typeof(ControlMapping))
        {
            return prop.GetValue(profile) as ControlMapping;
        }

        return null;
    }

    public bool DeleteProfile(string name)
    {
        if (string.Equals(name, "Default", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The Default profile cannot be deleted.", nameof(name));
        foreach (var file in EnumerateProfileFiles())
        {
            var profile = TryLoadFile(file);
            if (profile is not null && string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(file);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Creates a profile, either empty apart from the default layout or as a copy of an
    /// existing profile.
    /// </summary>
    /// <returns>The created profile.</returns>
    /// <exception cref="ArgumentException">
    /// The name is empty or already used, or <paramref name="copyOf"/> does not exist.
    /// </exception>
    public MappingProfile CreateProfile(string name, string? copyOf = null)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("Profile name cannot be empty.", nameof(name));
        }

        if (LoadProfile(trimmed) is not null)
        {
            throw new ArgumentException($"A profile named '{trimmed}' already exists.", nameof(name));
        }

        MappingProfile profile;
        if (string.IsNullOrWhiteSpace(copyOf))
        {
            profile = MappingProfile.CreateDefault();
        }
        else
        {
            var source = LoadProfile(copyOf)
                ?? throw new ArgumentException($"Profile '{copyOf}' was not found.", nameof(copyOf));

            // The copy must not share state with the profile it came from.
            profile = CloneProfile(source);
        }

        profile.Name = trimmed;
        try
        {
            SaveProfile(profile, overwrite: false);
        }
        catch (IOException) when (File.Exists(GetPathForProfile(trimmed)))
        {
            throw new ArgumentException($"A profile with the same filename as '{trimmed}' already exists.", nameof(name));
        }
        return profile;
    }

    /// <summary>
    /// The first free name among "Base", "Base 2", "Base 3", ... so two front ends
    /// creating a profile at the same time do not fight over the name.
    /// </summary>
    public string NextAvailableProfileName(string baseName)
    {
        var root = string.IsNullOrWhiteSpace(baseName) ? "New profile" : baseName.Trim();
        if (LoadProfile(root) is null)
        {
            return root;
        }

        for (var suffix = 2; suffix < MaxNameAttempts; suffix++)
        {
            var candidate = $"{root} {suffix}";
            if (LoadProfile(candidate) is null)
            {
                return candidate;
            }
        }

        throw new InvalidOperationException($"No free profile name based on '{root}' was found.");
    }

    /// <summary>
    /// Renames a profile, keeping its settings, and moves its file to the new name.
    /// </summary>
    /// <returns><c>false</c> when no profile with <paramref name="name"/> exists.</returns>
    /// <exception cref="ArgumentException">
    /// The new name is empty or already used by a different profile.
    /// </exception>
    public bool RenameProfile(string? name, string newName)
    {
        var trimmed = (newName ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("Profile name cannot be empty.", nameof(newName));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var sourceFile = EnumerateProfileFiles().FirstOrDefault(file =>
        {
            var candidate = TryLoadFile(file);
            return candidate is not null &&
                   (string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFileNameWithoutExtension(file), name, StringComparison.OrdinalIgnoreCase));
        });

        if (sourceFile is null)
        {
            return false;
        }

        var profile = TryLoadFile(sourceFile);
        if (profile is null)
        {
            return false;
        }

        // Changing only the letter case is a rename of the same profile, not a clash.
        var caseOnlyChange = string.Equals(profile.Name, trimmed, StringComparison.OrdinalIgnoreCase);
        if (!caseOnlyChange && LoadProfile(trimmed) is not null)
        {
            throw new ArgumentException($"A profile named '{trimmed}' already exists.", nameof(newName));
        }

        profile.Name = trimmed;

        var targetFile = Path.Combine(Directory, SanitizeFileName(trimmed) + ".json");

        // "default" and "Default" are one file on Windows and macOS and two on Linux, and
        // the file names are the same either way. A case sensitive file system needs the move
        // to happen, while on a case insensitive one the file is already in place and the
        // write below has to be the only thing that touches it: writing and then deleting the
        // old path would delete the profile that was just written.
        var sameFileName = string.Equals(
            Path.GetFileName(sourceFile), Path.GetFileName(targetFile), StringComparison.OrdinalIgnoreCase);
        if (!sameFileName && File.Exists(targetFile))
        {
            throw new ArgumentException($"A profile with the same filename as '{trimmed}' already exists.", nameof(newName));
        }
        if (sameFileName && !File.Exists(targetFile))
        {
            File.Move(sourceFile, targetFile);
        }

        try
        {
            SaveProfile(profile, overwrite: sameFileName);
        }
        catch (IOException) when (!sameFileName && File.Exists(targetFile))
        {
            throw new ArgumentException($"A profile with the same filename as '{trimmed}' already exists.", nameof(newName));
        }

        if (!sameFileName)
        {
            File.Delete(sourceFile);
        }

        return true;
    }

    public void EnsureDefaultProfile()
    {
        if (LoadProfile("Default") is not null)
        {
            return;
        }

        SaveProfile(MappingProfile.CreateDefault());
    }

    public string GetPathForProfile(string name)
    {
        return Path.Combine(Directory, SanitizeFileName(name) + ".json");
    }

    private IEnumerable<string> EnumerateProfileFiles()
    {
        return System.IO.Directory.EnumerateFiles(Directory, "*.json").OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
    }

    private MappingProfile CloneProfile(MappingProfile profile)
    {
        var json = JsonSerializer.Serialize(profile, _jsonOptions);
        return JsonSerializer.Deserialize<MappingProfile>(json, _jsonOptions) ?? new MappingProfile();
    }

    private MappingProfile? TryLoadFile(string path)
    {
        try
        {
            var profile = JsonSerializer.Deserialize<MappingProfile>(File.ReadAllText(path), _jsonOptions);
            return profile is null ? null : Migrate(profile);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Fills the extension sources that version 2 profiles do not have. A target whose
    /// Wiimote button was remapped is left alone, because the profile no longer follows the
    /// default layout and guessing the extension button would be wrong more often than not.
    /// </summary>
    private static MappingProfile Migrate(MappingProfile profile)
    {
        if (profile.Version < MappingProfile.CurrentVersion)
        {
            var defaults = MappingProfile.CreateDefault().AllMappings
                .ToDictionary(mapping => mapping.TargetName, StringComparer.OrdinalIgnoreCase);

            foreach (var mapping in profile.AllMappings)
            {
                if (!defaults.TryGetValue(mapping.TargetName, out var defaultMapping) ||
                    mapping.WiimoteButton != defaultMapping.WiimoteButton)
                {
                    continue;
                }

                mapping.NunchukButton ??= defaultMapping.NunchukButton;
                mapping.ClassicButton ??= defaultMapping.ClassicButton;
            }
        }

        profile.Version = MappingProfile.CurrentVersion;
        return profile;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var result = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(result) ? "Profile" : result;
    }
}

using System.Text.Json;
using OpenWiimote.Core.Models;
using OpenWiimote.Core.Profiles;
using Xunit;

namespace OpenWiimote.Core.Tests;

public sealed class ProfileStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "OpenWiimote.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void NewStore_CreatesDefaultProfile()
    {
        var store = new ProfileStore(_directory);

        var profile = store.LoadProfile("Default");

        Assert.NotNull(profile);
        Assert.Contains("Default", store.GetProfileNames());
        Assert.True(File.Exists(Path.Combine(_directory, "Default.json")));
    }

    [Fact]
    public void SaveAndLoad_RoundTripsProfile()
    {
        var store = new ProfileStore(_directory);
        var profile = MappingProfile.CreateDefault();
        profile.Name = "Test Profile";
        profile.Description = "Round trip";
        profile.IsFavorite = true;

        store.SaveProfile(profile);
        var loaded = store.LoadProfile("Test Profile");

        Assert.NotNull(loaded);
        Assert.Equal(profile.Description, loaded.Description);
        Assert.True(loaded.IsFavorite);
        Assert.Equal(profile.A.WiimoteButton, loaded.A.WiimoteButton);
    }

    [Fact]
    public void MigrateFrom_CopiesOnlyMissingProfiles()
    {
        var source = Path.Combine(_directory, "source");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "Existing.json"), "{}");
        File.WriteAllText(Path.Combine(source, "Imported.json"), "{}");

        var store = new ProfileStore(_directory);
        File.WriteAllText(Path.Combine(_directory, "Existing.json"), "keep");

        var copied = store.MigrateFrom(source);

        Assert.Equal(1, copied);
        Assert.Equal("keep", File.ReadAllText(Path.Combine(_directory, "Existing.json")));
        Assert.True(File.Exists(Path.Combine(_directory, "Imported.json")));
    }

    [Fact]
    public void MigrateFrom_PreservesMigratedDefaultBeforeCreatingFallbackDefault()
    {
        var source = Path.Combine(_directory, "source");
        var sourceStore = new ProfileStore(source);
        var migratedDefault = MappingProfile.CreateDefault();
        migratedDefault.Description = "Migrated default";
        sourceStore.SaveProfile(migratedDefault);

        var store = new ProfileStore(_directory, ensureDefaultProfile: false);
        var copied = store.MigrateFrom(source);
        store.EnsureDefaultProfile();

        Assert.Equal(1, copied);
        Assert.Equal("Migrated default", store.LoadProfile("Default")?.Description);
    }

    [Fact]
    public void CreateProfile_WithoutASourceUsesTheDefaultLayout()
    {
        var store = new ProfileStore(_directory);

        var created = store.CreateProfile("Brand New");

        Assert.Equal("Brand New", created.Name);
        Assert.True(File.Exists(Path.Combine(_directory, "Brand New.json")));
        Assert.Equal("Brand New", store.LoadProfile("Brand New")?.Name);
    }

    [Fact]
    public void CreateProfile_WithASourceCopiesTheMappingsAndTilt()
    {
        var store = new ProfileStore(_directory);
        var source = MappingProfile.CreateDefault();
        source.Name = "Source";
        source.Description = "Copied settings";
        source.AccelMapping.MaxTiltDegrees = 38f;
        source.A.WiimoteButton = WiimoteButton.Plus;
        store.SaveProfile(source);

        store.CreateProfile("Copy", "Source");

        var copy = store.LoadProfile("Copy");
        Assert.NotNull(copy);
        Assert.Equal("Copied settings", copy.Description);
        Assert.Equal(38f, copy.AccelMapping.MaxTiltDegrees);
        Assert.Equal(WiimoteButton.Plus, copy.A.WiimoteButton);
    }

    [Fact]
    public void CreateProfile_ProducesAnIndependentCopy()
    {
        var store = new ProfileStore(_directory);

        var copy = store.CreateProfile("Copy", "Default");
        copy.A.WiimoteButton = WiimoteButton.Home;
        store.SaveProfile(copy);

        Assert.NotEqual(WiimoteButton.Home, store.LoadProfile("Default")?.A.WiimoteButton);
    }

    [Fact]
    public void CreateProfile_RejectsANameThatIsTaken()
    {
        var store = new ProfileStore(_directory);

        Assert.Throws<ArgumentException>(() => store.CreateProfile("default"));
        Assert.Throws<ArgumentException>(() => store.CreateProfile("   "));
    }

    [Fact]
    public void CreateProfile_RejectsAMissingSource()
    {
        var store = new ProfileStore(_directory);

        Assert.Throws<ArgumentException>(() => store.CreateProfile("Copy", "Missing"));
        Assert.False(File.Exists(Path.Combine(_directory, "Copy.json")));
    }

    [Fact]
    public void NextAvailableProfileName_SkipsTheNamesThatAreTaken()
    {
        var store = new ProfileStore(_directory);

        Assert.Equal("New profile", store.NextAvailableProfileName("New profile"));

        store.CreateProfile("New profile");
        Assert.Equal("New profile 2", store.NextAvailableProfileName("New profile"));

        store.CreateProfile("New profile 2");
        Assert.Equal("New profile 3", store.NextAvailableProfileName("New profile"));
    }

    [Fact]
    public void RenameProfile_MovesTheFileAndKeepsTheSettings()
    {
        var store = new ProfileStore(_directory);
        var profile = MappingProfile.CreateDefault();
        profile.Name = "Old Name";
        profile.Description = "Kept across the rename";
        profile.AccelMapping.MaxTiltDegrees = 42f;
        store.SaveProfile(profile);

        var renamed = store.RenameProfile("Old Name", "New Name");

        Assert.True(renamed);
        Assert.Null(store.LoadProfile("Old Name"));
        Assert.False(File.Exists(Path.Combine(_directory, "Old Name.json")));
        Assert.True(File.Exists(Path.Combine(_directory, "New Name.json")));

        var loaded = store.LoadProfile("New Name");
        Assert.NotNull(loaded);
        Assert.Equal("New Name", loaded.Name);
        Assert.Equal("Kept across the rename", loaded.Description);
        Assert.Equal(42f, loaded.AccelMapping.MaxTiltDegrees);
        Assert.Contains("New Name", store.GetProfileNames());
        Assert.DoesNotContain("Old Name", store.GetProfileNames());
    }

    [Fact]
    public void RenameProfile_KeepsTheProfileWhenOnlyTheLetterCaseChanges()
    {
        var store = new ProfileStore(_directory);
        var profile = MappingProfile.CreateDefault();
        profile.Name = "Pixel";
        profile.Description = "Renamed by case only";
        store.SaveProfile(profile);
        var filesBefore = Directory.GetFiles(_directory, "*.json").Length;

        Assert.True(store.RenameProfile("Pixel", "pixel"));

        // The old spelling and the new one are the same file on Windows and macOS, so the
        // rename has to leave the same number of profiles behind instead of deleting the file
        // it just wrote.
        var loaded = store.LoadProfile("pixel");
        Assert.NotNull(loaded);
        Assert.Equal("pixel", loaded.Name);
        Assert.Equal("Renamed by case only", loaded.Description);
        Assert.Equal(filesBefore, Directory.GetFiles(_directory, "*.json").Length);
    }

    [Fact]
    public void RenameProfile_RejectsANameAnotherProfileUses()
    {
        var store = new ProfileStore(_directory);
        var profile = MappingProfile.CreateDefault();
        profile.Name = "First";
        store.SaveProfile(profile);

        Assert.Throws<ArgumentException>(() => store.RenameProfile("First", "Default"));

        // The rejected rename leaves both profiles untouched.
        Assert.NotNull(store.LoadProfile("First"));
        Assert.NotNull(store.LoadProfile("Default"));
    }

    [Fact]
    public void RenameProfile_RejectsAnEmptyName()
    {
        var store = new ProfileStore(_directory);

        Assert.Throws<ArgumentException>(() => store.RenameProfile("Default", "   "));
        Assert.NotNull(store.LoadProfile("Default"));
    }

    [Fact]
    public void RenameProfile_ReturnsFalseWhenTheProfileDoesNotExist()
    {
        var store = new ProfileStore(_directory);

        Assert.False(store.RenameProfile("Missing", "Something"));
        Assert.False(File.Exists(Path.Combine(_directory, "Something.json")));
    }

    [Fact]
    public void DeleteProfile_RemovesTheFile()
    {
        var store = new ProfileStore(_directory);
        var profile = MappingProfile.CreateDefault();
        profile.Name = "Throwaway";
        store.SaveProfile(profile);

        Assert.True(store.DeleteProfile("Throwaway"));
        Assert.Null(store.LoadProfile("Throwaway"));
        Assert.DoesNotContain("Throwaway", store.GetProfileNames());
    }

    [Fact]
    public void UpdateExtensionMapping_StoresTheNunchukAndClassicButtons()
    {
        var store = new ProfileStore(_directory);

        store.UpdateExtensionMapping("Default", "A", "Z", "X");
        var profile = store.LoadProfile("Default");

        Assert.NotNull(profile);
        Assert.Equal(NunchukButton.Z, profile.A.NunchukButton);
        Assert.Equal(ClassicButton.X, profile.A.ClassicButton);

        // The Wiimote source of the same target is not touched.
        Assert.Equal(WiimoteButton.Two, profile.A.WiimoteButton);
    }

    [Fact]
    public void UpdateExtensionMapping_AcceptsUnmappedAndRejectsUnknownNames()
    {
        var store = new ProfileStore(_directory);

        store.UpdateExtensionMapping("Default", "A", "Unmapped", null);
        var profile = store.LoadProfile("Default");

        Assert.NotNull(profile);
        Assert.Null(profile.A.NunchukButton);
        Assert.Null(profile.A.ClassicButton);
        Assert.Throws<ArgumentException>(() =>
            store.UpdateExtensionMapping("Default", "A", "C", "Jump"));
    }

    [Fact]
    public void CreateAndRenameDoNotOverwriteProfilesWithCollidingFileNames()
    {
        var store = new ProfileStore(_directory);
        store.CreateProfile("A_B");
        store.CreateProfile("Other");
        var original = File.ReadAllText(Path.Combine(_directory, "A_B.json"));

        // Slash is not a valid filename character on supported platforms.
        Assert.Throws<ArgumentException>(() => store.CreateProfile("A/B"));
        Assert.Throws<ArgumentException>(() => store.RenameProfile("Other", "A/B"));
        Assert.Equal(original, File.ReadAllText(Path.Combine(_directory, "A_B.json")));
        Assert.NotNull(store.LoadProfile("Other"));
    }

    [Fact]
    public async Task ConcurrentSavesKeepTheProfileReadable()
    {
        var store = new ProfileStore(_directory);
        var profiles = Enumerable.Range(0, 16).Select(_ => MappingProfile.CreateDefault()).ToArray();
        await Task.WhenAll(profiles.Select(profile => Task.Run(() => store.SaveProfile(profile))));
        Assert.NotNull(store.LoadProfile("Default"));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public void InvalidNumericButtonsDoNotChangeTheSavedProfile()
    {
        var store = new ProfileStore(_directory);
        var path = Path.Combine(_directory, "Default.json");
        var original = File.ReadAllText(path);

        Assert.Throws<ArgumentException>(() => store.UpdateMapping("Default", "A", "32768"));
        Assert.Equal(original, File.ReadAllText(path));
        Assert.Throws<ArgumentException>(() => store.UpdateExtensionMapping("Default", "A", "C", "32768"));
        Assert.Equal(original, File.ReadAllText(path));
    }

    [Fact]
    public void UpdateExtensionStick_StoresTheTargetAndValidatesTheSource()
    {
        var store = new ProfileStore(_directory);

        store.UpdateExtensionStick("Default", "Nunchuk", "RightStick");
        store.UpdateExtensionStick("Default", "ClassicRight", "Unmapped");
        var profile = store.LoadProfile("Default");

        Assert.NotNull(profile);
        Assert.Equal(ExtensionStickMapping.RightStickTarget, profile.ExtensionStick.NunchukTarget);
        Assert.Equal(ExtensionStickMapping.UnmappedTarget, profile.ExtensionStick.ClassicRightTarget);

        Assert.Throws<ArgumentException>(() =>
            store.UpdateExtensionStick("Default", "Nunchuk", "BothSticks"));
        Assert.Throws<ArgumentException>(() =>
            store.UpdateExtensionStick("Default", "Guitar", "LeftStick"));
    }

    [Fact]
    public void LoadProfile_MigratesAVersionTwoProfile()
    {
        var store = new ProfileStore(_directory);

        // A version 2 profile knew only Wiimote buttons. The copy keeps a remapped A, so
        // the migration must fill B but leave A alone.
        var old = MappingProfile.CreateDefault();
        old.Name = "Old";
        old.Version = 2;
        foreach (var mapping in old.AllMappings)
        {
            mapping.NunchukButton = null;
            mapping.ClassicButton = null;
        }
        old.A.WiimoteButton = WiimoteButton.Home;

        File.WriteAllText(
            Path.Combine(_directory, "Old.json"),
            JsonSerializer.Serialize(old, new JsonSerializerOptions { WriteIndented = true }));

        var loaded = store.LoadProfile("Old");

        Assert.NotNull(loaded);
        Assert.Equal(MappingProfile.CurrentVersion, loaded.Version);
        Assert.Equal(ClassicButton.B, loaded.B.ClassicButton);
        Assert.Equal(NunchukButton.Z, loaded.LeftTrigger.NunchukButton);
        Assert.Null(loaded.A.ClassicButton);
        Assert.NotNull(loaded.ExtensionStick);
        Assert.Equal(ExtensionStickMapping.LeftStickTarget, loaded.ExtensionStick.NunchukTarget);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}

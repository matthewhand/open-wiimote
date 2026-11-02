using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenWiimote.Core.Models;
using OpenWiimote.Core.Profiles;
using Xunit;

namespace OpenWiimote.App.Tests;

/// <summary>
/// Drives the profile controls of the real desktop window on the headless Avalonia
/// platform, so the rename and delete buttons are exercised the way a user uses them.
/// </summary>
public sealed class ProfileEditorTests
{
    private readonly string _configHome = Path.Combine(
        Path.GetTempPath(),
        "OpenWiimote.WindowTests",
        Guid.NewGuid().ToString("N"));

    private string ProfileDirectory => Path.Combine(_configHome, "OpenWiimote", "Profiles");

    public ProfileEditorTests()
    {
        Directory.CreateDirectory(ProfileDirectory);

        // ProfileStore.CreateDefault() resolves the profile folder from here.
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _configHome);

        var store = new ProfileStore(ProfileDirectory);
        SaveProfile(store, "Rename Me");
        SaveProfile(store, "Delete Me");
    }

    private static void SaveProfile(ProfileStore store, string name)
    {
        var profile = MappingProfile.CreateDefault();
        profile.Name = name;
        profile.Description = "Created by the window tests";
        store.SaveProfile(profile);
    }

    [AvaloniaFact]
    public void TheProfilePanelListsTheProfilesOnDisk()
    {
        using var window = OpenWindow();

        Assert.Contains("Default", window.Window.Profiles);
        Assert.Contains("Rename Me", window.Window.Profiles);
        Assert.Equal("Default", window.ProfileComboBox.SelectedItem);
    }

    [AvaloniaFact]
    public void TheRemoteProfilePickerOffersEveryProfilePlusTheActiveOne()
    {
        using var window = OpenWindow();

        var choices = window.DeviceProfileCombo.Items.OfType<string>().ToArray();

        Assert.Equal(window.Window.Profiles.Count + 1, choices.Length);
        Assert.Equal("Use the active profile", choices[0]);
        Assert.Contains("Rename Me", choices);
        Assert.Contains("Default", choices);

        // Without a remote there is nothing to pin, so the picker shows the active profile.
        Assert.Equal("Use the active profile", window.DeviceProfileCombo.SelectedItem);
    }

    [AvaloniaFact]
    public void RemovingAProfileTakesItOutOfTheRemoteProfilePicker()
    {
        using var window = OpenWindow();

        window.Select("Delete Me");
        window.Click(window.DeleteButton);
        window.Click(window.DeleteButton);

        Assert.DoesNotContain("Delete Me", window.DeviceProfileCombo.Items.OfType<string>());
        Assert.Contains("Rename Me", window.DeviceProfileCombo.Items.OfType<string>());
    }

    [AvaloniaFact]
    public void NewCopiesTheSelectedProfileAndSelectsIt()
    {
        using var window = OpenWindow();
        window.Select("Delete Me");

        var before = window.Window.Profiles.Count;
        window.Click(window.NewButton);

        Assert.Equal(before + 1, window.Window.Profiles.Count);
        Assert.Contains("New profile", window.Window.Profiles);
        Assert.Equal("New profile", window.ProfileComboBox.SelectedItem);
        Assert.Equal("New profile", window.NameBox.Text);
        Assert.True(File.Exists(Path.Combine(ProfileDirectory, "New profile.json")));
        Assert.Contains(
            window.Window.LogEntries,
            entry => entry.Message.Contains("Profile 'New profile' created"));
    }

    [AvaloniaFact]
    public void RenameMovesTheProfileFileAndKeepsItSelected()
    {
        using var window = OpenWindow();
        window.Select("Rename Me");

        Assert.Equal("Rename Me", window.NameBox.Text);

        window.NameBox.Text = "Renamed Profile";
        window.Click(window.RenameButton);

        Assert.True(File.Exists(Path.Combine(ProfileDirectory, "Renamed Profile.json")));
        Assert.False(File.Exists(Path.Combine(ProfileDirectory, "Rename Me.json")));
        Assert.Contains("Renamed Profile", window.Window.Profiles);
        Assert.DoesNotContain("Rename Me", window.Window.Profiles);
        Assert.Equal("Renamed Profile", window.ProfileComboBox.SelectedItem);
        Assert.Contains(
            window.Window.LogEntries,
            entry => entry.Message.Contains("renamed to 'Renamed Profile'"));
    }

    [AvaloniaFact]
    public void RenameRefusesANameAnotherProfileUses()
    {
        using var window = OpenWindow();
        window.Select("Rename Me");

        window.NameBox.Text = "Delete Me";
        window.Click(window.RenameButton);

        Assert.True(File.Exists(Path.Combine(ProfileDirectory, "Rename Me.json")));
        Assert.True(File.Exists(Path.Combine(ProfileDirectory, "Delete Me.json")));
        Assert.Contains(
            window.Window.LogEntries,
            entry => entry.Message.Contains("Could not rename the profile"));
    }

    [AvaloniaFact]
    public void DeleteAsksForASecondClickBeforeRemovingTheProfile()
    {
        using var window = OpenWindow();
        window.Select("Delete Me");

        window.Click(window.DeleteButton);

        // The first click only arms the button, so a stray click cannot lose a profile.
        Assert.True(File.Exists(Path.Combine(ProfileDirectory, "Delete Me.json")));
        Assert.Equal("Confirm delete", window.DeleteButton.Content);
        Assert.Contains(
            window.Window.LogEntries,
            entry => entry.Message.Contains("Press Delete again"));

        window.Click(window.DeleteButton);

        Assert.False(File.Exists(Path.Combine(ProfileDirectory, "Delete Me.json")));
        Assert.DoesNotContain("Delete Me", window.Window.Profiles);
        Assert.Equal("Default", window.ProfileComboBox.SelectedItem);
        Assert.Equal("Delete", window.DeleteButton.Content);
    }

    [AvaloniaFact]
    public void DeleteNeverRemovesTheDefaultProfile()
    {
        using var window = OpenWindow();
        window.Select("Default");

        window.Click(window.DeleteButton);
        window.Click(window.DeleteButton);

        Assert.True(File.Exists(Path.Combine(ProfileDirectory, "Default.json")));
        Assert.Contains("Default", window.Window.Profiles);
        Assert.Contains(
            window.Window.LogEntries,
            entry => entry.Message.Contains("The Default profile cannot be deleted"));
    }

    [AvaloniaFact]
    public void TheMappingRowForATargetShowsItsExtensionButtons()
    {
        using var window = OpenWindow();

        var classicA = FindMappingCombo(window.Window, "ClassicMappingComboBox", "A");
        var nunchukLt = FindMappingCombo(window.Window, "NunchukMappingComboBox", "LT");

        Assert.Equal("A", SelectedContent(classicA));
        Assert.Equal("Z", SelectedContent(nunchukLt));
    }

    [AvaloniaFact]
    public void ChangingTheClassicButtonKeepsTheNunchukButtonOfTheTarget()
    {
        using var window = OpenWindow();

        var classicA = FindMappingCombo(window.Window, "ClassicMappingComboBox", "A");
        classicA.SelectedItem = classicA.Items.OfType<ComboBoxItem>()
            .First(item => (string?)item.Content == "Y");
        Dispatcher.UIThread.RunJobs();

        var store = new ProfileStore(ProfileDirectory, ensureDefaultProfile: false);
        var profile = store.LoadProfile("Default");

        Assert.NotNull(profile);
        Assert.Equal(ClassicButton.Y, profile.A.ClassicButton);
        Assert.Null(profile.A.NunchukButton);
        Assert.Equal(WiimoteButton.Two, profile.A.WiimoteButton);
    }

    private static ComboBox FindMappingCombo(MainWindow window, string name, string target)
    {
        var combo = window.GetVisualDescendants()
            .OfType<ComboBox>()
            .FirstOrDefault(candidate => candidate.Name == name && (string?)candidate.Tag == target);

        Assert.NotNull(combo);
        return combo;
    }

    private static string? SelectedContent(ComboBox combo)
    {
        return (combo.SelectedItem as ComboBoxItem)?.Content?.ToString();
    }

    private WindowHandle OpenWindow()
    {
        var window = new MainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return new WindowHandle(window);
    }

    /// <summary>Thin accessor bundle so the tests read as a sequence of UI actions.</summary>
    private sealed class WindowHandle : IDisposable
    {
        public WindowHandle(MainWindow window)
        {
            Window = window;
            ProfileComboBox = window.FindControl<ComboBox>("ProfileComboBox")
                ?? throw new InvalidOperationException("ProfileComboBox is missing from the window.");
            DeviceProfileCombo = window.FindControl<ComboBox>("DeviceProfileComboBox")
                ?? throw new InvalidOperationException("DeviceProfileComboBox is missing from the window.");
            NameBox = window.FindControl<TextBox>("ProfileNameBox")
                ?? throw new InvalidOperationException("ProfileNameBox is missing from the window.");
            RenameButton = window.FindControl<Button>("RenameProfileButton")
                ?? throw new InvalidOperationException("RenameProfileButton is missing from the window.");
            DeleteButton = window.FindControl<Button>("DeleteProfileButton")
                ?? throw new InvalidOperationException("DeleteProfileButton is missing from the window.");
            NewButton = window.FindControl<Button>("NewProfileButton")
                ?? throw new InvalidOperationException("NewProfileButton is missing from the window.");
        }

        public MainWindow Window { get; }
        public ComboBox ProfileComboBox { get; }
        public ComboBox DeviceProfileCombo { get; }
        public TextBox NameBox { get; }
        public Button RenameButton { get; }
        public Button DeleteButton { get; }
        public Button NewButton { get; }

        public void Select(string profile)
        {
            ProfileComboBox.SelectedItem = profile;
            Dispatcher.UIThread.RunJobs();
        }

        public void Click(Button button)
        {
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
        }

        public void Dispose()
        {
            Window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }
}

namespace OpenWiimote.App;

public sealed record ProfileMappingRow(
    string Target,
    string WiimoteButton,
    string NunchukButton,
    string ClassicButton);

public sealed record AvatarThemeOption(string Key, string Name);

public sealed record LogEntry(DateTime Timestamp, string Message, string Level)
{
    public string TimeText => Timestamp.ToString("HH:mm:ss");
}

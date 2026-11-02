using System.Text.Json;
using OpenWiimote.Core.Settings;
using Xunit;

namespace OpenWiimote.Core.Tests;

public sealed class SettingsStoreTests
{
    [Fact]
    public async Task SavesPreserveUnknownSettingsAndDoNotShareTemporaryFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "openwiimote-settings-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SettingsStore(root);
            File.WriteAllText(store.SettingsPath, "{\"AutoConnect\":true,\"FutureOptions\":{\"mode\":\"keep-me\"}}");
            var settings = store.Load();
            await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() => store.Save(settings))));
            using var saved = JsonDocument.Parse(File.ReadAllText(store.SettingsPath));
            Assert.Equal("keep-me", saved.RootElement.GetProperty("FutureOptions").GetProperty("mode").GetString());
            Assert.Empty(Directory.GetFiles(root, "*.tmp"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}

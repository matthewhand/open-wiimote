using Avalonia;
using Avalonia.Headless;
using OpenWiimote.App;

[assembly: AvaloniaTestApplication(typeof(OpenWiimote.App.Tests.TestAppBuilder))]

namespace OpenWiimote.App.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }
}

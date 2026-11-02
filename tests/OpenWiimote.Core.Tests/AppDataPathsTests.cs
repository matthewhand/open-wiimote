using OpenWiimote.Core.Storage;
using Xunit;

namespace OpenWiimote.Core.Tests;

public sealed class AppDataPathsTests
{
    [Fact]
    public void XdgConfigHomeWinsOverTheFrameworkFolder()
    {
        var resolved = AppDataPaths.Resolve("/tmp/xdg", "/home/user/.config", "/home/user", "/app");

        Assert.Equal("/tmp/xdg", resolved);
    }

    [Fact]
    public void TheApplicationDataFolderIsUsedWithoutXdg()
    {
        var resolved = AppDataPaths.Resolve(null, "/home/user/.config", "/home/user", "/app");

        Assert.Equal("/home/user/.config", resolved);
    }

    [Fact]
    public void TheHomeDirectoryIsTheSecondFallback()
    {
        var resolved = AppDataPaths.Resolve(null, string.Empty, "/home/user", "/app");

        Assert.Equal(Path.Combine("/home/user", ".config"), resolved);
    }

    [Fact]
    public void BlankValuesAreIgnoredAndTheApplicationDirectoryIsTheLastResort()
    {
        var resolved = AppDataPaths.Resolve("   ", "  ", null, "/app");

        Assert.Equal("/app", resolved);
    }

    [Fact]
    public void TheResultIsNeverARelativePath()
    {
        // A relative result is what used to put the profile and log folders into the
        // directory the app was started from.
        Assert.True(Path.IsPathRooted(AppDataPaths.Resolve()));
        Assert.True(Path.IsPathRooted(AppDataPaths.ResolveOpenWiimoteDirectory()));
    }

    [Fact]
    public void TheOpenWiimoteDirectorySitsInsideTheResolvedRoot()
    {
        Assert.EndsWith(
            Path.Combine("OpenWiimote"),
            AppDataPaths.ResolveOpenWiimoteDirectory());
    }
}

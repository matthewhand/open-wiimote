using OpenWiimote.App.Headless;
using Xunit;

namespace OpenWiimote.Core.Tests;

public sealed class WebRootPathResolverTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "OpenWiimote.Tests",
        Guid.NewGuid().ToString("N"));

    public WebRootPathResolverTests()
    {
        var webRoot = Path.Combine(_root, "wwwroot");
        Directory.CreateDirectory(webRoot);
        Directory.CreateDirectory(Path.Combine(_root, "wwwroot-secret"));
        File.WriteAllText(Path.Combine(webRoot, "index.html"), "index");
        File.WriteAllText(Path.Combine(_root, "wwwroot-secret", "secret.txt"), "secret");
    }

    [Fact]
    public void TryResolve_ServesFilesInsideWebRoot()
    {
        var webRoot = Path.Combine(_root, "wwwroot");

        var resolved = WebRootPathResolver.TryResolve(webRoot, "/index.html", out var fullPath);

        Assert.True(resolved);
        Assert.Equal(Path.Combine(webRoot, "index.html"), fullPath);
    }

    [Fact]
    public void TryResolve_EmptyPathMapsToIndex()
    {
        var webRoot = Path.Combine(_root, "wwwroot");

        var resolved = WebRootPathResolver.TryResolve(webRoot, "/", out var fullPath);

        Assert.True(resolved);
        Assert.Equal(Path.Combine(webRoot, "index.html"), fullPath);
    }

    [Fact]
    public void TryResolve_RejectsParentTraversal()
    {
        var webRoot = Path.Combine(_root, "wwwroot");

        var resolved = WebRootPathResolver.TryResolve(webRoot, "/../wwwroot-secret/secret.txt", out _);

        Assert.False(resolved);
    }

    [Fact]
    public void TryResolve_RejectsEncodedParentTraversal()
    {
        var webRoot = Path.Combine(_root, "wwwroot");

        var resolved = WebRootPathResolver.TryResolve(webRoot, "/%2e%2e/wwwroot-secret/secret.txt", out _);

        Assert.False(resolved);
    }

    [Fact]
    public void TryResolve_RejectsWindowsSeparatorTraversal()
    {
        var webRoot = Path.Combine(_root, "wwwroot");

        // A backslash is a directory separator on Windows and would otherwise escape the root.
        var resolved = WebRootPathResolver.TryResolve(webRoot, "/..%5cwwwroot-secret%5csecret.txt", out _);

        Assert.False(resolved);
    }

    [Fact]
    public void TryResolve_TreatsAbsoluteUrlPathAsRootRelative()
    {
        var webRoot = Path.Combine(_root, "wwwroot");

        // An absolute looking url path is served from inside the web root, never from the
        // matching system location.
        var resolved = WebRootPathResolver.TryResolve(webRoot, "/etc/passwd", out var fullPath);

        Assert.True(resolved);
        Assert.Equal(Path.Combine(webRoot, "etc", "passwd"), fullPath);
        Assert.StartsWith(webRoot, fullPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryResolve_RejectsSiblingDirectoryWithSharedPrefix()
    {
        var webRoot = Path.Combine(_root, "wwwroot");

        // "wwwroot-secret" starts with "wwwroot" but is not inside it.
        var resolved = WebRootPathResolver.TryResolve(webRoot, "/../wwwroot-secret/secret.txt", out _);

        Assert.False(resolved);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}

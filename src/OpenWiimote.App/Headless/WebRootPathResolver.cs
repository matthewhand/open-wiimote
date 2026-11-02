namespace OpenWiimote.App.Headless;

/// <summary>
/// Resolves request paths against the static web root of the headless service.
/// </summary>
/// <remarks>
/// Request paths are untrusted input. A path that resolves outside the web root must be
/// rejected instead of being served, which also stops sibling directories such as
/// "wwwroot-backup" from being reachable through a prefix match.
/// </remarks>
public static class WebRootPathResolver
{
    /// <summary>
    /// Resolves <paramref name="requestPath"/> to a file inside <paramref name="webRoot"/>.
    /// </summary>
    /// <returns><c>true</c> when the path stays inside the web root.</returns>
    public static bool TryResolve(string webRoot, string requestPath, out string fullPath)
    {
        fullPath = string.Empty;

        if (string.IsNullOrWhiteSpace(webRoot))
        {
            return false;
        }

        var relative = (requestPath ?? string.Empty).TrimStart('/');
        if (string.IsNullOrWhiteSpace(relative))
        {
            relative = "index.html";
        }

        relative = Uri.UnescapeDataString(relative)
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

        // An absolute or rooted path must never be combined with the web root.
        if (Path.IsPathRooted(relative))
        {
            return false;
        }

        string rootFull;
        string combined;
        try
        {
            rootFull = Path.GetFullPath(webRoot);
            combined = Path.GetFullPath(Path.Combine(rootFull, relative));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        var rootPrefix = rootFull.EndsWith(Path.DirectorySeparatorChar)
            ? rootFull
            : rootFull + Path.DirectorySeparatorChar;

        if (!combined.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        fullPath = combined;
        return true;
    }
}

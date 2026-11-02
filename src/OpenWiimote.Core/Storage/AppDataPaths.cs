namespace OpenWiimote.Core.Storage;

/// <summary>
/// Finds the per-user directory that holds the settings, the profiles and the logs.
/// </summary>
/// <remarks>
/// The framework returns an empty string when it cannot resolve the application data
/// folder. Combining that empty string with "OpenWiimote" produces a relative path, which
/// silently writes the profile and log folders into whatever directory the app was started
/// from. Every store therefore asks this type for the directory instead of calling
/// <see cref="Environment.GetFolderPath(Environment.SpecialFolder)"/> itself.
/// </remarks>
public static class AppDataPaths
{
    /// <summary>The base directory for the app's own files.</summary>
    public static string Resolve()
    {
        return Resolve(
            Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            AppContext.BaseDirectory);
    }

    /// <summary>
    /// The resolution order, exposed so the fallbacks can be tested without changing the
    /// environment of the test run: XDG_CONFIG_HOME, the application data folder, the home
    /// directory, then the application directory.
    /// </summary>
    public static string Resolve(
        string? xdgConfigHome,
        string? applicationData,
        string? userProfile,
        string? applicationDirectory)
    {
        if (!string.IsNullOrWhiteSpace(xdgConfigHome))
        {
            return xdgConfigHome.Trim();
        }

        if (!string.IsNullOrWhiteSpace(applicationData))
        {
            return applicationData;
        }

        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            return Path.Combine(userProfile, ".config");
        }

        return string.IsNullOrWhiteSpace(applicationDirectory)
            ? Path.GetTempPath()
            : applicationDirectory;
    }

    /// <summary>The directory the profiles, settings and logs live in.</summary>
    public static string ResolveOpenWiimoteDirectory()
    {
        return Path.Combine(Resolve(), "OpenWiimote");
    }
}

using System.Reflection;

namespace Firetail;

/// <summary>
/// Provides version information for the Firetail package.
/// </summary>
public static class FiretailVersion
{
    /// <summary>
    /// Gets the current version of the Firetail package.
    /// </summary>
    public static string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";

    /// <summary>
    /// Gets the informational version of the Firetail package, which may include additional details like commit hash.
    /// </summary>
    public static string InformationalVersion => 
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "Unknown";
} 
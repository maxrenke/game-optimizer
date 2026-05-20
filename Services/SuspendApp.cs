namespace GameOptimizer.Services;

/// <summary>
/// A background app to fully suspend (freeze) while a game is running and
/// resume when it closes. Cloud-sync clients such as OneDrive are the typical
/// entries - they cause mid-game disk-I/O stutter and are safe to pause briefly.
/// </summary>
public class SuspendApp
{
    /// <summary>Process name to match, case-insensitive, without the ".exe" suffix.</summary>
    public string ProcessName { get; set; } = "";

    /// <summary>When false the entry stays in the list but is not suspended.</summary>
    public bool Enabled { get; set; } = true;
}

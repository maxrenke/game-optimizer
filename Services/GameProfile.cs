using System.Text.Json.Serialization;

namespace GameOptimizer.Services;

/// <summary>
/// An optional per-game override for CPU affinity and process priority.
/// When no profile matches a detected game, the global <see cref="OptimizerConfig.GameAffinityMask"/>
/// and High priority are used.
/// </summary>
public class GameProfile
{
    /// <summary>Process name to match, case-insensitive, stored without the ".exe" suffix.</summary>
    public string ProcessName { get; set; } = "";

    /// <summary>Affinity mask to apply. 0 means "use the global game affinity mask".</summary>
    public long AffinityMask { get; set; }

    /// <summary>
    /// Priority class name: Idle, BelowNormal, Normal, AboveNormal, High, or RealTime.
    /// Invalid values fall back to High.
    /// </summary>
    public string Priority { get; set; } = "High";

    /// <summary>One-line description for display in the Settings list.</summary>
    [JsonIgnore]
    public string Summary =>
        $"{ProcessName}   -   {(AffinityMask == 0 ? "default cores" : $"0x{AffinityMask:X}")}   -   {Priority}";
}

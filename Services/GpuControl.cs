using System.Diagnostics;

namespace GameOptimizer.Services;

/// <summary>
/// Pins NVIDIA GPU clocks while a game runs to prevent P-state oscillation
/// (a microstutter source). Locks graphics clocks to the card's maximum and
/// resets them afterward. All operations are best-effort and a no-op when
/// nvidia-smi is unavailable or the call lacks privileges.
/// </summary>
public static class GpuControl
{
    /// <summary>
    /// Locks graphics clocks to the card's maximum. Returns true on success.
    /// </summary>
    public static async Task<bool> LockClocksMaxAsync()
    {
        var max = await QueryMaxGraphicsClockAsync();
        if (max <= 0) return false;
        return await RunSmiAsync($"--lock-gpu-clocks={max},{max}");
    }

    /// <summary>Releases any clock lock, returning the card to dynamic clocking.</summary>
    public static Task<bool> ResetClocksAsync() => RunSmiAsync("--reset-gpu-clocks");

    private static async Task<int> QueryMaxGraphicsClockAsync()
    {
        var output = await CaptureSmiAsync(
            "--query-gpu=clocks.max.graphics --format=csv,noheader,nounits");
        if (output is null) return 0;
        var first = output.Split('\n')[0].Trim();
        return int.TryParse(first, out var mhz) ? mhz : 0;
    }

    private static async Task<bool> RunSmiAsync(string args)
        => await CaptureSmiAsync(args) is not null;

    // Runs nvidia-smi, draining both pipes to avoid a buffer-fill deadlock, with
    // a 3s timeout. Returns stdout on a clean exit, null on any failure.
    private static async Task<string?> CaptureSmiAsync(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("nvidia-smi", args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc is null) return null;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try
            {
                var stdoutTask = proc.StandardOutput.ReadToEndAsync(timeout.Token);
                var stderrTask = proc.StandardError.ReadToEndAsync(timeout.Token);
                var output = await stdoutTask;
                await stderrTask;
                await proc.WaitForExitAsync(timeout.Token);
                return proc.ExitCode == 0 ? output : null;
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(); } catch { }
                return null;
            }
        }
        catch { return null; }
    }
}

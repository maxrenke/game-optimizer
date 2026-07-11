using System.Net.NetworkInformation;

namespace GameOptimizer.Services;

/// <summary>
/// Pings a reference host to measure round-trip latency and jitter - the
/// metrics that actually predict online-game smoothness. Background traffic
/// (downloads, cloud sync) hurts games by inflating these, not by raw volume.
/// </summary>
public class LatencyMonitor
{
    public const int HistorySize = 120;

    private string _host;
    private readonly int[] _history = new int[HistorySize];
    private int _idx;
    private int _prevMs = -1;
    private double _jitter;

    public LatencyMonitor(string host)
    {
        _host = Normalize(host);
    }

    /// <summary>Host pinged for each sample. Settable so config changes apply live.</summary>
    public string Host
    {
        get => _host;
        set => _host = Normalize(value);
    }

    private static string Normalize(string host) =>
        string.IsNullOrWhiteSpace(host) ? "1.1.1.1" : host.Trim();

    /// <summary>Most recent successful round-trip time in ms; 0 before any reading.</summary>
    public int LastMs { get; private set; }

    /// <summary>Smoothed jitter in ms (RFC 3550-style running estimate).</summary>
    public int JitterMs { get; private set; }

    /// <summary>True if the most recent ping timed out or failed.</summary>
    public bool LastFailed { get; private set; }

    public int[] History => _history;
    public int HistoryIndex => _idx;

    /// <summary>Sends one ping and folds the result into the running state.</summary>
    public async Task SampleAsync()
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(_host, TimeSpan.FromSeconds(1));
            if (reply.Status == IPStatus.Success)
            {
                LastFailed = false;
                var ms = (int)Math.Clamp(reply.RoundtripTime, 0, 9999);
                LastMs = ms;
                _history[_idx] = ms;
                _idx = (_idx + 1) % HistorySize;

                // RFC 3550 jitter: smoothed mean deviation between consecutive samples
                if (_prevMs >= 0)
                    _jitter += (Math.Abs(ms - _prevMs) - _jitter) / 16.0;
                _prevMs = ms;
                JitterMs = (int)Math.Round(_jitter);
            }
            else
            {
                LastFailed = true;
            }
        }
        catch
        {
            LastFailed = true;
        }
    }
}

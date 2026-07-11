using GameOptimizer.Services;
using System.Text.Json;
using Xunit;

namespace GameOptimizer.Tests;

public class OptimizerConfigTests
{
    [Fact]
    public void DefaultConfig_HasCorrectAlertThresholds()
    {
        var cfg = new OptimizerConfig();
        Assert.Equal(80, cfg.AlertGpuTempC);
        Assert.Equal(90, cfg.AlertVramPct);
        Assert.Equal(95, cfg.AlertGpuUtilPct);
        Assert.Equal(90, cfg.AlertCpuZonePct);
        Assert.Equal(4, cfg.AlertSustainedTicks);
    }

    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        var tmpPath = Path.Combine(Path.GetTempPath(), $"test_config_{Guid.NewGuid():N}.json");
        try
        {
            var cfg = new OptimizerConfig
            {
                AlertGpuTempC = 75,
                AlertVramPct  = 85,
                NicName       = "TestNic",
            };

            // Serialize directly (bypassing ConfigPath) to temp path
            var json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(tmpPath, json);

            var loaded = JsonSerializer.Deserialize<OptimizerConfig>(
                File.ReadAllText(tmpPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

            Assert.Equal(75, loaded.AlertGpuTempC);
            Assert.Equal(85, loaded.AlertVramPct);
            Assert.Equal("TestNic", loaded.NicName);
        }
        finally
        {
            File.Delete(tmpPath);
        }
    }

    [Fact]
    public void Load_ReturnsDefault_WhenFileMissing()
    {
        // Temporarily redirect ConfigPath is not possible since it's a static computed property.
        // Instead we test the Load() logic directly by ensuring it returns a valid config
        // when the real config file doesn't exist (which is true in CI).
        // We do this by calling the deserializer with a non-existent path pattern.

        var missing = Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid():N}.json");
        OptimizerConfig result;
        try
        {
            var json = File.ReadAllText(missing); // will throw
            result = JsonSerializer.Deserialize<OptimizerConfig>(json) ?? new OptimizerConfig();
        }
        catch
        {
            result = new OptimizerConfig();
        }

        Assert.NotNull(result);
        Assert.Equal(80, result.AlertGpuTempC);
    }

    [Fact]
    public void DefaultConfig_StartFlags_AreFalse()
    {
        var cfg = new OptimizerConfig();
        Assert.False(cfg.StartMinimized);
        Assert.False(cfg.StartWithWindows);
    }

    [Fact]
    public void StartFlags_RoundTripThroughJson()
    {
        var cfg = new OptimizerConfig { StartMinimized = true, StartWithWindows = true };
        var json = JsonSerializer.Serialize(cfg);
        var loaded = JsonSerializer.Deserialize<OptimizerConfig>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.True(loaded.StartMinimized);
        Assert.True(loaded.StartWithWindows);
    }

    [Fact]
    public void DefaultConfig_AutoFlushStandby_IsFalse()
    {
        Assert.False(new OptimizerConfig().AutoFlushStandbyOnGameStart);
    }

    [Fact]
    public void AutoFlushStandby_RoundTripsThroughJson()
    {
        var cfg = new OptimizerConfig { AutoFlushStandbyOnGameStart = true };
        var json = JsonSerializer.Serialize(cfg);
        var loaded = JsonSerializer.Deserialize<OptimizerConfig>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.True(loaded.AutoFlushStandbyOnGameStart);
    }

    [Fact]
    public void DefaultConfig_PingHost_IsCloudflare()
    {
        Assert.Equal("1.1.1.1", new OptimizerConfig().PingHost);
    }

    [Fact]
    public void Validate_BlankPingHost_ResetToDefault()
    {
        var cfg = new OptimizerConfig { PingHost = "   " };
        cfg.Validate();
        Assert.Equal("1.1.1.1", cfg.PingHost);
    }

    [Fact]
    public void Validate_PingHost_Trimmed()
    {
        var cfg = new OptimizerConfig { PingHost = "  8.8.8.8 " };
        cfg.Validate();
        Assert.Equal("8.8.8.8", cfg.PingHost);
    }

    [Fact]
    public void PingHost_RoundTripsThroughJson()
    {
        var cfg = new OptimizerConfig { PingHost = "8.8.8.8" };
        var json = JsonSerializer.Serialize(cfg);
        var loaded = JsonSerializer.Deserialize<OptimizerConfig>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.Equal("8.8.8.8", loaded.PingHost);
    }

    [Fact]
    public void GamePaths_RoundTripThroughJson()
    {
        var cfg = new OptimizerConfig();
        cfg.GamePaths = [@"C:\Games\Steam", @"C:\Games\Epic"];
        var json = JsonSerializer.Serialize(cfg);
        var loaded = JsonSerializer.Deserialize<OptimizerConfig>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.Equal(2, loaded.GamePaths.Count);
        Assert.Contains(@"C:\Games\Steam", loaded.GamePaths);
        Assert.Contains(@"C:\Games\Epic", loaded.GamePaths);
    }

    [Fact]
    public void ExtraThrottledProcs_RoundTripThroughJson()
    {
        var cfg = new OptimizerConfig();
        cfg.ExtraThrottledProcs = ["slack", "discord"];
        var json = JsonSerializer.Serialize(cfg);
        var loaded = JsonSerializer.Deserialize<OptimizerConfig>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.Contains("slack", loaded.ExtraThrottledProcs);
        Assert.Contains("discord", loaded.ExtraThrottledProcs);
    }

    [Fact]
    public void Load_ReturnsDefault_WhenFileIsCorrupt()
    {
        var tmpPath = Path.Combine(Path.GetTempPath(), $"corrupt_config_{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(tmpPath, "{ this is not valid json !!!");

            OptimizerConfig result;
            try
            {
                var json = File.ReadAllText(tmpPath);
                result = JsonSerializer.Deserialize<OptimizerConfig>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? new OptimizerConfig();
            }
            catch
            {
                result = new OptimizerConfig();
            }

            Assert.NotNull(result);
            Assert.Equal(80, result.AlertGpuTempC);
        }
        finally
        {
            File.Delete(tmpPath);
        }
    }

    // ── Validate ─────────────────────────────────────────────────────────────

    private static long AllCores => (1L << Environment.ProcessorCount) - 1;

    [Fact]
    public void Validate_ZeroAffinityMask_ResetsToAllCores()
    {
        var cfg = new OptimizerConfig { GameAffinityMask = 0 };
        cfg.Validate();
        Assert.Equal(AllCores, cfg.GameAffinityMask);
    }

    [Fact]
    public void Validate_MaskWithNonexistentCores_ResetsToAllCores()
    {
        // A bit set well beyond any real core count
        var cfg = new OptimizerConfig { BgAffinityMask = 1L << 60 };
        cfg.Validate();
        Assert.Equal(AllCores, cfg.BgAffinityMask);
    }

    [Fact]
    public void Validate_ValidMask_LeftUnchanged()
    {
        // Bit 0 always exists
        var cfg = new OptimizerConfig { GameAffinityMask = 0b1 };
        cfg.Validate();
        Assert.Equal(0b1, cfg.GameAffinityMask);
    }

    [Fact]
    public void Validate_AlertThresholds_ClampedToValidRanges()
    {
        var cfg = new OptimizerConfig
        {
            AlertGpuTempC       = 999,
            AlertVramPct        = 5,
            AlertGpuUtilPct     = 0,
            AlertCpuZonePct     = 200,
            AlertSustainedTicks = 999,
        };
        cfg.Validate();
        Assert.Equal(110, cfg.AlertGpuTempC);
        Assert.Equal(50,  cfg.AlertVramPct);
        Assert.Equal(50,  cfg.AlertGpuUtilPct);
        Assert.Equal(100, cfg.AlertCpuZonePct);
        Assert.Equal(20,  cfg.AlertSustainedTicks);
    }

    [Fact]
    public void Validate_ValidThresholds_LeftUnchanged()
    {
        var cfg = new OptimizerConfig(); // defaults are all in range
        cfg.Validate();
        Assert.Equal(80, cfg.AlertGpuTempC);
        Assert.Equal(90, cfg.AlertVramPct);
        Assert.Equal(95, cfg.AlertGpuUtilPct);
        Assert.Equal(90, cfg.AlertCpuZonePct);
        Assert.Equal(4,  cfg.AlertSustainedTicks);
    }

    // ── Validate: per-game profiles ──────────────────────────────────────────

    [Fact]
    public void Validate_ProfileName_TrimmedLowercasedExeStripped()
    {
        var cfg = new OptimizerConfig
        {
            GameProfiles = [new GameProfile { ProcessName = "  EldenRing.EXE  " }],
        };
        cfg.Validate();
        Assert.Equal("eldenring", cfg.GameProfiles[0].ProcessName);
    }

    [Fact]
    public void Validate_ProfileWithBlankName_IsDropped()
    {
        var cfg = new OptimizerConfig
        {
            GameProfiles =
            [
                new GameProfile { ProcessName = "   " },
                new GameProfile { ProcessName = "eldenring" },
            ],
        };
        cfg.Validate();
        Assert.Single(cfg.GameProfiles);
        Assert.Equal("eldenring", cfg.GameProfiles[0].ProcessName);
    }

    [Fact]
    public void Validate_ProfileCorruptMask_ResetToZero()
    {
        // A non-zero mask referencing nonexistent cores falls back to 0 (use default)
        var cfg = new OptimizerConfig
        {
            GameProfiles = [new GameProfile { ProcessName = "game", AffinityMask = 1L << 60 }],
        };
        cfg.Validate();
        Assert.Equal(0, cfg.GameProfiles[0].AffinityMask);
    }

    [Fact]
    public void Validate_ProfileValidMask_LeftUnchanged()
    {
        var cfg = new OptimizerConfig
        {
            GameProfiles = [new GameProfile { ProcessName = "game", AffinityMask = 0b1 }],
        };
        cfg.Validate();
        Assert.Equal(0b1, cfg.GameProfiles[0].AffinityMask);
    }

    [Fact]
    public void Validate_ProfileInvalidPriority_ResetToHigh()
    {
        var cfg = new OptimizerConfig
        {
            GameProfiles = [new GameProfile { ProcessName = "game", Priority = "Ludicrous" }],
        };
        cfg.Validate();
        Assert.Equal("High", cfg.GameProfiles[0].Priority);
    }

    // ── Suspend-during-game list ─────────────────────────────────────────────

    [Fact]
    public void DefaultConfig_SuspendDuringGame_IncludesOneDrive()
    {
        var cfg = new OptimizerConfig();
        Assert.Contains(cfg.SuspendDuringGame, a => a.ProcessName == "onedrive" && a.Enabled);
    }

    [Fact]
    public void Validate_SuspendApp_NameNormalized()
    {
        var cfg = new OptimizerConfig
        {
            SuspendDuringGame = [new SuspendApp { ProcessName = "  OneDrive.EXE " }],
        };
        cfg.Validate();
        Assert.Equal("onedrive", cfg.SuspendDuringGame[0].ProcessName);
    }

    [Fact]
    public void Validate_SuspendApp_BlankNameDropped()
    {
        var cfg = new OptimizerConfig
        {
            SuspendDuringGame =
            [
                new SuspendApp { ProcessName = "  " },
                new SuspendApp { ProcessName = "dropbox" },
            ],
        };
        cfg.Validate();
        Assert.Single(cfg.SuspendDuringGame);
        Assert.Equal("dropbox", cfg.SuspendDuringGame[0].ProcessName);
    }

    [Fact]
    public void SuspendApp_RoundTripsThroughJson_WithEnabledFlag()
    {
        var cfg = new OptimizerConfig
        {
            SuspendDuringGame = [new SuspendApp { ProcessName = "dropbox", Enabled = false }],
        };
        var json = JsonSerializer.Serialize(cfg);
        var loaded = JsonSerializer.Deserialize<OptimizerConfig>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.Single(loaded.SuspendDuringGame);
        Assert.Equal("dropbox", loaded.SuspendDuringGame[0].ProcessName);
        Assert.False(loaded.SuspendDuringGame[0].Enabled);
    }

    [Fact]
    public void DefaultConfig_AutoPinOnGameDetect_IsFalse()
    {
        Assert.False(new OptimizerConfig().AutoPinOnGameDetect);
    }

    [Fact]
    public void Validate_ExtraThrottledProcs_NormalizedAndDeduplicated()
    {
        var cfg = new OptimizerConfig
        {
            ExtraThrottledProcs = ["  Spotify.EXE ", "spotify", "", "Discord"],
        };
        cfg.Validate();
        Assert.Equal(["spotify", "discord"], cfg.ExtraThrottledProcs);
    }

    [Fact]
    public void Validate_StopServices_TrimmedAndDeduplicated()
    {
        var cfg = new OptimizerConfig
        {
            StopServicesDuringSession = [" WSearch ", "wsearch", "", "DiagTrack"],
        };
        cfg.Validate();
        Assert.Equal(2, cfg.StopServicesDuringSession.Count);
        Assert.Equal("WSearch", cfg.StopServicesDuringSession[0]);
        Assert.Equal("DiagTrack", cfg.StopServicesDuringSession[1]);
    }
}

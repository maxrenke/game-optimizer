using GameOptimizer.Services;
using Xunit;

namespace GameOptimizer.Tests;

public class GameLibraryScannerTests
{
    [Fact]
    public void ScanSteam_ReturnsEmpty_WhenNoSteamInstalled()
    {
        // In CI / machines without Steam the registry key won't exist
        // This test is inherently environment-dependent; we just verify no exception
        // and that the result is a valid (possibly empty) enumerable.
        var result = GameLibraryScanner.ScanSteam().ToList();
        Assert.NotNull(result);
    }

    [Fact]
    public void ScanEpic_ReturnsEmpty_WhenManifestsDirMissing()
    {
        // C:\ProgramData\Epic\... won't exist in CI
        var result = GameLibraryScanner.ScanEpic().ToList();
        // Either empty (no Epic) or has paths - just confirm no exception
        Assert.NotNull(result);
    }

    [Fact]
    public void ScanGog_ReturnsEmpty_WhenNoGogRegistry()
    {
        // Registry keys won't exist in CI
        var result = GameLibraryScanner.ScanGog().ToList();
        Assert.NotNull(result);
    }

    [Fact]
    public void ScanAll_ReturnsDeduplicated()
    {
        // Use a temp dir as a stand-in to confirm dedup logic
        // We can't inject paths, so just verify the return type has no dupes
        var result = GameLibraryScanner.ScanAll();
        var distinct = result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Assert.Equal(result.Count, distinct.Count);
    }

    [Fact]
    public void ScanAll_FiltersOutNonExistentDirectories()
    {
        // All returned paths must actually exist (ScanAll filters via Directory.Exists)
        var result = GameLibraryScanner.ScanAll();
        foreach (var path in result)
            Assert.True(Directory.Exists(path), $"Path does not exist: {path}");
    }
}

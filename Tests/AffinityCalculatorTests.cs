using GameOptimizer.Services;
using Xunit;

namespace GameOptimizer.Tests;

public class AffinityCalculatorTests
{
    [Fact]
    public void FourCores_GameGets2_Media1_Bg1()
    {
        var z = AffinityCalculator.FromCoreCount(4);
        Assert.Equal(4, z.TotalCores);
        Assert.Equal(2, BitCount(z.GameMask));
        Assert.Equal(1, BitCount(z.MediaMask));
        Assert.Equal(1, BitCount(z.BgMask));
    }

    [Fact]
    public void EightCores_GameGets6_Media1_Bg1()
    {
        var z = AffinityCalculator.FromCoreCount(8);
        Assert.Equal(8, z.TotalCores);
        Assert.Equal(6, BitCount(z.GameMask));
        Assert.Equal(1, BitCount(z.MediaMask));
        Assert.Equal(1, BitCount(z.BgMask));
    }

    [Fact]
    public void SixteenCores_GameGets12_Media2_Bg2()
    {
        var z = AffinityCalculator.FromCoreCount(16);
        Assert.Equal(16, z.TotalCores);
        Assert.Equal(12, BitCount(z.GameMask));
        Assert.Equal(2, BitCount(z.MediaMask));
        Assert.Equal(2, BitCount(z.BgMask));
    }

    [Fact]
    public void Masks_AreDisjoint()
    {
        foreach (var count in new[] { 4, 8, 16 })
        {
            var z = AffinityCalculator.FromCoreCount(count);
            Assert.Equal(0L, z.GameMask & z.MediaMask);
            Assert.Equal(0L, z.GameMask & z.BgMask);
            Assert.Equal(0L, z.MediaMask & z.BgMask);
        }
    }

    [Fact]
    public void Masks_CoverAllCores()
    {
        foreach (var count in new[] { 4, 8, 16 })
        {
            var z = AffinityCalculator.FromCoreCount(count);
            var allCores = (1L << count) - 1;
            Assert.Equal(allCores, z.GameMask | z.MediaMask | z.BgMask);
        }
    }

    [Fact]
    public void TwoCores_AllZonesGetAllCores()
    {
        var z = AffinityCalculator.FromCoreCount(2);
        var all = (1L << 2) - 1; // 0b11
        Assert.Equal(all, z.GameMask);
        Assert.Equal(all, z.MediaMask);
        Assert.Equal(all, z.BgMask);
    }

    private static int BitCount(long mask)
    {
        int count = 0;
        while (mask != 0) { count += (int)(mask & 1); mask >>= 1; }
        return count;
    }
}

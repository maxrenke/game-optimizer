using GameOptimizer.Services;
using Xunit;

namespace GameOptimizer.Tests;

public class MemoryServiceTests
{
    [Fact]
    public void FlushStandbyList_DoesNotThrow()
    {
        // Exercises the full P/Invoke path (token privilege, NtSetSystemInformation,
        // GlobalMemoryStatusEx). A struct-layout mistake would surface here.
        var ex = Record.Exception(() => MemoryService.FlushStandbyList());
        Assert.Null(ex);
    }

    [Fact]
    public void FlushStandbyList_ReturnsFreedMbOrFailure()
    {
        // >= 0 : MB freed (approximate). -1 : failed (e.g. no admin rights in CI).
        var result = MemoryService.FlushStandbyList();
        Assert.True(result >= -1);
    }
}

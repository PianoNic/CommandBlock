using CommandBlock.Application.Command.Server;

namespace CommandBlock.Tests;

public class MemoryParsingTests
{
    [Test]
    [Arguments("4G", 4294967296L)]   // 4 * 1024^3
    [Arguments("512M", 536870912L)]  // 512 * 1024^2
    [Arguments("1024K", 1048576L)]   // 1024 * 1024
    [Arguments("2048", 2147483648L)] // bare number = MB (itzg default unit)
    [Arguments("1.5G", 1610612736L)] // 1.5 * 1024^3
    [Arguments("", 0L)]
    [Arguments("nonsense", 0L)]
    [Arguments(null, 0L)]
    public async Task ParseMemoryBytes_Parses(string? input, long expected)
    {
        await Assert.That(ServerContainerSpec.ParseMemoryBytes(input)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("4G", 6442450944L)]   // 1.5x the heap
    [Arguments("2G", 3221225472L)]
    [Arguments("1G", 1610612736L)]
    [Arguments("512M", 1073741824L)] // 1.5x would be under the 512 MB minimum headroom
    [Arguments("nonsense", 0L)]      // unparseable -> no limit rather than an arbitrary one
    [Arguments(null, 0L)]
    public async Task ContainerMemoryLimitBytes_LeavesHeadroomAboveTheHeap(string? memory, long expected)
    {
        await Assert.That(ServerContainerSpec.ContainerMemoryLimitBytes(memory)).IsEqualTo(expected);
    }
}

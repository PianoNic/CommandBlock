using CommandBlock.Application.Queries.Host;

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
        await Assert.That(GetHostResourcesQueryHandler.ParseMemoryBytes(input)).IsEqualTo(expected);
    }
}

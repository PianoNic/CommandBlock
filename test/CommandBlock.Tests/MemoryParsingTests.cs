using CommandBlock.Application.Queries.Host;
using Xunit;

namespace CommandBlock.Tests;

public class MemoryParsingTests
{
    [Theory]
    [InlineData("4G", 4L * 1024 * 1024 * 1024)]
    [InlineData("512M", 512L * 1024 * 1024)]
    [InlineData("1024K", 1024L * 1024)]
    [InlineData("2048", 2048L * 1024 * 1024)] // bare number = MB (itzg default unit)
    [InlineData("1.5G", 1610612736L)]         // 1.5 * 1024^3
    [InlineData("", 0L)]
    [InlineData("nonsense", 0L)]
    [InlineData(null, 0L)]
    public void ParseMemoryBytes_Parses(string? input, long expected)
    {
        Assert.Equal(expected, GetHostResourcesQueryHandler.ParseMemoryBytes(input));
    }
}

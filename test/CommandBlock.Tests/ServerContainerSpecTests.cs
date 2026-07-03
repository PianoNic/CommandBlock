using CommandBlock.Application.Command.Server;
using CommandBlock.Domain;
using Xunit;

namespace CommandBlock.Tests;

public class ServerContainerSpecTests
{
    [Theory]
    [InlineData(null, "25")]      // unknown -> newest runtime
    [InlineData("", "25")]
    [InlineData("LATEST", "25")]  // no minor version -> newest
    [InlineData("1.22", "25")]
    [InlineData("1.21.5", "25")]  // 1.21.5+ moved to Java 25
    [InlineData("1.21.4", "21")]
    [InlineData("1.21", "21")]
    [InlineData("1.20.5", "21")]
    [InlineData("1.20.4", "17")]
    [InlineData("1.17", "17")]
    [InlineData("1.16.5", "8")]
    [InlineData("1.8.9", "8")]
    public void AutoJavaForMinecraft_PicksRuntime(string? version, string expected)
    {
        Assert.Equal(expected, ServerContainerSpec.AutoJavaForMinecraft(version));
    }

    [Theory]
    [InlineData("17", null, "java17")]     // explicit Java version wins
    [InlineData(null, "1.21.5", "java25")] // derived from the Minecraft version
    [InlineData(null, "1.16.5", "java8")]
    [InlineData("99", null, "latest")]     // unknown Java version -> latest tag
    public void ImageTag_MapsToItzgTag(string? java, string? version, string expected)
    {
        var s = new ServerInstance
        {
            ServerType = "PAPER",
            Memory = "2G",
            DisplayName = "test",
            Hostname = "test.example.com",
            Version = version,
            JavaVersion = java,
        };

        Assert.Equal(expected, ServerContainerSpec.ImageTag(s));
    }
}

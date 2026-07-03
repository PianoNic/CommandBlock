using CommandBlock.Application.Command.Server;
using CommandBlock.Domain;

namespace CommandBlock.Tests;

public class ServerContainerSpecTests
{
    [Test]
    [Arguments(null, "25")]      // unknown -> newest runtime
    [Arguments("", "25")]
    [Arguments("LATEST", "25")]  // no minor version -> newest
    [Arguments("1.22", "25")]
    [Arguments("1.21.5", "25")]  // 1.21.5+ moved to Java 25
    [Arguments("1.21.4", "21")]
    [Arguments("1.21", "21")]
    [Arguments("1.20.5", "21")]
    [Arguments("1.20.4", "17")]
    [Arguments("1.17", "17")]
    [Arguments("1.16.5", "8")]
    [Arguments("1.8.9", "8")]
    public async Task AutoJavaForMinecraft_PicksRuntime(string? version, string expected)
    {
        await Assert.That(ServerContainerSpec.AutoJavaForMinecraft(version)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("17", null, "java17")]     // explicit Java version wins
    [Arguments(null, "1.21.5", "java25")] // derived from the Minecraft version
    [Arguments(null, "1.16.5", "java8")]
    [Arguments("99", null, "latest")]     // unknown Java version -> latest tag
    public async Task ImageTag_MapsToItzgTag(string? java, string? version, string expected)
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

        await Assert.That(ServerContainerSpec.ImageTag(s)).IsEqualTo(expected);
    }
}

using CommandBlock.Application.Command.Server;
using CommandBlock.Application.Dtos.Server;

namespace CommandBlock.Tests;

public class ServerPropertiesFileTests
{
    private const string Sample =
        "#Minecraft server properties\n" +
        "motd=Welcome!\n" +
        "max-players=42\n" +
        "difficulty=hard\n" +
        "gamemode=creative\n" +
        "pvp=false\n" +
        "online-mode=false\n" +
        "white-list=true\n" +
        "hardcore=true\n" +
        "allow-flight=true\n" +
        "enable-command-block=true\n" +
        "view-distance=16\n" +
        "spawn-protection=0\n" +
        "level-seed=12345\n"; // unknown-to-us key we must preserve on write

    [Test]
    public async Task ToDto_ParsesCuratedKeys()
    {
        var dto = ServerPropertiesFile.ToDto(Sample);

        await Assert.That(dto.Available).IsTrue();
        await Assert.That(dto.Motd).IsEqualTo("Welcome!");
        await Assert.That(dto.MaxPlayers).IsEqualTo(42);
        await Assert.That(dto.Difficulty).IsEqualTo("hard");
        await Assert.That(dto.Gamemode).IsEqualTo("creative");
        await Assert.That(dto.Pvp).IsFalse();
        await Assert.That(dto.OnlineMode).IsFalse();
        await Assert.That(dto.Whitelist).IsTrue();
        await Assert.That(dto.Hardcore).IsTrue();
        await Assert.That(dto.AllowFlight).IsTrue();
        await Assert.That(dto.EnableCommandBlock).IsTrue();
        await Assert.That(dto.ViewDistance).IsEqualTo(16);
        await Assert.That(dto.SpawnProtection).IsEqualTo(0);
    }

    [Test]
    public async Task ToDto_UsesFallbacksForMissingKeys()
    {
        var dto = ServerPropertiesFile.ToDto("motd=Hi\n");

        await Assert.That(dto.Motd).IsEqualTo("Hi");
        await Assert.That(dto.MaxPlayers).IsEqualTo(20);
        await Assert.That(dto.Difficulty).IsEqualTo("easy");
        await Assert.That(dto.Gamemode).IsEqualTo("survival");
        await Assert.That(dto.Pvp).IsTrue();
        await Assert.That(dto.OnlineMode).IsTrue();
        await Assert.That(dto.Whitelist).IsFalse();
        await Assert.That(dto.ViewDistance).IsEqualTo(10);
        await Assert.That(dto.SpawnProtection).IsEqualTo(16);
    }

    [Test]
    public async Task ToDto_IgnoresCommentsBlankLinesAndCrlf()
    {
        var text = "# a comment\r\n! also a comment\r\n\r\nmax-players=7\r\n";
        var dto = ServerPropertiesFile.ToDto(text);

        await Assert.That(dto.MaxPlayers).IsEqualTo(7);
        // The commented "motd" style lines must not leak in as values.
        await Assert.That(dto.Motd).IsEqualTo("");
    }

    [Test]
    public async Task ApplyUpdate_ReplacesInPlaceAndPreservesUnknownKeysAndComments()
    {
        var update = new UpdateServerPropertiesDto
        {
            Motd = "New MOTD",
            MaxPlayers = 8,
            Difficulty = "normal",
            Gamemode = "adventure",
            Pvp = true,
            OnlineMode = true,
            Whitelist = false,
            Hardcore = false,
            AllowFlight = false,
            EnableCommandBlock = false,
            ViewDistance = 12,
            SpawnProtection = 4,
        };

        var result = ServerPropertiesFile.ApplyUpdate(Sample, update);

        // Comment header and the unknown key are preserved verbatim.
        await Assert.That(result).Contains("#Minecraft server properties");
        await Assert.That(result).Contains("level-seed=12345");
        // Curated keys are updated in place (no duplicate appended copies).
        await Assert.That(result).Contains("motd=New MOTD");
        await Assert.That(result).Contains("max-players=8");
        await Assert.That(result).Contains("difficulty=normal");

        // Reading it back reflects the update exactly.
        var round = ServerPropertiesFile.ToDto(result);
        await Assert.That(round.Motd).IsEqualTo("New MOTD");
        await Assert.That(round.MaxPlayers).IsEqualTo(8);
        await Assert.That(round.Gamemode).IsEqualTo("adventure");
        await Assert.That(round.ViewDistance).IsEqualTo(12);
        await Assert.That(round.SpawnProtection).IsEqualTo(4);
    }

    [Test]
    public async Task ApplyUpdate_DoesNotDuplicateKeys()
    {
        var update = new UpdateServerPropertiesDto { MaxPlayers = 99 };
        var result = ServerPropertiesFile.ApplyUpdate("max-players=1\n", update);

        var occurrences = result.Split('\n').Count(l => l.StartsWith("max-players="));
        await Assert.That(occurrences).IsEqualTo(1);
        await Assert.That(result).Contains("max-players=99");
    }

    [Test]
    public async Task ApplyUpdate_AppendsMissingCuratedKeys()
    {
        // Empty input: every curated key should be appended.
        var result = ServerPropertiesFile.ApplyUpdate("", new UpdateServerPropertiesDto { MaxPlayers = 5 });
        var round = ServerPropertiesFile.ToDto(result);

        await Assert.That(round.MaxPlayers).IsEqualTo(5);
        await Assert.That(result).Contains("difficulty=easy");
        await Assert.That(result).EndsWith("\n");
    }

    [Test]
    public async Task ApplyUpdate_StripsNewlinesFromMotd()
    {
        var update = new UpdateServerPropertiesDto { Motd = "line one\nline two\r\nline three" };
        var result = ServerPropertiesFile.ApplyUpdate("motd=old\n", update);

        // The written motd line must be a single line (no embedded newlines).
        var motdLine = result.Split('\n').Single(l => l.StartsWith("motd="));
        await Assert.That(motdLine).IsEqualTo("motd=line one line two line three");
    }
}

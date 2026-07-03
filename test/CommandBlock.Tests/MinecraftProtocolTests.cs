using System.Text;
using CommandBlock.API.Routing;
using Xunit;

namespace CommandBlock.Tests;

public class MinecraftProtocolTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(255)]
    [InlineData(300)]
    [InlineData(2097151)]
    [InlineData(int.MaxValue)]
    public void EncodeVarInt_RoundTripsThroughTryRead(int value)
    {
        var bytes = MinecraftProtocol.EncodeVarInt(value);
        var pos = 0;
        Assert.True(MinecraftProtocol.TryReadVarInt(bytes, ref pos, out var decoded));
        Assert.Equal(value, decoded);
        Assert.Equal(bytes.Length, pos); // consumed exactly the encoded bytes
    }

    [Fact]
    public void ParseHandshake_ReadsAllFields()
    {
        var body = BuildHandshakeBody(protocol: 765, address: "smp.example.com", port: 25565, nextState: 2);

        var hs = MinecraftProtocol.ParseHandshake(body);

        Assert.NotNull(hs);
        Assert.Equal(765, hs!.ProtocolVersion);
        Assert.Equal("smp.example.com", hs.ServerAddress);
        Assert.Equal((ushort)25565, hs.ServerPort);
        Assert.Equal(2, hs.NextState);
    }

    [Fact]
    public void ParseHandshake_RejectsWrongPacketId()
    {
        var body = new List<byte>();
        body.AddRange(MinecraftProtocol.EncodeVarInt(0x01)); // not a handshake (0x00)
        body.AddRange(MinecraftProtocol.EncodeVarInt(765));

        Assert.Null(MinecraftProtocol.ParseHandshake(body.ToArray()));
    }

    [Fact]
    public void ParseHandshake_RejectsTruncatedBuffer()
    {
        var body = BuildHandshakeBody(765, "host", 25565, 2);
        // Drop the trailing port + next-state bytes so the buffer runs out mid-parse.
        Assert.Null(MinecraftProtocol.ParseHandshake(body.AsSpan(0, body.Length - 3)));
    }

    [Theory]
    [InlineData("SMP.Example.Com", "smp.example.com")]
    [InlineData("host.example.com.", "host.example.com")]      // trailing FQDN dot
    [InlineData("  spaced.example.com  ", "spaced.example.com")]
    [InlineData("host.example.com\0FML3", "host.example.com")] // Forge/BungeeCord data after NUL
    public void SanitizeAddress_Normalizes(string raw, string expected)
    {
        Assert.Equal(expected, MinecraftProtocol.SanitizeAddress(raw));
    }

    private static byte[] BuildHandshakeBody(int protocol, string address, ushort port, int nextState)
    {
        var body = new List<byte>();
        body.AddRange(MinecraftProtocol.EncodeVarInt(0x00)); // packet id
        body.AddRange(MinecraftProtocol.EncodeVarInt(protocol));
        var addr = Encoding.UTF8.GetBytes(address);
        body.AddRange(MinecraftProtocol.EncodeVarInt(addr.Length));
        body.AddRange(addr);
        body.Add((byte)(port >> 8));   // big-endian unsigned short
        body.Add((byte)(port & 0xFF));
        body.AddRange(MinecraftProtocol.EncodeVarInt(nextState));
        return body.ToArray();
    }
}

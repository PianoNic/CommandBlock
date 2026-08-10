using System.Text;
using CommandBlock.API.Routing;

namespace CommandBlock.Tests;

public class MinecraftProtocolTests
{
    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(127)]
    [Arguments(128)]
    [Arguments(255)]
    [Arguments(300)]
    [Arguments(2097151)]
    [Arguments(int.MaxValue)]
    public async Task EncodeVarInt_RoundTripsThroughTryRead(int value)
    {
        var bytes = MinecraftProtocol.EncodeVarInt(value);
        var pos = 0;
        var ok = MinecraftProtocol.TryReadVarInt(bytes, ref pos, out var decoded);

        await Assert.That(ok).IsTrue();
        await Assert.That(decoded).IsEqualTo(value);
        await Assert.That(pos).IsEqualTo(bytes.Length); // consumed exactly the encoded bytes
    }

    [Test]
    public async Task ParseHandshake_ReadsAllFields()
    {
        var body = BuildHandshakeBody(protocol: 765, address: "smp.example.com", port: 25565, nextState: 2);

        var hs = MinecraftProtocol.ParseHandshake(body);

        await Assert.That(hs).IsNotNull();
        await Assert.That(hs!.ProtocolVersion).IsEqualTo(765);
        await Assert.That(hs.ServerAddress).IsEqualTo("smp.example.com");
        await Assert.That(hs.ServerPort).IsEqualTo((ushort)25565);
        await Assert.That(hs.NextState).IsEqualTo(2);
    }

    [Test]
    public async Task ParseHandshake_RejectsWrongPacketId()
    {
        var body = new List<byte>();
        body.AddRange(MinecraftProtocol.EncodeVarInt(0x01)); // not a handshake (0x00)
        body.AddRange(MinecraftProtocol.EncodeVarInt(765));

        await Assert.That(MinecraftProtocol.ParseHandshake(body.ToArray())).IsNull();
    }

    [Test]
    public async Task ParseHandshake_RejectsTruncatedBuffer()
    {
        var body = BuildHandshakeBody(765, "host", 25565, 2);
        var truncated = body[..^3]; // drop port + next-state so the buffer runs out mid-parse

        await Assert.That(MinecraftProtocol.ParseHandshake(truncated)).IsNull();
    }

    [Test]
    [Arguments("SMP.Example.Com", "smp.example.com")]
    [Arguments("host.example.com.", "host.example.com")]       // trailing FQDN dot
    [Arguments("  spaced.example.com  ", "spaced.example.com")]
    [Arguments("host.example.com\0FML3", "host.example.com")]  // Forge/BungeeCord data after NUL
    public async Task SanitizeAddress_Normalizes(string raw, string expected)
    {
        await Assert.That(MinecraftProtocol.SanitizeAddress(raw)).IsEqualTo(expected);
    }

    [Test]
    public async Task ParseLoginStartUsername_ReadsTheName()
    {
        var body = BuildLoginStartBody("Redstone_Rae");

        await Assert.That(MinecraftProtocol.ParseLoginStartUsername(body)).IsEqualTo("Redstone_Rae");
    }

    [Test]
    public async Task ParseLoginStartUsername_RejectsAnotherPacket()
    {
        // A handshake, not a Login Start - the router must not mistake its address field for a name.
        var body = BuildHandshakeBody(protocol: 765, address: "smp.example.com", port: 25565, nextState: 2);

        await Assert.That(MinecraftProtocol.ParseLoginStartUsername(body)).IsNull();
    }

    [Test]
    public async Task ParseLoginStartUsername_RejectsTruncatedName()
    {
        // Length says 12 bytes, only 4 follow: a short read must not walk off the end.
        var body = new List<byte>();
        body.AddRange(MinecraftProtocol.EncodeVarInt(0x00));
        body.AddRange(MinecraftProtocol.EncodeVarInt(12));
        body.AddRange(Encoding.UTF8.GetBytes("Alex"));

        await Assert.That(MinecraftProtocol.ParseLoginStartUsername(body.ToArray())).IsNull();
    }

    private static byte[] BuildLoginStartBody(string username)
    {
        var body = new List<byte>();
        body.AddRange(MinecraftProtocol.EncodeVarInt(0x00)); // packet id
        var name = Encoding.UTF8.GetBytes(username);
        body.AddRange(MinecraftProtocol.EncodeVarInt(name.Length));
        body.AddRange(name);
        body.AddRange(new byte[16]);                          // player uuid
        return body.ToArray();
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

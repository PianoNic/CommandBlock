using System.Text;

namespace CommandBlock.API.Routing
{
    /// <summary>
    /// Minimal Minecraft Java protocol reader - just enough to route by hostname. The first packet a
    /// client sends is the Handshake (id 0x00), which carries the server address the player typed.
    /// The router reads only that packet, then becomes a transparent byte pipe, so it never needs to
    /// understand login/play packets.
    ///
    /// Wire format of the handshake (all lengths are Minecraft VarInts):
    ///   [packet length: VarInt][packet id: VarInt = 0x00][protocol version: VarInt]
    ///   [server address: String (VarInt length + UTF-8)][server port: UnsignedShort][next state: VarInt]
    /// </summary>
    public static class MinecraftProtocol
    {
        /// <summary>The parsed handshake fields we care about. <see cref="NextState"/> is 1 for a
        /// status ping (server-list) and 2 for login.</summary>
        public sealed record Handshake(int ProtocolVersion, string ServerAddress, ushort ServerPort, int NextState);

        /// <summary>A VarInt's decoded value together with the exact bytes it occupied on the wire,
        /// so the caller can replay them verbatim to the backend.</summary>
        public sealed record VarIntResult(int Value, byte[] Raw);

        /// <summary>Reads a Minecraft VarInt (little-endian, 7 bits/byte, MSB = continuation) from a
        /// stream. Returns null on EOF before any byte is read. Throws on a malformed (&gt;5 byte) VarInt.</summary>
        public static async Task<VarIntResult?> ReadVarIntAsync(Stream stream, CancellationToken cancellationToken)
        {
            var raw = new byte[5];
            var one = new byte[1];
            int value = 0, size = 0;
            while (true)
            {
                var read = await stream.ReadAsync(one.AsMemory(0, 1), cancellationToken);
                if (read == 0) return size == 0 ? null : throw new InvalidDataException("Stream ended mid-VarInt.");

                var b = one[0];
                raw[size] = b;
                value |= (b & 0x7F) << (size * 7);
                size++;

                if ((b & 0x80) == 0) return new VarIntResult(value, raw[..size]);
                if (size >= 5) throw new InvalidDataException("VarInt is too big (>5 bytes).");
            }
        }

        /// <summary>Reads a VarInt out of an in-memory buffer, advancing <paramref name="pos"/>.
        /// Returns false if the buffer runs out or the VarInt is malformed.</summary>
        public static bool TryReadVarInt(ReadOnlySpan<byte> buffer, ref int pos, out int value)
        {
            value = 0;
            var size = 0;
            while (true)
            {
                if (pos >= buffer.Length) return false;
                var b = buffer[pos++];
                value |= (b & 0x7F) << (size * 7);
                size++;
                if ((b & 0x80) == 0) return true;
                if (size >= 5) return false;
            }
        }

        /// <summary>Parses a handshake packet body (everything after the packet-length prefix).
        /// Returns null if the bytes aren't a well-formed handshake (id 0x00 with all fields present).</summary>
        public static Handshake? ParseHandshake(ReadOnlySpan<byte> body)
        {
            var pos = 0;
            if (!TryReadVarInt(body, ref pos, out var packetId) || packetId != 0x00) return null;
            if (!TryReadVarInt(body, ref pos, out var protocol)) return null;
            if (!TryReadVarInt(body, ref pos, out var addrLen) || addrLen < 0 || pos + addrLen > body.Length) return null;

            var address = Encoding.UTF8.GetString(body.Slice(pos, addrLen));
            pos += addrLen;

            if (pos + 2 > body.Length) return null;
            var port = (ushort)((body[pos] << 8) | body[pos + 1]);
            pos += 2;

            if (!TryReadVarInt(body, ref pos, out var nextState)) return null;
            return new Handshake(protocol, address, port, nextState);
        }

        /// <summary>Normalises the address from the handshake into a routing key: strips the extra
        /// data Forge/BungeeCord append after a NUL, drops a trailing dot (FQDN form), and lowercases.</summary>
        public static string SanitizeAddress(string rawAddress)
        {
            var s = rawAddress;
            var nul = s.IndexOf('\0');
            if (nul >= 0) s = s[..nul];
            return s.Trim().TrimEnd('.').ToLowerInvariant();
        }
    }
}

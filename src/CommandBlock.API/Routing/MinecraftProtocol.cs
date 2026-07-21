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

        // --- Encoding (for synthetic status / disconnect responses when a server is asleep) ---

        public static byte[] EncodeVarInt(int value)
        {
            var bytes = new List<byte>(5);
            var v = (uint)value;
            while (true)
            {
                if ((v & ~0x7Fu) == 0) { bytes.Add((byte)v); break; }
                bytes.Add((byte)((v & 0x7F) | 0x80));
                v >>= 7;
            }
            return bytes.ToArray();
        }

        private static byte[] EncodeString(string s)
        {
            var utf8 = Encoding.UTF8.GetBytes(s);
            return [.. EncodeVarInt(utf8.Length), .. utf8];
        }

        /// <summary>Frames a packet as [VarInt length][VarInt id][payload].</summary>
        private static byte[] Packet(int packetId, byte[] payload)
        {
            var body = new List<byte>();
            body.AddRange(EncodeVarInt(packetId));
            body.AddRange(payload);
            return [.. EncodeVarInt(body.Count), .. body];
        }

        /// <summary>A Status Response packet (id 0x00) carrying the given server-list JSON.</summary>
        public static byte[] StatusResponsePacket(string statusJson) => Packet(0x00, EncodeString(statusJson));

        /// <summary>A login Disconnect packet (id 0x00) carrying a chat-component JSON reason.</summary>
        public static byte[] LoginDisconnectPacket(string reason)
        {
            var chatJson = "{\"text\":" + JsonString(reason) + "}";
            return Packet(0x00, EncodeString(chatJson));
        }

        /// <summary>A login Plugin Request (id 0x04): message id + channel, no payload. Clients always answer one
        /// (understood or not), and their login read-timeout is on inactivity rather than total duration - so
        /// sending these every few seconds holds a joining player in the login phase indefinitely. Measured against
        /// a real 26.2 client: still answering after 137s, where a silent hold dies at ~30s.</summary>
        public static byte[] LoginPluginRequestPacket(int messageId, string channel) =>
            Packet(0x04, [.. EncodeVarInt(messageId), .. EncodeString(channel)]);

        /// <summary>Builds a Handshake packet (id 0x00) with the given next-state. Used to rewrite a Transfer
        /// reconnect (intent 3) into a normal login (intent 2) before piping to a backend that has no
        /// accepts-transfers flag.</summary>
        public static byte[] HandshakePacket(int protocolVersion, string serverAddress, ushort serverPort, int nextState)
        {
            byte[] body = [.. EncodeVarInt(protocolVersion), .. EncodeString(serverAddress), (byte)(serverPort >> 8), (byte)(serverPort & 0xff), .. EncodeVarInt(nextState)];
            return Packet(0x00, body);
        }

        /// <summary>Builds the server-list status JSON with a custom MOTD (used while a server wakes).</summary>
        public static string StatusJson(string motd, int protocolVersion) =>
            "{\"version\":{\"name\":\"CommandBlock\",\"protocol\":" + protocolVersion + "}," +
            "\"players\":{\"max\":0,\"online\":0}," +
            "\"description\":{\"text\":" + JsonString(motd) + "}}";

        private static string JsonString(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}

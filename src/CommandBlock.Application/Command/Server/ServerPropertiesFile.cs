using System.Globalization;
using System.Text;
using CommandBlock.Application.Dtos.Server;

namespace CommandBlock.Application.Command.Server
{
    /// <summary>Reads and writes the curated subset of server.properties while preserving every other
    /// line, comment and property in the file. Java .properties is line-oriented "key=value".</summary>
    public static class ServerPropertiesFile
    {
        // Curated field -> property key.
        private const string Motd = "motd";
        private const string MaxPlayers = "max-players";
        private const string Difficulty = "difficulty";
        private const string Gamemode = "gamemode";
        private const string Pvp = "pvp";
        private const string OnlineMode = "online-mode";
        private const string Whitelist = "white-list";
        private const string Hardcore = "hardcore";
        private const string AllowFlight = "allow-flight";
        private const string EnableCommandBlock = "enable-command-block";
        private const string ViewDistance = "view-distance";
        private const string SpawnProtection = "spawn-protection";

        public static ServerPropertiesDto ToDto(string rawText)
        {
            var p = Parse(rawText);
            return new ServerPropertiesDto
            {
                Available = true,
                Motd = Get(p, Motd, ""),
                MaxPlayers = GetInt(p, MaxPlayers, 20),
                Difficulty = Get(p, Difficulty, "easy"),
                Gamemode = Get(p, Gamemode, "survival"),
                Pvp = GetBool(p, Pvp, true),
                OnlineMode = GetBool(p, OnlineMode, true),
                Whitelist = GetBool(p, Whitelist, false),
                Hardcore = GetBool(p, Hardcore, false),
                AllowFlight = GetBool(p, AllowFlight, false),
                EnableCommandBlock = GetBool(p, EnableCommandBlock, false),
                ViewDistance = GetInt(p, ViewDistance, 10),
                SpawnProtection = GetInt(p, SpawnProtection, 16),
            };
        }

        public static string ApplyUpdate(string rawText, UpdateServerPropertiesDto u)
        {
            var updates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [Motd] = (u.Motd ?? "").Replace("\n", " ").Replace("\r", ""),
                [MaxPlayers] = u.MaxPlayers.ToString(CultureInfo.InvariantCulture),
                [Difficulty] = u.Difficulty,
                [Gamemode] = u.Gamemode,
                [Pvp] = Bool(u.Pvp),
                [OnlineMode] = Bool(u.OnlineMode),
                [Whitelist] = Bool(u.Whitelist),
                [Hardcore] = Bool(u.Hardcore),
                [AllowFlight] = Bool(u.AllowFlight),
                [EnableCommandBlock] = Bool(u.EnableCommandBlock),
                [ViewDistance] = u.ViewDistance.ToString(CultureInfo.InvariantCulture),
                [SpawnProtection] = u.SpawnProtection.ToString(CultureInfo.InvariantCulture),
            };
            return Merge(rawText, updates);
        }

        private static Dictionary<string, string> Parse(string text)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in (text ?? "").Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                var trimmed = line.TrimStart();
                if (trimmed.Length == 0 || trimmed[0] == '#' || trimmed[0] == '!') continue;
                var eq = line.IndexOf('=');
                if (eq < 0) continue;
                d[line[..eq].Trim()] = line[(eq + 1)..];
            }
            return d;
        }

        /// <summary>Replaces the value of each updated key in place; unknown keys, comments and blank
        /// lines are kept verbatim; keys not already present are appended.</summary>
        private static string Merge(string text, IReadOnlyDictionary<string, string> updates)
        {
            var remaining = new Dictionary<string, string>(updates, StringComparer.OrdinalIgnoreCase);
            var output = new List<string>();
            foreach (var line in (text ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                var trimmed = line.TrimStart();
                var eq = line.IndexOf('=');
                if (trimmed.Length > 0 && trimmed[0] != '#' && trimmed[0] != '!' && eq >= 0)
                {
                    var key = line[..eq].Trim();
                    if (remaining.TryGetValue(key, out var nv))
                    {
                        output.Add($"{key}={nv}");
                        remaining.Remove(key);
                        continue;
                    }
                }
                output.Add(line);
            }
            while (output.Count > 0 && output[^1].Trim().Length == 0) output.RemoveAt(output.Count - 1);
            foreach (var kv in remaining) output.Add($"{kv.Key}={kv.Value}");
            return string.Join('\n', output) + "\n";
        }

        private static string Get(IReadOnlyDictionary<string, string> p, string key, string fallback)
            => p.TryGetValue(key, out var v) ? v.Trim() : fallback;

        private static int GetInt(IReadOnlyDictionary<string, string> p, string key, int fallback)
            => p.TryGetValue(key, out var v) && int.TryParse(v.Trim(), out var n) ? n : fallback;

        private static bool GetBool(IReadOnlyDictionary<string, string> p, string key, bool fallback)
            => p.TryGetValue(key, out var v) ? v.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) : fallback;

        private static string Bool(bool b) => b ? "true" : "false";
    }
}

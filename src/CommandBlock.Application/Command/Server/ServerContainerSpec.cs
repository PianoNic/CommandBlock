using Docker.DotNet.Models;
using CommandBlock.Application.Containers;
using CommandBlock.Domain;

namespace CommandBlock.Application.Command.Server
{
    /// <summary>Builds the Docker create-spec for a Minecraft server container from a
    /// <see cref="ServerInstance"/>. Shared by create and recreate so both stay in lock-step: the
    /// itzg image is configured entirely by its tag (java runtime) and environment (everything else).</summary>
    public static class ServerContainerSpec
    {
        public const string Image = "itzg/minecraft-server";
        public const int McPort = 25565;

        /// <summary>The itzg image tag = the Java runtime. An explicit <see cref="ServerInstance.JavaVersion"/>
        /// wins; otherwise it's derived from the Minecraft version so beginners never think about Java.</summary>
        public static string ImageTag(ServerInstance s)
        {
            var v = new string((s.JavaVersion ?? "").Where(char.IsDigit).ToArray());
            if (v.Length == 0) v = AutoJavaForMinecraft(s.Version);
            return ImageTagForJava(v);
        }

        /// <summary>Maps a Java major version to its itzg image tag. Split out of <see cref="ImageTag"/> so
        /// callers without a <see cref="ServerInstance"/> (e.g. a throwaway capture server) share the same map.</summary>
        public static string ImageTagForJava(string javaVersion) => javaVersion switch
        {
            "8" => "java8",
            "11" => "java11",
            "16" => "java16",
            "17" => "java17",
            "21" => "java21",
            "23" => "java23",
            "24" => "java24",
            "25" => "java25",
            _ => "latest",
        };

        /// <summary>itzg's recommended Java per Minecraft version: 1.21.5+/newest → 25, 1.20.5-1.21.4 → 21,
        /// 1.17-1.20.4 → 17, ≤1.16 → 8. Unknown/LATEST/modpack-derived versions default to the newest
        /// runtime (25) - it runs the latest Minecraft and is backward-compatible with older builds.</summary>
        public static string AutoJavaForMinecraft(string? mcVersion)
        {
            if (string.IsNullOrWhiteSpace(mcVersion)) return "25";
            var parts = mcVersion.Trim().Split('.', '-');
            if (parts.Length < 2 || !int.TryParse(parts[1], out var minor)) return "25";
            var patch = parts.Length >= 3 && int.TryParse(parts[2], out var p) ? p : 0;

            if (minor >= 22) return "25";
            if (minor == 21) return patch >= 5 ? "25" : "21"; // 1.21.5+ moved to Java 25
            if (minor == 20) return patch >= 5 ? "21" : "17";
            if (minor >= 17) return "17";
            return "8";
        }

        /// <summary>Builds the itzg environment for a server: base (EULA/TYPE/MEMORY), the loader or
        /// modpack ref, optional Aikar flags and JVM args, then the user's extra env last so it can
        /// override anything above.</summary>
        public static List<string> BuildEnv(ServerInstance s)
        {
            if (string.IsNullOrWhiteSpace(s.Memory))
                throw new ArgumentException("Memory is required (e.g. \"4G\").");

            var env = new List<string>
            {
                "EULA=TRUE",
                $"TYPE={s.ServerType}",
                $"MEMORY={s.Memory}",
            };

            switch (s.ServerType)
            {
                case "MODRINTH":
                    RequireModpack(s);
                    env.Add($"MODRINTH_MODPACK={s.ModpackRef}");
                    break;
                case "CURSEFORGE":
                case "AUTO_CURSEFORGE":
                    RequireModpack(s);
                    env.Add($"CF_SLUG={s.ModpackRef}");
                    break;
                case "FTBA":
                    RequireModpack(s);
                    env.Add($"FTB_MODPACK_ID={s.ModpackRef}");
                    break;
                default:
                    if (!string.IsNullOrWhiteSpace(s.Version))
                        env.Add($"VERSION={s.Version}");
                    break;
            }

            if (s.UseAikarFlags)
                env.Add("USE_AIKAR_FLAGS=true");
            if (!string.IsNullOrWhiteSpace(s.JvmArgs))
                env.Add($"JVM_OPTS={s.JvmArgs.Trim()}");

            // Extra env last (wins over the derived values). One KEY=VALUE per line; blanks and
            // '#' comments ignored; lines without '=' skipped rather than passed as a bare flag.
            if (!string.IsNullOrWhiteSpace(s.ExtraEnv))
            {
                foreach (var raw in s.ExtraEnv.Split('\n'))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith('#') || !line.Contains('=')) continue;
                    env.Add(line);
                }
            }

            return env;
        }

        /// <summary>The full Docker create parameters for a server container. No host port is
        /// published - the server sits on CommandBlock's network and is reached through the router.</summary>
        public static CreateContainerParameters BuildCreateParams(ServerInstance s, string containerName, string bindSpec)
        {
            return new CreateContainerParameters
            {
                Image = $"{Image}:{ImageTag(s)}",
                Name = containerName,
                Env = BuildEnv(s),
                ExposedPorts = new Dictionary<string, EmptyStruct> { [$"{McPort}/tcp"] = default },
                HostConfig = new HostConfig
                {
                    Binds = new List<string> { bindSpec },
                    RestartPolicy = new RestartPolicy { Name = RestartPolicyKind.UnlessStopped },
                },
                Labels = CommandBlockContainerLabels.ForServer(s.ServerType, s.Id, s.Hostname, s.DisplayName),
            };
        }

        private static void RequireModpack(ServerInstance s)
        {
            if (string.IsNullOrWhiteSpace(s.ModpackRef))
                throw new ArgumentException($"ServerType '{s.ServerType}' requires a modpack reference (ModpackRef).");
        }
    }
}

using System.Text.RegularExpressions;

namespace CommandBlock.Application.Containers
{
    /// <summary>
    /// The labels stamped on every CommandBlock-provisioned server container. Besides the internal
    /// <c>commandblock.*</c> markers (used to recognise our own containers), we set Docker Compose
    /// project labels so Docker Desktop groups all servers into one cluster.
    /// </summary>
    public static partial class CommandBlockContainerLabels
    {
        /// <summary>The Compose project provisioned Minecraft servers are grouped under, kept
        /// separate from CommandBlock's own stack.</summary>
        public const string ServersComposeProject = "commandblock-servers";

        /// <summary>The label the router keys on: its value is the hostname players connect with.
        /// Discovery maps <c>mc.host</c> -&gt; this container on its Minecraft port.</summary>
        public const string McHostLabel = "mc.host";

        /// <summary>Labels for a provisioned Minecraft server container. Besides the internal
        /// <c>commandblock.*</c> markers and the Compose grouping, it stamps <see cref="McHostLabel"/>
        /// so the router can discover the backend and route <paramref name="hostname"/> to it.</summary>
        public static Dictionary<string, string> ForServer(string serverType, Guid instanceId, string? hostname, string? displayName = null)
        {
            return new Dictionary<string, string>
            {
                ["commandblock.managed"] = "true",
                ["commandblock.server-type"] = serverType,
                ["commandblock.instance-id"] = instanceId.ToString(),
                [McHostLabel] = hostname,
                ["com.docker.compose.project"] = ServersComposeProject,
                ["com.docker.compose.service"] = ServiceName(serverType, displayName),
                ["com.docker.compose.oneoff"] = "False",
            };
        }

        // Compose service names allow [a-zA-Z0-9._-]; slugify the display name and fall back to the
        // server type so each server shows up under a readable service row in the project.
        private static string ServiceName(string serverType, string? displayName)
        {
            var basis = string.IsNullOrWhiteSpace(displayName) ? serverType : displayName!;
            var slug = SlugRegex().Replace(basis.Trim().ToLowerInvariant(), "-").Trim('-');
            return string.IsNullOrEmpty(slug) ? serverType.ToLowerInvariant() : slug;
        }

        [GeneratedRegex("[^a-z0-9._-]+")]
        private static partial Regex SlugRegex();
    }
}

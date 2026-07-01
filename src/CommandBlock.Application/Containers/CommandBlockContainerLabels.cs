using System.Text.RegularExpressions;

namespace CommandBlock.Application.Containers
{
    /// <summary>
    /// The labels stamped on every CommandBlock-provisioned container.
    ///
    /// Besides the internal <c>commandblock.*</c> labels (used by discovery to recognise our own
    /// containers), we set Docker Compose project labels so Docker Desktop - and any tool that
    /// reads them - groups all provisioned databases into one cluster instead of listing them as
    /// loose, unrelated containers.
    /// </summary>
    public static partial class CommandBlockContainerLabels
    {
        /// <summary>
        /// The Compose project all provisioned databases are grouped under. Deliberately distinct
        /// from the app's own stack (backend + db + keycloak, whose project is named after the repo
        /// directory, usually "commandblock") so the provisioned databases form their own cluster in
        /// Docker Desktop rather than mixing in with CommandBlock's own services.
        /// </summary>
        public const string ComposeProject = "commandblock-databases";

        public static Dictionary<string, string> For(string engine, Guid instanceId, string? displayName = null)
        {
            return new Dictionary<string, string>
            {
                ["commandblock.managed"] = "true",
                ["commandblock.engine"] = engine,
                ["commandblock.instance-id"] = instanceId.ToString(),
                // Compose project labels: make Docker Desktop cluster these under their own
                // "commandblock-databases" project. We intentionally omit config_files/working_dir so the
                // `docker compose` CLI won't try to manage containers with no compose file behind them.
                ["com.docker.compose.project"] = ComposeProject,
                ["com.docker.compose.service"] = ServiceName(engine, displayName),
                ["com.docker.compose.oneoff"] = "False",
            };
        }

        // Compose service names allow [a-zA-Z0-9._-]; slugify the display name and fall back to the
        // engine so each instance shows up under a readable service row in the project.
        private static string ServiceName(string engine, string? displayName)
        {
            var basis = string.IsNullOrWhiteSpace(displayName) ? engine : displayName!;
            var slug = SlugRegex().Replace(basis.Trim().ToLowerInvariant(), "-").Trim('-');
            return string.IsNullOrEmpty(slug) ? engine.ToLowerInvariant() : slug;
        }

        [GeneratedRegex("[^a-z0-9._-]+")]
        private static partial Regex SlugRegex();
    }
}

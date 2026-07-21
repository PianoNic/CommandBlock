using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using CommandBlock.Infrastructure.Interfaces;

namespace CommandBlock.Infrastructure.Services
{
    /// <summary>Fetches Mojang's official version manifest and returns the release versions. The
    /// list changes only when Mojang ships a version, so it's cached for a few hours to avoid
    /// pulling the ~1&#160;MB manifest on every create-dialog open. Registered as a typed HttpClient.</summary>
    public class MinecraftVersionClient(HttpClient http, IMemoryCache cache) : IMinecraftVersionClient
    {
        private const string CacheKey = "mc-release-versions";
        private static readonly TimeSpan CacheFor = TimeSpan.FromHours(3);

        // v2 manifest lists every version newest-first with a "type" of release/snapshot/old_beta/old_alpha.
        private const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

        public async Task<IReadOnlyList<string>> GetReleaseVersionsAsync(CancellationToken cancellationToken = default)
        {
            if (cache.TryGetValue(CacheKey, out IReadOnlyList<string>? cached) && cached is not null)
                return cached;

            using var doc = await http.GetFromJsonSafeAsync(ManifestUrl, cancellationToken);
            var versions = new List<string>();
            if (doc is not null && doc.RootElement.TryGetProperty("versions", out var arr))
            {
                foreach (var v in arr.EnumerateArray())
                {
                    if (v.TryGetProperty("type", out var type) && type.GetString() == "release"
                        && v.TryGetProperty("id", out var id) && id.GetString() is { Length: > 0 } vid
                        && HasServerJar(v))
                    {
                        versions.Add(vid);
                    }
                }
            }

            cache.Set(CacheKey, (IReadOnlyList<string>)versions, CacheFor);
            return versions;
        }

        /// <summary>Mojang only began publishing a downloadable *server* jar with 1.2.5 - earlier releases
        /// (1.0, 1.1 and the beta/alpha line) are listed in the manifest but are client-only. Offering one
        /// leaves the container crash-looping on "No server jar download available", so they're filtered out
        /// here rather than failing at boot. Keyed on the manifest's own release date so no version list has
        /// to be maintained by hand.</summary>
        private static readonly DateTimeOffset FirstServerJarRelease = new(2012, 3, 29, 0, 0, 0, TimeSpan.Zero);

        private static bool HasServerJar(JsonElement version) =>
            version.TryGetProperty("releaseTime", out var released)
            && released.TryGetDateTimeOffset(out var when)
            && when >= FirstServerJarRelease;
    }

    internal static class HttpJsonExtensions
    {
        public static async Task<JsonDocument?> GetFromJsonSafeAsync(this HttpClient http, string url, CancellationToken cancellationToken)
        {
            await using var stream = await http.GetStreamAsync(url, cancellationToken);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
    }
}

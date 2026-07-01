using System.Net.Http.Json;
using System.Text.Json;
using CommandBlock.Infrastructure.Interfaces;

namespace CommandBlock.Infrastructure.Services
{
    /// <summary>Calls Modrinth's public search API (no key needed) and maps hits to the trimmed
    /// result the create UI shows. Registered as a typed <see cref="HttpClient"/>.</summary>
    public class ModrinthClient(HttpClient http) : IModrinthClient
    {
        public async Task<IReadOnlyList<ModpackSearchResult>> SearchModpacksAsync(string query, CancellationToken cancellationToken = default)
        {
            // facets restricts results to modpack projects. limit keeps the payload small for the picker.
            var url = "v2/search?limit=20&index=relevance"
                + "&query=" + Uri.EscapeDataString(query ?? string.Empty)
                + "&facets=" + Uri.EscapeDataString("[[\"project_type:modpack\"]]");

            using var doc = await http.GetFromJsonAsync<JsonDocument>(url, cancellationToken);
            if (doc is null || !doc.RootElement.TryGetProperty("hits", out var hits)) return [];

            var results = new List<ModpackSearchResult>();
            foreach (var hit in hits.EnumerateArray())
            {
                var slug = hit.TryGetProperty("slug", out var s) ? s.GetString() : null;
                if (string.IsNullOrWhiteSpace(slug)) continue;

                results.Add(new ModpackSearchResult(
                    Slug: slug!,
                    Title: hit.TryGetProperty("title", out var t) ? t.GetString() ?? slug! : slug!,
                    Description: hit.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "",
                    IconUrl: hit.TryGetProperty("icon_url", out var i) ? i.GetString() : null,
                    Downloads: hit.TryGetProperty("downloads", out var dl) && dl.TryGetInt64(out var n) ? n : 0));
            }
            return results;
        }
    }
}

namespace CommandBlock.Infrastructure.Interfaces
{
    /// <summary>A modpack search hit from Modrinth, trimmed to what the create UI needs. The
    /// <see cref="Slug"/> is what goes into a server's ModpackRef for a MODRINTH server.</summary>
    public sealed record ModpackSearchResult(
        string Slug,
        string Title,
        string Description,
        string? IconUrl,
        long Downloads);

    public interface IModrinthClient
    {
        /// <summary>Searches Modrinth for modpacks matching <paramref name="query"/>.</summary>
        Task<IReadOnlyList<ModpackSearchResult>> SearchModpacksAsync(string query, CancellationToken cancellationToken = default);
    }
}

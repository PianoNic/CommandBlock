namespace CommandBlock.Infrastructure.Interfaces
{
    public interface IMinecraftVersionClient
    {
        /// <summary>Returns the released Minecraft (Java) versions, newest first, from Mojang's
        /// official version manifest. Snapshots and pre-releases are excluded.</summary>
        Task<IReadOnlyList<string>> GetReleaseVersionsAsync(CancellationToken cancellationToken = default);
    }
}

namespace CommandBlock.Application.Options;

public sealed class CommandBlockOptions
{
    /// <summary>
    /// Controls how provisioned servers persist world data. Defaults to per-server named Docker
    /// volumes (commandblock-mc-{id}-data). Set to HostFolder + provide a HostPath to instead
    /// bind-mount each server's data directory under {HostPath}/{containerName} on the host.
    /// </summary>
    public StorageOptions Storage { get; set; } = new();

    /// <summary>
    /// Public URL this control plane is served on (e.g. https://commandblock.example.com). Used to
    /// derive the OIDC redirect when Oidc:RedirectUri isn't set. Usually set via the env file
    /// (CommandBlock__PublicUrl); this is the commandblock.yaml fallback.
    /// </summary>
    public string? PublicUrl { get; set; }
}

public enum StorageMode
{
    Volume,
    HostFolder,
}

public sealed class StorageOptions
{
    public StorageMode Mode { get; set; } = StorageMode.Volume;

    /// <summary>
    /// Required when Mode = HostFolder. Absolute path on the Docker host (not inside the
    /// CommandBlock container) under which a subdirectory is created per provisioned server.
    /// Forward slashes work on Windows hosts too because dockerd normalises them.
    /// </summary>
    public string? HostPath { get; set; }

    public string ResolveBindForContainer(string containerName, string dataPath)
    {
        return Mode switch
        {
            StorageMode.HostFolder => $"{ResolveHostPath()}/{containerName}:{dataPath}",
            _ => $"{containerName}-data:{dataPath}",
        };
    }

    public string? ResolveHostFolderForContainer(string containerName)
    {
        return Mode == StorageMode.HostFolder ? $"{ResolveHostPath()}/{containerName}" : null;
    }

    /// <summary>
    /// Non-throwing variant used by cleanup paths (delete) which run even when the server was
    /// originally provisioned under a different storage mode. Returns null both when the current
    /// mode is Volume and when HostFolder is misconfigured.
    /// </summary>
    public string? TryResolveHostFolderForContainer(string containerName)
    {
        if (Mode != StorageMode.HostFolder || string.IsNullOrWhiteSpace(HostPath)) return null;
        return $"{HostPath.TrimEnd('/', '\\')}/{containerName}";
    }

    private string ResolveHostPath()
    {
        if (string.IsNullOrWhiteSpace(HostPath))
            throw new InvalidOperationException("storage.mode is HostFolder but storage.host_path is not set in commandblock.yaml.");
        return HostPath.TrimEnd('/', '\\');
    }
}

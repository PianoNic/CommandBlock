namespace CommandBlock.Application.Options;

public sealed class CommandBlockOptions
{
    /// <summary>
    /// Controls how provisioned servers persist world data. Defaults to <see cref="StorageMode.HostFolder"/>:
    /// each server's /data is bind-mounted to {HostPath}/{containerName} on the Docker host, so the
    /// world files are directly inspectable and backup-able. Switch to <see cref="StorageMode.Volume"/>
    /// to use a per-server Docker named volume instead.
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
    /// <summary>Per-server host directory bind-mount (the default).</summary>
    HostFolder,

    /// <summary>Per-server Docker named volume.</summary>
    Volume,
}

public sealed class StorageOptions
{
    /// <summary>Defaults to a host folder so worlds live on disk where you can see and back them up.</summary>
    public StorageMode Mode { get; set; } = StorageMode.HostFolder;

    /// <summary>
    /// Base directory on the Docker host under which each server gets a data subdirectory (used when
    /// Mode = HostFolder). For the containerised deployment this same path is bind-mounted into the
    /// CommandBlock container at the identical path, so the control plane and the daemon agree on it.
    /// Forward slashes work on Windows hosts too - dockerd normalises them.
    /// </summary>
    public string HostPath { get; set; } = "/data/servers";

    public string ResolveBindForContainer(string containerName, string dataPath)
    {
        return Mode switch
        {
            StorageMode.Volume => $"{containerName}-data:{dataPath}",
            _ => $"{ResolveHostPath()}/{containerName}:{dataPath}",
        };
    }

    public string? ResolveHostFolderForContainer(string containerName)
    {
        return Mode == StorageMode.HostFolder ? $"{ResolveHostPath()}/{containerName}" : null;
    }

    /// <summary>
    /// Non-throwing variant used by cleanup paths (delete) which run even when the server was
    /// originally provisioned under a different storage mode. Returns null when the mode is Volume
    /// or HostPath is missing.
    /// </summary>
    public string? TryResolveHostFolderForContainer(string containerName)
    {
        if (Mode != StorageMode.HostFolder || string.IsNullOrWhiteSpace(HostPath)) return null;
        return $"{HostPath.TrimEnd('/', '\\')}/{containerName}";
    }

    private string ResolveHostPath()
    {
        if (string.IsNullOrWhiteSpace(HostPath))
            throw new InvalidOperationException("storage.host_path is not set in commandblock.yaml.");
        return HostPath.TrimEnd('/', '\\');
    }
}

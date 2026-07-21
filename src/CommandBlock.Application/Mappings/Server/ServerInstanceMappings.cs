using CommandBlock.Application.Dtos.Server;

namespace CommandBlock.Application.Mappings.Server
{
    public static class ServerInstanceMappings
    {
        public static ServerInstanceDto ToDto(this CommandBlock.Domain.ServerInstance s, string? state = null, int? playersOnline = null, int? playersMax = null, long? memoryBytes = null) => new()
        {
            Id = s.Id,
            ServerType = s.ServerType,
            Version = s.Version,
            ModpackRef = s.ModpackRef,
            Memory = s.Memory,
            JavaVersion = s.JavaVersion,
            UseAikarFlags = s.UseAikarFlags,
            AllowAnyClientVersion = s.AllowAnyClientVersion,
            JvmArgs = s.JvmArgs,
            ExtraEnv = s.ExtraEnv,
            DisplayName = s.DisplayName,
            Hostname = s.Hostname,
            Port = s.Port,
            ContainerName = s.ContainerName,
            IsManaged = s.IsManaged,
            IsPublic = s.IsPublic,
            LanPort = s.LanPort,
            LanBindAddress = s.LanBindAddress,
            RoutedThroughProxy = s.RoutedThroughProxy,
            State = state,
            IsConfigManaged = s.IsConfigManaged,
            PlayersOnline = playersOnline,
            PlayersMax = playersMax,
            MemoryBytes = memoryBytes,
            HasIcon = s.IconPng != null && s.IconPng.Length > 0,
            WakeOnConnect = s.WakeOnConnect,
            WakeQueueSeconds = s.WakeQueueSeconds,
            AutoSleepEnabled = s.AutoSleepEnabled,
            AutoSleepIdleMinutes = s.AutoSleepIdleMinutes,
            CreatedAt = s.CreatedAt,
        };
    }
}

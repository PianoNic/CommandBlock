using CommandBlock.Application.Dtos.Server;

namespace CommandBlock.Application.Mappings.Server
{
    public static class ServerInstanceMappings
    {
        public static ServerInstanceDto ToDto(this CommandBlock.Domain.ServerInstance s, string? state = null) => new()
        {
            Id = s.Id,
            ServerType = s.ServerType,
            Version = s.Version,
            PreviousVersion = s.PreviousVersion,
            ModpackRef = s.ModpackRef,
            Memory = s.Memory,
            DisplayName = s.DisplayName,
            Hostname = s.Hostname,
            Port = s.Port,
            ContainerName = s.ContainerName,
            IsManaged = s.IsManaged,
            IsPublic = s.IsPublic,
            State = state,
            IsConfigManaged = s.IsConfigManaged,
            NodeId = s.NodeId,
            CreatedAt = s.CreatedAt,
        };
    }
}

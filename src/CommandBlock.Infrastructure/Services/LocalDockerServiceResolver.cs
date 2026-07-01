using CommandBlock.Infrastructure.Interfaces;

namespace CommandBlock.Infrastructure.Services
{
    /// <summary>Single-host resolver: every request runs against the control plane's local Docker
    /// daemon. (Multi-host node routing was removed; <paramref name="nodeId"/> is ignored.)</summary>
    public class LocalDockerServiceResolver(IDockerService docker) : IDockerServiceResolver
    {
        public IDockerService Resolve(Guid? nodeId) => docker;
    }
}

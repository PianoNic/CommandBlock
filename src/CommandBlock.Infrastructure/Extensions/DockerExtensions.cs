using Docker.DotNet;
using CommandBlock.Infrastructure.Interfaces;
using CommandBlock.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CommandBlock.Infrastructure.Extensions
{
    public static class DockerExtensions
    {
        public static IServiceCollection AddDocker(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddSingleton<IDockerClient>(_ =>
            {
                var endpoint = configuration["Docker:Endpoint"];
                var config = string.IsNullOrWhiteSpace(endpoint)
                    ? new DockerClientConfiguration()
                    : new DockerClientConfiguration(new Uri(endpoint));
                return config.CreateClient();
            });

            services.AddScoped<IDockerService, DockerService>();

            return services;
        }
    }
}

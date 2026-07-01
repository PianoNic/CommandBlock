using CommandBlock.Infrastructure.Interfaces;
using CommandBlock.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CommandBlock.Infrastructure.Extensions
{
    public static class CatalogExtensions
    {
        public static IServiceCollection AddCatalog(this IServiceCollection services)
        {
            services.AddMemoryCache();
            services.AddHttpClient<IDatabaseVersionService, DatabaseVersionService>(http =>
            {
                // Docker Hub / MCR can rate-limit or reject requests without a UA; set one explicitly.
                http.DefaultRequestHeaders.UserAgent.ParseAdd("commandblock-version-check");
                http.Timeout = TimeSpan.FromSeconds(15);
            });
            return services;
        }
    }
}

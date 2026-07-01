using CommandBlock.Infrastructure.Interfaces;
using CommandBlock.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CommandBlock.Infrastructure.Extensions
{
    public static class ActivityExtensions
    {
        /// <summary>Registers the activity-log writer used to record server actions.</summary>
        public static IServiceCollection AddActivityLog(this IServiceCollection services)
        {
            services.AddScoped<IActivityLogger, ActivityLogger>();
            return services;
        }
    }
}

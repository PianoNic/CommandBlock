using CommandBlock.Application.Options;
using Microsoft.Extensions.Options;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace CommandBlock.API.Extensions;

public static class CommandBlockConfigExtensions
{
    private const string FileName = "commandblock.yaml";
    private const string EnvVar = "CommandBlock_CONFIG";

    public static IServiceCollection AddCommandBlockConfig(this IServiceCollection services, IHostEnvironment env)
    {
        var path = Environment.GetEnvironmentVariable(EnvVar);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            path = FindUpwards(env.ContentRootPath);

        if (path is null || !File.Exists(path))
        {
            // No config file present: start with defaults rather than crashing. Provisioning
            // needs port ranges (commandblock.yaml -> commandblock.port_ranges), so those operations surface a
            // clear error until configured, but the app boots - important for the desktop build.
            Console.Error.WriteLine($"[CommandBlock] No {FileName} found (set {EnvVar} or place {FileName} at the content root). Starting with defaults.");
            var defaults = new CommandBlockOptions();
            services.AddSingleton<IOptions<CommandBlockOptions>>(Options.Create(defaults));
            return services;
        }

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        using var reader = File.OpenText(path);
        var root = deserializer.Deserialize<CommandBlockConfigFile>(reader)
            ?? throw new InvalidOperationException($"{FileName} is empty or invalid.");

        var options = root.CommandBlock ?? new CommandBlockOptions();
        services.AddSingleton<IOptions<CommandBlockOptions>>(Options.Create(options));
        return services;
    }

    private static string? FindUpwards(string startDir)
    {
        var dir = new DirectoryInfo(startDir);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, FileName);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    private sealed class CommandBlockConfigFile
    {
        public CommandBlockOptions? CommandBlock { get; set; }
    }
}

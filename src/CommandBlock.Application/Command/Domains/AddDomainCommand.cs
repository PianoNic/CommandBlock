using System.Text.RegularExpressions;
using Mediator;
using Microsoft.EntityFrameworkCore;
using CommandBlock.Application.Dtos.Domains;
using CommandBlock.Infrastructure;
using CommandBlock.Infrastructure.Interfaces;

namespace CommandBlock.Application.Command.Domains
{
    public record AddDomainCommand(string Name) : ICommand<DomainDto>;

    public partial class AddDomainCommandHandler(CommandBlockDbContext db, IActivityLogger activity)
        : ICommandHandler<AddDomainCommand, DomainDto>
    {
        public async ValueTask<DomainDto> Handle(AddDomainCommand command, CancellationToken cancellationToken)
        {
            var name = Normalize(command.Name);
            if (!DomainRegex().IsMatch(name))
                throw new ArgumentException($"'{command.Name}' is not a valid domain (e.g. \"gaggao.com\").");

            if (await db.Domains.AnyAsync(d => d.Name == name, cancellationToken))
                throw new InvalidOperationException($"Domain '{name}' already exists.");

            var entry = new CommandBlock.Domain.DomainEntry { Name = name };
            db.Domains.Add(entry);
            await db.SaveChangesAsync(cancellationToken);

            await activity.LogAsync("domain.add", name, null, null, null, cancellationToken);

            return new DomainDto { Id = entry.Id, Name = entry.Name, CreatedAt = entry.CreatedAt };
        }

        /// <summary>Lower-cases and strips scheme, a leading "*." wildcard, whitespace, and any
        /// trailing dot/slash so "https://*.Gaggao.com/" and "gaggao.com" normalise to the same value.</summary>
        internal static string Normalize(string raw)
        {
            var s = (raw ?? string.Empty).Trim().ToLowerInvariant();
            s = SchemeRegex().Replace(s, "");
            if (s.StartsWith("*.")) s = s[2..];
            s = s.Trim().TrimEnd('.', '/');
            // Drop anything after the first slash (a pasted URL path) and any port.
            var slash = s.IndexOf('/');
            if (slash >= 0) s = s[..slash];
            var colon = s.IndexOf(':');
            if (colon >= 0) s = s[..colon];
            return s;
        }

        [GeneratedRegex(@"^[a-z0-9]([a-z0-9-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)+$")]
        private static partial Regex DomainRegex();

        [GeneratedRegex(@"^[a-z][a-z0-9+.-]*://")]
        private static partial Regex SchemeRegex();
    }
}

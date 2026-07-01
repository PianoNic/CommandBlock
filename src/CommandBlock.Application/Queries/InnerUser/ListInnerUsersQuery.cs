using Mediator;
using CommandBlock.Infrastructure;
using CommandBlock.Infrastructure.Extensions;
using CommandBlock.Infrastructure.Interfaces;

namespace CommandBlock.Application.Queries.InnerUser
{
    public record ListInnerUsersQuery(Guid InstanceId) : IQuery<IReadOnlyList<string>>;

    public class ListInnerUsersQueryHandler(CommandBlockDbContext db, ISecretsVaultService vault, IInnerUserServiceResolver resolver) : IQueryHandler<ListInnerUsersQuery, IReadOnlyList<string>>
    {
        public async ValueTask<IReadOnlyList<string>> Handle(ListInnerUsersQuery query, CancellationToken cancellationToken)
        {
            var target = await InnerDatabaseTargetLoader.LoadAsync(db, vault, query.InstanceId, cancellationToken);
            return await resolver.Resolve(target.Engine).ListAsync(target, cancellationToken);
        }
    }
}

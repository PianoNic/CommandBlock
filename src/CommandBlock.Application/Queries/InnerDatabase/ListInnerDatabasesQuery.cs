using Mediator;
using CommandBlock.Infrastructure;
using CommandBlock.Infrastructure.Extensions;
using CommandBlock.Infrastructure.Interfaces;

namespace CommandBlock.Application.Queries.InnerDatabase
{
    public record ListInnerDatabasesQuery(Guid InstanceId) : IQuery<IReadOnlyList<string>>;

    public class ListInnerDatabasesQueryHandler(CommandBlockDbContext db, ISecretsVaultService vault, IInnerDatabaseServiceResolver resolver) : IQueryHandler<ListInnerDatabasesQuery, IReadOnlyList<string>>
    {
        public async ValueTask<IReadOnlyList<string>> Handle(ListInnerDatabasesQuery query, CancellationToken cancellationToken)
        {
            var target = await InnerDatabaseTargetLoader.LoadAsync(db, vault, query.InstanceId, cancellationToken);
            return await resolver.Resolve(target.Engine).ListAsync(target, cancellationToken);
        }
    }
}

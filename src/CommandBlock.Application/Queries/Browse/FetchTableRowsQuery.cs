using Mediator;
using CommandBlock.Application.Dtos.Browse;
using CommandBlock.Application.Mappings.Browse;
using CommandBlock.Infrastructure;
using CommandBlock.Infrastructure.Extensions;
using CommandBlock.Infrastructure.Interfaces;

namespace CommandBlock.Application.Queries.Browse
{
    public record FetchTableRowsQuery(Guid InstanceId, string Database, string Table, int Limit = 50, int Offset = 0)
        : IQuery<TableRowsDto>;

    public class FetchTableRowsQueryHandler(CommandBlockDbContext db, ISecretsVaultService vault, IInnerSchemaServiceResolver resolver) : IQueryHandler<FetchTableRowsQuery, TableRowsDto>
    {
        public async ValueTask<TableRowsDto> Handle(FetchTableRowsQuery query, CancellationToken cancellationToken)
        {
            var target = await InnerDatabaseTargetLoader.LoadAsync(db, vault, query.InstanceId, cancellationToken);
            var rows = await resolver.Resolve(target.Engine).FetchRowsAsync(target, query.Database, query.Table, query.Limit, query.Offset, cancellationToken);
            return rows.ToDto();
        }
    }
}

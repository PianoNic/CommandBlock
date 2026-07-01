using Mediator;
using CommandBlock.Application.Dtos.Query;
using CommandBlock.Application.Mappings.Query;
using CommandBlock.Infrastructure;
using CommandBlock.Infrastructure.Extensions;

namespace CommandBlock.Application.Command.Query
{
    public record RunQueryCommand(Guid InstanceId, string Database, string Sql, int RowLimit) : ICommand<RunQueryResultDto>;

    public class RunQueryCommandHandler(CommandBlockDbContext db, Infrastructure.Interfaces.ISecretsVaultService vault, IInnerQueryServiceResolver resolver) : ICommandHandler<RunQueryCommand, RunQueryResultDto>
    {
        public async ValueTask<RunQueryResultDto> Handle(RunQueryCommand command, CancellationToken cancellationToken)
        {
            var target = await InnerDatabaseTargetLoader.LoadAsync(db, vault, command.InstanceId, cancellationToken);
            var svc = resolver.TryResolve(target.Engine)
                ?? throw new NotSupportedException($"The query console is not available for engine '{target.Engine}' yet. " + "Supported: postgres, timescaledb, pgvector, cockroachdb, mysql, mariadb, mssql, clickhouse.");

            var result = await svc.RunAsync(target, command.Database, command.Sql, command.RowLimit, cancellationToken);

            return result.ToDto();
        }
    }
}

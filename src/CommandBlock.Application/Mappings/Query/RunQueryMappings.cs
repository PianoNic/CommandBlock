using CommandBlock.Application.Dtos.Query;
using CommandBlock.Infrastructure.Interfaces;

namespace CommandBlock.Application.Mappings.Query
{
    public static class RunQueryMappings
    {
        public static RunQueryResultDto ToDto(this QueryResult result) => new()
        {
            Columns = result.Columns.Select(c => new RunQueryColumnDto(c.Name, c.TypeName)).ToList(),
            Rows = result.Rows,
            RowsAffected = result.RowsAffected,
            ElapsedMs = result.ElapsedMs,
            Truncated = result.Truncated,
        };
    }
}

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace CommandBlock.API.OpenApi
{
    /// <summary>Documents numbers as plain numbers.
    ///
    /// ASP.NET's web JSON defaults set <c>JsonNumberHandling.AllowReadingFromString</c>, so a number may
    /// legitimately arrive as <c>42</c> or <c>"42"</c>, and .NET 10 faithfully documents that as
    /// <c>type: ["integer", "string"]</c>. Accurate, but client generators can't express the union: the
    /// TypeScript generator hoists each one into a junk named model (an empty <c>interface XDtoActiveNow {}</c>),
    /// which then can't be used as the number it is. We only ever *emit* numbers as numbers, so the string
    /// half is noise for consumers - it's stripped here rather than by hand-editing the generated client.</summary>
    public sealed class NumericSchemaTransformer : IOpenApiSchemaTransformer
    {
        public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
        {
            var type = schema.Type;
            if (type is null || (type & (JsonSchemaType.Integer | JsonSchemaType.Number)) == 0) return Task.CompletedTask;
            if ((type & JsonSchemaType.String) == 0) return Task.CompletedTask;

            schema.Type = type & ~JsonSchemaType.String;   // keep null, so optional stays optional
            schema.Pattern = null;                        // the pattern only described the string form

            return Task.CompletedTask;
        }
    }
}

using Mediator;
using Microsoft.AspNetCore.Mvc;
using CommandBlock.Application.Command.Domains;
using CommandBlock.Application.Dtos.Domains;
using CommandBlock.Application.Queries.Domains;

namespace CommandBlock.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DomainsController(IMediator mediator) : ControllerBase
    {
        [HttpGet]
        [ProducesResponseType(typeof(IReadOnlyList<DomainDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> List(CancellationToken cancellationToken)
        {
            return Ok(await mediator.Send(new ListDomainsQuery(), cancellationToken));
        }

        [HttpPost]
        [ProducesResponseType(typeof(DomainDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Add([FromBody] AddDomainDto body, CancellationToken cancellationToken)
        {
            try
            {
                var result = await mediator.Send(new AddDomainCommand(body.Name), cancellationToken);
                return CreatedAtAction(nameof(List), new { id = result.Id }, result);
            }
            catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
            catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        }

        [HttpDelete("{id:guid}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
        {
            await mediator.Send(new DeleteDomainCommand(id), cancellationToken);
            return NoContent();
        }
    }
}

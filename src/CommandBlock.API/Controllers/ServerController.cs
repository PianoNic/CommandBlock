using Mediator;
using Microsoft.AspNetCore.Mvc;
using CommandBlock.Application.Command.Server;
using CommandBlock.Application.Dtos.Server;
using CommandBlock.Application.Queries.Server;

namespace CommandBlock.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ServerController(IMediator mediator) : ControllerBase
    {
        [HttpGet]
        [ProducesResponseType(typeof(IReadOnlyList<ServerInstanceDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> List(CancellationToken cancellationToken)
        {
            var result = await mediator.Send(new ListServersQuery(), cancellationToken);
            return Ok(result);
        }

        [HttpPost]
        [ProducesResponseType(typeof(ServerInstanceDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Create([FromBody] CreateServerDto body, CancellationToken cancellationToken)
        {
            try
            {
                var result = await mediator.Send(new CreateServerCommand(
                    body.ServerType, body.DisplayName, body.Hostname, body.Memory,
                    body.Version, body.ModpackRef, body.NodeId), cancellationToken);
                return CreatedAtAction(nameof(Create), new { id = result.Id }, result);
            }
            catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
            catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        }

        [HttpPost("{id:guid}/start")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Start(Guid id, CancellationToken cancellationToken)
        {
            try
            {
                await mediator.Send(new StartServerCommand(id), cancellationToken);
                return NoContent();
            }
            catch (ServerNotFoundException) { return NotFound(); }
            catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        }

        [HttpPost("{id:guid}/stop")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Stop(Guid id, CancellationToken cancellationToken)
        {
            try
            {
                await mediator.Send(new StopServerCommand(id), cancellationToken);
                return NoContent();
            }
            catch (ServerNotFoundException) { return NotFound(); }
            catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        }

        [HttpDelete("{id:guid}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
        {
            try
            {
                await mediator.Send(new DeleteServerCommand(id), cancellationToken);
                return NoContent();
            }
            catch (ServerNotFoundException) { return NotFound(); }
        }
    }
}

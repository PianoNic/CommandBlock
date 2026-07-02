using Mediator;
using Microsoft.AspNetCore.Mvc;
using CommandBlock.Application.Queries.Host;

namespace CommandBlock.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class HostController(IMediator mediator) : ControllerBase
    {
        /// <summary>Host memory: total, already allocated to servers, and available - so the create
        /// dialog can cap the memory slider and avoid overshooting the machine.</summary>
        [HttpGet("resources")]
        [ProducesResponseType(typeof(HostResourcesDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> Resources(CancellationToken cancellationToken)
        {
            return Ok(await mediator.Send(new GetHostResourcesQuery(), cancellationToken));
        }
    }
}

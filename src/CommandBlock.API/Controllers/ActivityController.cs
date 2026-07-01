using Mediator;
using Microsoft.AspNetCore.Mvc;
using CommandBlock.Application.Dtos.Activity;
using CommandBlock.Application.Queries.Activity;

namespace CommandBlock.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ActivityController(IMediator mediator) : ControllerBase
    {
        [HttpGet]
        [ProducesResponseType(typeof(IReadOnlyList<ActivityEntryDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> List([FromQuery] int limit = 200, CancellationToken cancellationToken = default)
        {
            var result = await mediator.Send(new ListActivityQuery(limit), cancellationToken);
            return Ok(result);
        }
    }
}

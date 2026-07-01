using Mediator;
using Microsoft.AspNetCore.Mvc;
using CommandBlock.Application.Dtos.Settings;
using CommandBlock.Application.Queries.Settings;

namespace CommandBlock.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SettingsController(IMediator mediator) : ControllerBase
    {
        [HttpGet]
        [ProducesResponseType(typeof(SettingsDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> Get(CancellationToken cancellationToken)
        {
            var result = await mediator.Send(new GetSettingsQuery(), cancellationToken);
            return Ok(result);
        }
    }
}

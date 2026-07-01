using Microsoft.AspNetCore.Mvc;
using CommandBlock.Infrastructure.Interfaces;

namespace CommandBlock.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MinecraftVersionsController(IMinecraftVersionClient versions) : ControllerBase
    {
        /// <summary>The released Minecraft (Java) versions, newest first, for the create dialog's
        /// version picker. Sourced live from Mojang's version manifest.</summary>
        [HttpGet]
        [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status502BadGateway)]
        public async Task<IActionResult> List(CancellationToken cancellationToken)
        {
            try
            {
                return Ok(await versions.GetReleaseVersionsAsync(cancellationToken));
            }
            catch (HttpRequestException ex)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new { error = "Mojang is unreachable: " + ex.Message });
            }
        }
    }
}

using Microsoft.AspNetCore.Mvc;
using CommandBlock.Infrastructure.Interfaces;

namespace CommandBlock.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ModpacksController(IModrinthClient modrinth) : ControllerBase
    {
        /// <summary>Searches Modrinth for modpacks to create a server from.</summary>
        [HttpGet]
        [ProducesResponseType(typeof(IReadOnlyList<ModpackSearchResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status502BadGateway)]
        public async Task<IActionResult> Search([FromQuery] string query, CancellationToken cancellationToken)
        {
            try
            {
                var results = await modrinth.SearchModpacksAsync(query ?? string.Empty, cancellationToken);
                return Ok(results);
            }
            catch (HttpRequestException ex)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new { error = "Modrinth is unreachable: " + ex.Message });
            }
        }
    }
}

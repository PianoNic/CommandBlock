using Microsoft.AspNetCore.Mvc;
using CommandBlock.Application.Dtos.Files;
using CommandBlock.Infrastructure.Interfaces;

namespace CommandBlock.API.Controllers
{
    [ApiController]
    [Route("api/Server/{serverId:guid}/files")]
    public class FilesController(IServerFilesService files) : ControllerBase
    {
        [HttpGet]
        [ProducesResponseType(typeof(IReadOnlyList<FileEntry>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public Task<IActionResult> List(Guid serverId, [FromQuery] string path = "", CancellationToken ct = default)
            => Run(async () => Ok(await files.ListAsync(serverId, path, ct)));

        [HttpGet("content")]
        [ProducesResponseType(typeof(FileContent), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public Task<IActionResult> Read(Guid serverId, [FromQuery] string path, CancellationToken ct = default)
            => Run(async () => Ok(await files.ReadTextAsync(serverId, path, ct)));

        [HttpPut("content")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public Task<IActionResult> Write(Guid serverId, [FromBody] WriteFileDto body, CancellationToken ct = default)
            => Run(async () => { await files.WriteTextAsync(serverId, body.Path, body.Content, ct); return NoContent(); });

        [HttpGet("download")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Download(Guid serverId, [FromQuery] string path, CancellationToken ct = default)
        {
            try
            {
                var stream = await files.OpenReadAsync(serverId, path, ct);
                var name = path.Replace('\\', '/').TrimEnd('/');
                name = name[(name.LastIndexOf('/') + 1)..];
                return File(stream, "application/octet-stream", string.IsNullOrEmpty(name) ? "download" : name);
            }
            catch (FileServerNotFoundException) { return NotFound(); }
            catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        }

        [HttpPost("upload")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public Task<IActionResult> Upload(Guid serverId, [FromForm] string path, IFormFile file, CancellationToken ct = default)
            => Run(async () =>
            {
                if (file is null || file.Length == 0) return BadRequest(new { error = "A non-empty file is required." });
                var dest = string.IsNullOrWhiteSpace(path) ? file.FileName : path.TrimEnd('/') + "/" + file.FileName;
                await using var s = file.OpenReadStream();
                await files.UploadAsync(serverId, dest, s, ct);
                return NoContent();
            });

        [HttpPost("mkdir")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public Task<IActionResult> Mkdir(Guid serverId, [FromBody] PathDto body, CancellationToken ct = default)
            => Run(async () => { await files.MakeDirAsync(serverId, body.Path, ct); return NoContent(); });

        [HttpDelete]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public Task<IActionResult> Delete(Guid serverId, [FromQuery] string path, CancellationToken ct = default)
            => Run(async () => { await files.DeleteAsync(serverId, path, ct); return NoContent(); });

        [HttpPost("rename")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public Task<IActionResult> Rename(Guid serverId, [FromBody] RenameFileDto body, CancellationToken ct = default)
            => Run(async () => { await files.RenameAsync(serverId, body.From, body.To, ct); return NoContent(); });

        // Uniform error mapping: missing server -> 404, bad path / shell failure -> 400.
        private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
        {
            try { return await action(); }
            catch (FileServerNotFoundException) { return NotFound(); }
            catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        }
    }
}

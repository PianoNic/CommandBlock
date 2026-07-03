using Mediator;
using Microsoft.AspNetCore.Mvc;
using CommandBlock.Application.Command.Backup;
using CommandBlock.Application.Command.BackupSchedule;
using CommandBlock.Application.Command.Server;
using CommandBlock.Application.Dtos.Backup;
using CommandBlock.Application.Dtos.BackupSchedule;
using CommandBlock.Application.Dtos.Server;
using CommandBlock.Application.Queries.Backup;
using CommandBlock.Application.Queries.BackupSchedule;
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
                    body.Version, body.ModpackRef,
                    body.JavaVersion, body.UseAikarFlags, body.JvmArgs, body.ExtraEnv), cancellationToken);
                return CreatedAtAction(nameof(Create), new { id = result.Id }, result);
            }
            catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
            catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        }

        [HttpPut("{id:guid}/runtime")]
        [ProducesResponseType(typeof(ServerInstanceDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UpdateRuntime(Guid id, [FromBody] UpdateServerRuntimeDto body, CancellationToken cancellationToken)
        {
            try
            {
                var result = await mediator.Send(new RecreateServerCommand(
                    id, body.Memory, body.JavaVersion, body.UseAikarFlags, body.JvmArgs, body.ExtraEnv), cancellationToken);
                return Ok(result);
            }
            catch (ServerNotFoundException) { return NotFound(); }
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

        [HttpPost("{id:guid}/restart")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Restart(Guid id, CancellationToken cancellationToken)
        {
            try
            {
                await mediator.Send(new RestartServerCommand(id), cancellationToken);
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

        [HttpGet("{id:guid}/players")]
        [ProducesResponseType(typeof(PlayerListDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> ListPlayers(Guid id, CancellationToken cancellationToken)
        {
            return Ok(await mediator.Send(new ListPlayersQuery(id), cancellationToken));
        }

        [HttpGet("{id:guid}/properties")]
        [ProducesResponseType(typeof(ServerPropertiesDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetProperties(Guid id, CancellationToken cancellationToken)
        {
            return Ok(await mediator.Send(new GetServerPropertiesQuery(id), cancellationToken));
        }

        [HttpPut("{id:guid}/properties")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> UpdateProperties(Guid id, [FromBody] UpdateServerPropertiesDto body, CancellationToken cancellationToken)
        {
            try
            {
                await mediator.Send(new UpdateServerPropertiesCommand(id, body), cancellationToken);
                return NoContent();
            }
            catch (Exception)
            {
                // No container yet, or server.properties not generated - the server must run once first.
                return Conflict(new { error = "server.properties isn't available yet. Start the server once, then edit it." });
            }
        }

        // Anonymous so a plain <img> tag can load it (no bearer token on img requests). The icon is
        // the public in-game server-icon anyway, and ids are unguessable GUIDs.
        [Microsoft.AspNetCore.Authorization.AllowAnonymous]
        [HttpGet("{id:guid}/icon")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetIcon(Guid id, CancellationToken cancellationToken)
        {
            var png = await mediator.Send(new GetServerIconQuery(id), cancellationToken);
            if (png is null || png.Length == 0) return NotFound();
            return File(png, "image/png");
        }

        [HttpPost("{id:guid}/icon")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> SetIcon(Guid id, IFormFile file, CancellationToken cancellationToken)
        {
            if (file is null || file.Length == 0) return BadRequest(new { error = "No image uploaded." });
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, cancellationToken);
            try
            {
                await mediator.Send(new SetServerIconCommand(id, ms.ToArray()), cancellationToken);
                return NoContent();
            }
            catch (ServerNotFoundException) { return NotFound(); }
            catch (Exception) { return BadRequest(new { error = "That file isn't a readable image." }); }
        }

        [HttpDelete("{id:guid}/icon")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> RemoveIcon(Guid id, CancellationToken cancellationToken)
        {
            await mediator.Send(new RemoveServerIconCommand(id), cancellationToken);
            return NoContent();
        }

        [HttpPut("{id:guid}/wake")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UpdateWake(Guid id, [FromBody] UpdateWakeDto body, CancellationToken cancellationToken)
        {
            try
            {
                await mediator.Send(new UpdateWakeCommand(id, body.WakeOnConnect, body.WakeQueueSeconds), cancellationToken);
                return NoContent();
            }
            catch (ServerNotFoundException) { return NotFound(); }
        }

        [HttpGet("{id:guid}/backups")]
        [ProducesResponseType(typeof(IReadOnlyList<BackupEntryDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> ListBackups(Guid id, CancellationToken cancellationToken)
        {
            var result = await mediator.Send(new ListBackupsQuery(id), cancellationToken);
            return Ok(result);
        }

        [HttpPost("{id:guid}/backups")]
        [ProducesResponseType(typeof(BackupEntryDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> CreateBackup(Guid id, [FromQuery] string? kind, CancellationToken cancellationToken)
        {
            var backupKind = string.Equals(kind, "world", StringComparison.OrdinalIgnoreCase)
                ? CommandBlock.Domain.BackupKind.World
                : CommandBlock.Domain.BackupKind.Server;
            try
            {
                var result = await mediator.Send(new CreateBackupCommand(id, backupKind), cancellationToken);
                return CreatedAtAction(nameof(ListBackups), new { id }, result);
            }
            catch (ServerNotFoundException) { return NotFound(); }
            catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        }

        [HttpPost("backups/{backupId:guid}/restore")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> RestoreBackup(Guid backupId, CancellationToken cancellationToken)
        {
            try
            {
                await mediator.Send(new RestoreBackupCommand(backupId), cancellationToken);
                return NoContent();
            }
            catch (BackupNotFoundException) { return NotFound(); }
            catch (ServerNotFoundException) { return NotFound(); }
            catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        }

        [HttpPost("backups/{backupId:guid}/create-server")]
        [ProducesResponseType(typeof(ServerInstanceDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> CreateServerFromBackup(Guid backupId, [FromBody] CreateServerFromBackupDto body, CancellationToken cancellationToken)
        {
            try
            {
                var result = await mediator.Send(new CreateServerFromBackupCommand(backupId, body.DisplayName, body.Hostname), cancellationToken);
                return StatusCode(StatusCodes.Status201Created, result);
            }
            catch (BackupNotFoundException) { return NotFound(); }
            catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
            catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        }

        [HttpGet("{id:guid}/backup-schedules")]
        [ProducesResponseType(typeof(IReadOnlyList<BackupScheduleDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> ListBackupSchedules(Guid id, CancellationToken cancellationToken)
        {
            return Ok(await mediator.Send(new ListBackupSchedulesQuery(id), cancellationToken));
        }

        [HttpPost("{id:guid}/backup-schedules")]
        [ProducesResponseType(typeof(BackupScheduleDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> CreateBackupSchedule(Guid id, [FromBody] CreateBackupScheduleDto body, CancellationToken cancellationToken)
        {
            try
            {
                var result = await mediator.Send(new CreateBackupScheduleCommand(id, body.CronExpression), cancellationToken);
                return CreatedAtAction(nameof(ListBackupSchedules), new { id }, result);
            }
            catch (ServerNotFoundException) { return NotFound(); }
            catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        }

        [HttpPatch("backup-schedules/{scheduleId:guid}")]
        [ProducesResponseType(typeof(BackupScheduleDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> ToggleBackupSchedule(Guid scheduleId, [FromBody] ToggleBackupScheduleDto body, CancellationToken cancellationToken)
        {
            try
            {
                var result = await mediator.Send(new ToggleBackupScheduleCommand(scheduleId, body.Enabled), cancellationToken);
                return Ok(result);
            }
            catch (BackupScheduleNotFoundException) { return NotFound(); }
        }

        [HttpDelete("backup-schedules/{scheduleId:guid}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> DeleteBackupSchedule(Guid scheduleId, CancellationToken cancellationToken)
        {
            await mediator.Send(new DeleteBackupScheduleCommand(scheduleId), cancellationToken);
            return NoContent();
        }

        [HttpGet("backups/{backupId:guid}/download")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DownloadBackup(Guid backupId, CancellationToken cancellationToken)
        {
            var result = await mediator.Send(new DownloadBackupQuery(backupId), cancellationToken);
            if (result is null) return NotFound();
            // File() streams the S3 object straight through and disposes it when done.
            return File(result.Content, "application/octet-stream", result.FileName);
        }

        [HttpDelete("backups/{backupId:guid}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteBackup(Guid backupId, CancellationToken cancellationToken)
        {
            try
            {
                await mediator.Send(new DeleteBackupCommand(backupId), cancellationToken);
                return NoContent();
            }
            catch (BackupNotFoundException) { return NotFound(); }
        }
    }
}

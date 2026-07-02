using Mediator;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;
using CommandBlock.Infrastructure;
using CommandBlock.Infrastructure.Interfaces;

namespace CommandBlock.Application.Command.Server
{
    public record SetServerIconCommand(Guid ServerId, byte[] ImageData) : ICommand;
    public record RemoveServerIconCommand(Guid ServerId) : ICommand;

    public class SetServerIconCommandHandler(CommandBlockDbContext db, IServerFilesService files)
        : ICommandHandler<SetServerIconCommand>
    {
        public async ValueTask<Unit> Handle(SetServerIconCommand command, CancellationToken cancellationToken)
        {
            var server = await db.ServerInstances.FirstOrDefaultAsync(s => s.Id == command.ServerId, cancellationToken)
                ?? throw new ServerNotFoundException(command.ServerId);

            byte[] png;
            using (var image = Image.Load(command.ImageData))
            {
                // Center-crop to a square, then scale to Minecraft's 64x64 server-icon size.
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(64, 64),
                    Mode = ResizeMode.Crop,
                    Position = AnchorPositionMode.Center,
                }));
                using var ms = new MemoryStream();
                await image.SaveAsync(ms, new PngEncoder(), cancellationToken);
                png = ms.ToArray();
            }

            server.IconPng = png;
            await db.SaveChangesAsync(cancellationToken);

            // Best-effort: also drop it into the container as server-icon.png so it shows in-game too.
            if (server.ContainerId is not null)
            {
                try
                {
                    using var iconStream = new MemoryStream(png);
                    await files.UploadAsync(command.ServerId, "server-icon.png", iconStream, cancellationToken);
                }
                catch { /* container not running/reachable - the dashboard icon still works */ }
            }
            return Unit.Value;
        }
    }

    public class RemoveServerIconCommandHandler(CommandBlockDbContext db) : ICommandHandler<RemoveServerIconCommand>
    {
        public async ValueTask<Unit> Handle(RemoveServerIconCommand command, CancellationToken cancellationToken)
        {
            var server = await db.ServerInstances.FirstOrDefaultAsync(s => s.Id == command.ServerId, cancellationToken);
            if (server is null) return Unit.Value;
            server.IconPng = null;
            await db.SaveChangesAsync(cancellationToken);
            return Unit.Value;
        }
    }
}

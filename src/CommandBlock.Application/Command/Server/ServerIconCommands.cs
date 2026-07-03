using Mediator;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;
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

            var png = ToServerIconPng(command.ImageData);

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

        /// <summary>Center-crops the upload to a square and scales it to Minecraft's 64x64 server-icon
        /// size, returning PNG bytes.</summary>
        private static byte[] ToServerIconPng(byte[] imageData)
        {
            using var original = SKBitmap.Decode(imageData)
                ?? throw new ArgumentException("The uploaded file isn't a valid image.");

            // Center-crop to a square first.
            var side = Math.Min(original.Width, original.Height);
            var left = (original.Width - side) / 2;
            var top = (original.Height - side) / 2;
            var square = new SKRectI(left, top, left + side, top + side);

            using var cropped = new SKBitmap(side, side);
            if (!original.ExtractSubset(cropped, square))
                throw new ArgumentException("The uploaded image could not be processed.");

            // Scale the square down to the Minecraft server-icon size.
            using var resized = cropped.Resize(new SKImageInfo(64, 64), SKFilterQuality.High)
                ?? throw new ArgumentException("The uploaded image could not be processed.");

            using var image = SKImage.FromBitmap(resized);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
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

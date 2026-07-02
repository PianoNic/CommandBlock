using System.Formats.Tar;
using System.Text;
using Microsoft.EntityFrameworkCore;
using CommandBlock.Infrastructure.Interfaces;

namespace CommandBlock.Infrastructure.Services
{
    public class ServerFilesService(CommandBlockDbContext db, IDockerServiceResolver dockerResolver) : IServerFilesService
    {
        private const string Root = "/data";
        private const long MaxTextBytes = 2 * 1024 * 1024;

        private async Task<(IDockerService docker, string containerId)> ResolveAsync(Guid serverId, CancellationToken ct)
        {
            var server = await db.ServerInstances.AsNoTracking().FirstOrDefaultAsync(s => s.Id == serverId, ct)
                ?? throw new FileServerNotFoundException(serverId);
            if (server.ContainerId is null) throw new FileServerNotFoundException(serverId);
            return (dockerResolver.Resolve(server.NodeId), server.ContainerId);
        }

        /// <summary>Resolves a client path (relative to /data) to a normalized absolute container path,
        /// rejecting anything that escapes /data.</summary>
        private static string Full(string relative)
        {
            var parts = new List<string> { "data" };
            foreach (var seg in (relative ?? "").Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                if (seg == ".") continue;
                if (seg == "..")
                {
                    if (parts.Count <= 1) throw new InvalidOperationException("Path escapes the server data directory.");
                    parts.RemoveAt(parts.Count - 1);
                    continue;
                }
                parts.Add(seg);
            }
            return "/" + string.Join('/', parts);
        }

        public async Task<IReadOnlyList<FileEntry>> ListAsync(Guid serverId, string path, CancellationToken ct = default)
        {
            var (docker, id) = await ResolveAsync(serverId, ct);
            var full = Full(path);

            // %y=type (d/f/l), %s=size, %f=filename. \t/\n are interpreted by find.
            var bytes = await docker.ExecCaptureAsync(id,
                new[] { "find", full, "-maxdepth", "1", "-mindepth", "1", "-printf", "%y\\t%s\\t%f\\n" }, ct);

            var entries = new List<FileEntry>();
            foreach (var line in Encoding.UTF8.GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var cols = line.Split('\t', 3);
                if (cols.Length != 3) continue;
                var isDir = cols[0] == "d";
                long.TryParse(cols[1], out var size);
                entries.Add(new FileEntry(cols[2], isDir, isDir ? 0 : size));
            }
            return entries
                .OrderByDescending(e => e.IsDirectory)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public async Task<FileContent> ReadTextAsync(Guid serverId, string path, CancellationToken ct = default)
        {
            var (docker, id) = await ResolveAsync(serverId, ct);
            await using var raw = await FirstTarEntryAsync(docker, id, Full(path), ct);

            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int n;
            var truncated = false;
            while ((n = await raw.ReadAsync(chunk, ct)) > 0)
            {
                if (buffer.Length + n > MaxTextBytes) { await buffer.WriteAsync(chunk.AsMemory(0, (int)(MaxTextBytes - buffer.Length)), ct); truncated = true; break; }
                await buffer.WriteAsync(chunk.AsMemory(0, n), ct);
            }

            var data = buffer.ToArray();
            var binary = Array.IndexOf(data, (byte)0) >= 0;
            return new FileContent(binary ? "" : Encoding.UTF8.GetString(data), truncated, binary);
        }

        public async Task WriteTextAsync(Guid serverId, string path, string content, CancellationToken ct = default)
        {
            using var body = new MemoryStream(Encoding.UTF8.GetBytes(content ?? ""));
            await UploadAsync(serverId, path, body, ct);
        }

        public async Task<Stream> OpenReadAsync(Guid serverId, string path, CancellationToken ct = default)
        {
            var (docker, id) = await ResolveAsync(serverId, ct);
            return await FirstTarEntryAsync(docker, id, Full(path), ct);
        }

        public async Task UploadAsync(Guid serverId, string path, Stream content, CancellationToken ct = default)
        {
            var (docker, id) = await ResolveAsync(serverId, ct);
            var full = Full(path);
            var dir = full[..full.LastIndexOf('/')];
            if (dir.Length == 0) dir = "/";
            var name = full[(full.LastIndexOf('/') + 1)..];
            if (name.Length == 0) throw new InvalidOperationException("A file name is required.");

            using var payload = new MemoryStream();
            await content.CopyToAsync(payload, ct);

            using var tar = new MemoryStream();
            await using (var writer = new TarWriter(tar, TarEntryFormat.Ustar, leaveOpen: true))
            {
                var entry = new UstarTarEntry(TarEntryType.RegularFile, name)
                {
                    Mode = (UnixFileMode)0b110_100_100, // rw-r--r--
                    DataStream = new MemoryStream(payload.ToArray()),
                };
                await writer.WriteEntryAsync(entry, ct);
            }
            tar.Position = 0;
            await docker.ExtractArchiveAsync(id, dir, tar, ct);
        }

        public async Task MakeDirAsync(Guid serverId, string path, CancellationToken ct = default)
        {
            var (docker, id) = await ResolveAsync(serverId, ct);
            await docker.ExecCaptureAsync(id, new[] { "mkdir", "-p", Full(path) }, ct);
        }

        public async Task DeleteAsync(Guid serverId, string path, CancellationToken ct = default)
        {
            var (docker, id) = await ResolveAsync(serverId, ct);
            var full = Full(path);
            if (full == Root) throw new InvalidOperationException("Refusing to delete the data root.");
            await docker.ExecCaptureAsync(id, new[] { "rm", "-rf", full }, ct);
        }

        public async Task RenameAsync(Guid serverId, string fromPath, string toPath, CancellationToken ct = default)
        {
            var (docker, id) = await ResolveAsync(serverId, ct);
            await docker.ExecCaptureAsync(id, new[] { "mv", Full(fromPath), Full(toPath) }, ct);
        }

        /// <summary>Copies a single file out of the container and returns its bytes as a seekable
        /// stream. Docker's archive comes over a chunked HTTP stream that TarReader can't read
        /// incrementally, so buffer it whole first, then hand back a self-contained copy of the entry.</summary>
        private static async Task<Stream> FirstTarEntryAsync(IDockerService docker, string containerId, string full, CancellationToken ct)
        {
            using var buffer = new MemoryStream();
            await using (var archive = await docker.GetArchiveAsync(containerId, full, ct))
                await archive.CopyToAsync(buffer, ct);
            buffer.Position = 0;

            using var reader = new TarReader(buffer, leaveOpen: true);
            while (await reader.GetNextEntryAsync(cancellationToken: ct) is { } entry)
            {
                if (entry.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile)
                {
                    // A 0-byte file has a null DataStream - that's an empty file, not an error.
                    var data = new MemoryStream();
                    if (entry.DataStream is not null) await entry.DataStream.CopyToAsync(data, ct);
                    data.Position = 0;
                    return data;
                }
            }
            throw new InvalidOperationException("Not a regular file.");
        }
    }
}

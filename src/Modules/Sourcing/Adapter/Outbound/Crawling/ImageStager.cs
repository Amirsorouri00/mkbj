using System.Security.Cryptography;
using Mkbj.Core.Application.FileTypeDetection;
using Mkbj.Modules.Sourcing.Application.Ports.Outbound;
using Mkbj.Storage;
using Microsoft.Extensions.Options;

namespace Mkbj.Modules.Sourcing.Adapter.Outbound.Crawling;

internal sealed class ImageStager(
    IHttpClientFactory clients,
    IFileTypeDetector fileTypes,
    IObjectStorage storage,
    IOptions<CrawlerOptions> options) : IImageStager
{
    public async Task<IReadOnlyList<string>> StageAsync(
        IReadOnlyList<string> imageUrls, int max, CancellationToken ct = default)
    {
        var keys = new List<string>();
        foreach (var value in imageUrls.Distinct(StringComparer.Ordinal).Take(Math.Clamp(max, 0, 10)))
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https")) continue;

            try
            {
                using var response = await clients.CreateClient(WebsiteCrawlerAdapter.ClientName)
                    .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!response.IsSuccessStatusCode
                    || response.Content.Headers.ContentLength > options.Value.MaxImageBytes) continue;

                await using var source = await response.Content.ReadAsStreamAsync(ct);
                using var buffer = new MemoryStream();
                var chunk = new byte[81920];
                while (true)
                {
                    var read = await source.ReadAsync(chunk, ct);
                    if (read == 0) break;
                    if (buffer.Length + read > options.Value.MaxImageBytes)
                    {
                        buffer.SetLength(0);
                        break;
                    }
                    await buffer.WriteAsync(chunk.AsMemory(0, read), ct);
                }
                if (buffer.Length == 0) continue;

                var bytes = buffer.ToArray();
                var type = fileTypes.Detect(bytes).FileType;
                if (type is not (FileType.Jpeg or FileType.Png or FileType.Gif or FileType.Webp))
                    continue;

                var extension = FileTypeMetadata.GetExtension(type)!;
                var key = $"images/{Convert.ToHexStringLower(SHA256.HashData(bytes))}{extension}";
                if (!await storage.ExistsAsync(StorageBuckets.ImportStaging, key, ct))
                {
                    await using var content = new MemoryStream(bytes, writable: false);
                    await storage.PutAsync(
                        StorageBuckets.ImportStaging, key, content,
                        FileTypeMetadata.GetMimeType(type)!, ct);
                }
                keys.Add(key);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A broken optional image must not abort the remaining product rows.
            }
        }
        return keys;
    }
}

namespace EwaysVNext.Modules.Catalog.Application.Products.Services;

/// <summary>
/// The bytes already read from a stream, followed by whatever is left of it.
/// <para>
/// A multipart upload is forward-only, so identifying the file consumes its first bytes and they
/// cannot be rewound. Rather than buffer the whole upload in memory to get a seekable copy — the
/// thing the size limit exists to avoid — the header is replayed in front of the remainder.
/// </para>
/// </summary>
internal sealed class ConcatenatedStream(ReadOnlyMemory<byte> head, Stream tail) : Stream
{
    private int _headPosition;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return Read(buffer.AsSpan(offset, count));
    }

    public override int Read(Span<byte> buffer)
    {
        if (_headPosition < head.Length)
        {
            var take = Math.Min(buffer.Length, head.Length - _headPosition);
            head.Span.Slice(_headPosition, take).CopyTo(buffer);
            _headPosition += take;
            return take;
        }

        return tail.Read(buffer);
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        if (_headPosition < head.Length)
        {
            var take = Math.Min(buffer.Length, head.Length - _headPosition);
            head.Slice(_headPosition, take).CopyTo(buffer);
            _headPosition += take;
            return take;
        }

        return await tail.ReadAsync(buffer, ct);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
    }

    public override void Flush() { }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

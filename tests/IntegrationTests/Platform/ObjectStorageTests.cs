using System.Net;
using System.Text;
using AwesomeAssertions;
using EwaysVNext.Storage;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EwaysVNext.IntegrationTests.Platform;

/// <summary>
/// The object-storage adapter against a real S3 server. Presigned-URL expiry is exercised for real
/// rather than asserted on the TTL value, because an invoice link that outlives its window is a
/// disclosure.
/// </summary>
[Collection(PostgresSuite.Name)]
public sealed class ObjectStorageTests(PostgresFixture fixture)
{
    private static readonly byte[] Bytes = Encoding.UTF8.GetBytes("invoice-pdf-bytes");

    [Fact]
    public async Task Put_Then_Get_Returns_The_Same_Bytes()
    {
        var storage = Storage();
        var key = NewKey();

        await storage.PutAsync(StorageBuckets.Documents, key, new MemoryStream(Bytes), "application/pdf");

        await using var stream = await storage.GetAsync(StorageBuckets.Documents, key);
        stream.Should().NotBeNull();
        (await ReadAllAsync(stream!)).Should().BeEquivalentTo(Bytes);
    }

    [Fact]
    public async Task Get_Missing_Returns_Null() =>
        (await Storage().GetAsync(StorageBuckets.Documents, "missing/x.pdf")).Should().BeNull();

    [Fact]
    public async Task Exists_Follows_Put_And_Delete()
    {
        var storage = Storage();
        var key = NewKey();

        (await storage.ExistsAsync(StorageBuckets.Documents, key)).Should().BeFalse();

        await storage.PutAsync(StorageBuckets.Documents, key, new MemoryStream(Bytes), "application/pdf");
        (await storage.ExistsAsync(StorageBuckets.Documents, key)).Should().BeTrue();

        await storage.DeleteAsync(StorageBuckets.Documents, key);
        (await storage.ExistsAsync(StorageBuckets.Documents, key)).Should().BeFalse();
    }

    [Fact]
    public async Task Copy_Moves_Bytes_Between_Buckets()
    {
        var storage = Storage();
        var key = NewKey();

        await storage.PutAsync(StorageBuckets.ImportStaging, key, new MemoryStream(Bytes), "image/jpeg");
        await storage.CopyAsync(StorageBuckets.ImportStaging, key, StorageBuckets.CatalogMedia, key);

        // Publishing copies; the staged original stays until the cleanup job removes it.
        (await storage.ExistsAsync(StorageBuckets.ImportStaging, key)).Should().BeTrue();

        await using var copied = await storage.GetAsync(StorageBuckets.CatalogMedia, key);
        (await ReadAllAsync(copied!)).Should().BeEquivalentTo(Bytes);
    }

    [Fact]
    public async Task Presigned_Url_Downloads_Then_Expires()
    {
        var storage = Storage();
        var key = NewKey();
        await storage.PutAsync(StorageBuckets.Documents, key, new MemoryStream(Bytes), "application/pdf");

        var url = await storage.GetPresignedUrlAsync(StorageBuckets.Documents, key, TimeSpan.FromSeconds(2));

        using var http = new HttpClient();
        (await http.GetByteArrayAsync(url)).Should().BeEquivalentTo(Bytes);

        await Task.Delay(TimeSpan.FromSeconds(4));

        var afterExpiry = await http.GetAsync(url);
        afterExpiry.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(604801)]
    public async Task Presign_Rejects_Ttl_Outside_One_Second_To_Seven_Days(int seconds)
    {
        var act = () => Storage().GetPresignedUrlAsync(
            StorageBuckets.Documents, NewKey(), TimeSpan.FromSeconds(seconds));

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    private IObjectStorage Storage() =>
        fixture.CreateFactory().Services.GetRequiredService<IObjectStorage>();

    private static string NewKey() => $"tests/{Guid.NewGuid():N}.bin";

    private static async Task<byte[]> ReadAllAsync(Stream stream)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }
}

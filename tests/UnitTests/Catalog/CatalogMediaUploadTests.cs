using AwesomeAssertions;
using EwaysVNext.Audit;
using EwaysVNext.Core.Application;
using EwaysVNext.Core.Application.FileTypeDetection;
using EwaysVNext.Core.Implementation.FileTypeDetection;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Catalog.Application.Brands.Ports.Outbound;
using EwaysVNext.Modules.Catalog.Application.Categories.Ports.Outbound;
using EwaysVNext.Modules.Catalog.Application.Products;
using EwaysVNext.Modules.Catalog.Application.Products.Domain;
using EwaysVNext.Modules.Catalog.Application.Products.Ports.Inbound;
using EwaysVNext.Modules.Catalog.Application.Products.Ports.Outbound;
using EwaysVNext.Modules.Catalog.Application.Products.Services;
using EwaysVNext.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace EwaysVNext.UnitTests.Catalog;

/// <summary>
/// Image upload. The two rules worth pinning are that the file's type comes from its bytes
/// rather than from anything the uploader said, and that an object is never left referenced by a
/// row that does not exist — or the other way round.
/// </summary>
public sealed class CatalogMediaUploadTests
{
    private const long ProductId = 1;
    private const long BrandId = 10;
    private const long CategoryId = 20;
    private const long MediaId = 777;

    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly ICatalogMediaStore _media = Substitute.For<ICatalogMediaStore>();
    private readonly IFileTypeDetector _fileTypes = new FileTypeDetector();
    private readonly IOptions<CatalogMediaOptions> _mediaOptions = Options.Create(new CatalogMediaOptions());
    private readonly ILogger<CatalogAdminService> _logger = NullLogger<CatalogAdminService>.Instance;
    private readonly IBrandRepository _brands = Substitute.For<IBrandRepository>();
    private readonly ICategoryRepository _categories = Substitute.For<ICategoryRepository>();
    private readonly ICategoryProductRepository _categoryProducts = Substitute.For<ICategoryProductRepository>();
    private readonly IAuditTrail _audit = Substitute.For<IAuditTrail>();
    private readonly ICatalogChangeNotifier _notifier = Substitute.For<ICatalogChangeNotifier>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IIdGenerator _ids = Substitute.For<IIdGenerator>();
    private readonly FixedClock _clock = new();
    private readonly AdminActor _actor = new(Guid.NewGuid(), IsSuperAdmin: false);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public CatalogMediaUploadTests() => _ids.Generate().Returns(MediaId);

    private CatalogAdminService CreateService() => new(
        _products, _media, _fileTypes, _mediaOptions, _logger, _brands, _categories,
        _categoryProducts, _audit, _notifier, _unitOfWork, _ids, _clock);

    private Product Draft()
    {
        var product = Product.CreateDraft(
            ProductId, BrandId, CategoryId, "Demo Kettle", "demo-kettle", null, null, _clock.UtcNow);
        _products.GetByIdAsync(ProductId, Arg.Any<CancellationToken>()).Returns(product);
        return product;
    }

    private static UploadMediaCommand Upload(byte[] content, long? length = null) =>
        new(ProductId, new MemoryStream(content), length ?? content.Length, "a kettle",
            MediaUsageRights.Allowed, null);

    [Fact]
    public async Task Upload_Png_Stores_Object_Then_Saves()
    {
        var product = Draft();

        var result = await CreateService().UploadMediaAsync(Upload(FileSamples.Png), _actor, Ct);

        result.IsSuccess.Should().BeTrue();
        Received.InOrder(() =>
        {
            _media.PutAsync($"products/{ProductId}/{MediaId}.png", Arg.Any<Stream>(), "image/png",
                Arg.Any<CancellationToken>());
            _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>());
        });

        product.Media.Single().ObjectKey.Should().Be($"products/{ProductId}/{MediaId}.png");
    }

    [Fact]
    public async Task Uploaded_Bytes_Reach_Storage_Whole_Despite_The_Type_Sniff()
    {
        Draft();
        byte[]? stored = null;
        await _media.PutAsync(
            Arg.Any<string>(),
            Arg.Do<Stream>(stream =>
            {
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                stored = buffer.ToArray();
            }),
            Arg.Any<string>(), Arg.Any<CancellationToken>());

        await CreateService().UploadMediaAsync(Upload(FileSamples.Png), _actor, Ct);

        stored.Should().BeEquivalentTo(FileSamples.Png,
            "identifying the file consumes its first bytes, and a forward-only upload cannot be "
            + "rewound — those bytes have to be replayed in front of the remainder");
    }

    [Fact]
    public async Task A_Stream_That_Cannot_Rewind_Still_Reaches_Storage_Whole()
    {
        Draft();
        byte[]? stored = null;
        await _media.PutAsync(
            Arg.Any<string>(),
            Arg.Do<Stream>(stream =>
            {
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                stored = buffer.ToArray();
            }),
            Arg.Any<string>(), Arg.Any<CancellationToken>());

        var command = new UploadMediaCommand(
            ProductId, new ForwardOnlyStream(FileSamples.Png), FileSamples.Png.Length, null,
            MediaUsageRights.Allowed, null);

        await CreateService().UploadMediaAsync(command, _actor, Ct);

        stored.Should().BeEquivalentTo(FileSamples.Png,
            "identifying the file consumes its first bytes and this stream cannot be rewound, so "
            + "they have to be replayed in front of the remainder");
    }

    /// <summary>A stream with no rewind, like a raw network body before anything buffers it.</summary>
    private sealed class ForwardOnlyStream(byte[] content) : Stream
    {
        private readonly MemoryStream _inner = new(content);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => _inner.Read(buffer);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }

    [Fact]
    public async Task Upload_Rejects_Pdf_Without_Touching_Storage()
    {
        Draft();

        var result = await CreateService().UploadMediaAsync(Upload(FileSamples.Pdf), _actor, Ct);

        result.Error!.Code.Should().Be("MEDIA_TYPE_UNSUPPORTED");
        result.Error.HttpStatus.Should().Be(415);
        await _media.DidNotReceiveWithAnyArgs().PutAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task Upload_Rejects_An_Executable_Named_Like_An_Image()
    {
        Draft();

        var result = await CreateService().UploadMediaAsync(Upload(FileSamples.Executable), _actor, Ct);

        result.Error!.Code.Should().Be("MEDIA_TYPE_UNSUPPORTED",
            "the type comes from the bytes; a file name or a declared content type is the "
            + "uploader's claim, and an executable renamed to .png would pass either");
    }

    [Fact]
    public async Task Upload_Rejects_Over_Limit()
    {
        Draft();

        var result = await CreateService().UploadMediaAsync(
            Upload(FileSamples.Png, length: 10_485_761), _actor, Ct);

        result.Error!.Code.Should().Be("MEDIA_TOO_LARGE");
        result.Error.HttpStatus.Should().Be(413);
        await _products.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
    }

    [Fact]
    public async Task Exactly_The_Limit_Is_Accepted()
    {
        Draft();

        var result = await CreateService().UploadMediaAsync(
            Upload(FileSamples.Png, length: 10_485_760), _actor, Ct);

        result.IsSuccess.Should().BeTrue("the limit is inclusive");
    }

    [Fact]
    public async Task Failed_Save_Deletes_Uploaded_Object()
    {
        Draft();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns<Task<int>>(_ => throw new InvalidOperationException("save failed"));

        var act = async () => await CreateService().UploadMediaAsync(Upload(FileSamples.Jpeg), _actor, Ct);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await _media.Received(1).DeleteAsync(
            $"products/{ProductId}/{MediaId}.jpg", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_Failed_Cleanup_Does_Not_Replace_The_Original_Failure()
    {
        Draft();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns<Task<int>>(_ => throw new InvalidOperationException("save failed"));
        _media.DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new TimeoutException("storage is down"));

        var act = async () => await CreateService().UploadMediaAsync(Upload(FileSamples.Png), _actor, Ct);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("save failed", "a second failure must not hide the first");
    }

    [Fact]
    public async Task Sort_Order_Defaults_To_The_End_Of_The_Gallery()
    {
        var product = Draft();
        product.AddMedia(1, "products/1/1.png", 0, null, null, MediaUsageRights.Allowed, _clock.UtcNow);
        product.AddMedia(2, "products/1/2.png", 7, null, null, MediaUsageRights.Allowed, _clock.UtcNow);

        await CreateService().UploadMediaAsync(Upload(FileSamples.Webp), _actor, Ct);

        product.Media.Single(image => image.Id == MediaId).SortOrder.Should().Be(8);
    }

    [Fact]
    public async Task Removing_Media_Saves_Before_Deleting_The_Object()
    {
        var product = Draft();
        product.AddMedia(MediaId, "products/1/777.png", 0, null, null, MediaUsageRights.Allowed, _clock.UtcNow);

        var result = await CreateService().RemoveMediaAsync(ProductId, MediaId, _actor, Ct);

        result.IsSuccess.Should().BeTrue();
        Received.InOrder(() =>
        {
            _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>());
            _media.DeleteAsync("products/1/777.png", Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Removing_Media_That_Is_Not_There_Touches_Neither_Store_Nor_Save()
    {
        Draft();

        var result = await CreateService().RemoveMediaAsync(ProductId, 999, _actor, Ct);

        result.Error!.Code.Should().Be("PRODUCT_MEDIA_NOT_FOUND");
        await _media.DidNotReceiveWithAnyArgs().DeleteAsync(default!, default);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Uploading_To_A_Missing_Product_Stores_Nothing()
    {
        _products.GetByIdAsync(ProductId, Arg.Any<CancellationToken>()).Returns((Product?)null);

        var result = await CreateService().UploadMediaAsync(Upload(FileSamples.Png), _actor, Ct);

        result.Error!.Code.Should().Be("PRODUCT_NOT_FOUND");
        await _media.DidNotReceiveWithAnyArgs().PutAsync(default!, default!, default!, default);
    }
}

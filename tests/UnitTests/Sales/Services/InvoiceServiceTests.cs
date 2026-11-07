using AwesomeAssertions;
using EwaysVNext.Modules.Sales.Application.Domain.Documents;
using EwaysVNext.Modules.Sales.Application.Domain.Orders;
using EwaysVNext.Modules.Sales.Application.Ports.Outbound;
using EwaysVNext.Modules.Sales.Application.Services;
using Xunit;

namespace EwaysVNext.UnitTests.Sales.Services;

/// <summary>
/// Handing out a link to an invoice PDF.
/// </summary>
/// <remarks>
/// Rendering is lazy on purpose: a PDF library and an object store on the path of every sale
/// would be two more things that can be slow or down between a partner paying and an order
/// existing. Most invoices are never downloaded at all.
/// </remarks>
public sealed class InvoiceServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_Document_The_Caller_Cannot_See_Is_Not_Found_And_Is_Never_Rendered()
    {
        var (service, renderer, store) = Build(document: null);

        var result = await service.GetPdfUrlAsync(new DocumentAccess.Admin(), 1, Ct);

        result.Error!.Code.Should().Be("NOT_FOUND");
        renderer.Renders.Should().Be(0, "rendering a document nobody may see would be work done "
            + "on behalf of someone probing for ids");
        store.Puts.Should().BeEmpty();
    }

    [Fact]
    public async Task The_First_Request_Renders_And_Stores_Under_The_Recorded_Key()
    {
        var (service, renderer, store) = Build(Document());

        var url = await service.GetPdfUrlAsync(new DocumentAccess.Admin(), 1, Ct);

        url.IsSuccess.Should().BeTrue();
        renderer.Renders.Should().Be(1);

        // The key the aggregate recorded, so the file an order points at is the file written.
        store.Puts.Should().Equal("1001/INV-000123.pdf");
    }

    [Fact]
    public async Task A_Later_Request_Serves_What_Is_Already_There()
    {
        var (service, renderer, store) = Build(Document());
        store.Existing.Add("1001/INV-000123.pdf");

        await service.GetPdfUrlAsync(new DocumentAccess.Admin(), 1, Ct);

        renderer.Renders.Should().Be(0, "a document is rendered once, not once per download");
        store.Puts.Should().BeEmpty();
    }

    [Fact]
    public async Task The_Link_Comes_From_The_Store()
    {
        var (service, _, _) = Build(Document());

        var url = await service.GetPdfUrlAsync(new DocumentAccess.Admin(), 1, Ct);

        url.Value!.Should().Be(new Uri("https://example.invalid/1001/INV-000123.pdf"));
    }

    [Fact]
    public async Task The_Callers_Identity_Reaches_The_Query()
    {
        var (service, _, _, orders) = BuildWithRepository(Document());
        var access = new DocumentAccess.Partner(7);

        await service.GetPdfUrlAsync(access, 1, Ct);

        // Scoping happens in the query, not afterwards — so what the caller is allowed to see is
        // decided before any content is read into this process.
        orders.LastAccess.Should().Be(access);
    }

    private static (InvoiceService Service, StubRenderer Renderer, StubStore Store) Build(
        DocumentForRendering? document)
    {
        var (service, renderer, store, _) = BuildWithRepository(document);
        return (service, renderer, store);
    }

    private static (InvoiceService, StubRenderer, StubStore, StubOrders) BuildWithRepository(
        DocumentForRendering? document)
    {
        var orders = new StubOrders(document);
        var renderer = new StubRenderer();
        var store = new StubStore();

        return (new InvoiceService(orders, renderer, store), renderer, store, orders);
    }

    private static DocumentForRendering Document() => new(
        OrderId: 1001,
        DocumentId: 1,
        DocumentType.Commercial,
        "INV-000123",
        new DocumentContent(
            "Invoice", "Commercial", "INV-000123", DateTimeOffset.UnixEpoch, "ORD-000123",
            "PartnerApi", null,
            new SellerBlock("S", "U", "A", "E", "F"),
            new BuyerBlock(1, null, null, null, "b@x.test", null, null, null),
            [], "USD", 0, 0, 0, "Not GST registered",
            DocumentWording.FulfillmentStatement, "Paid", null, null, null, null, null),
        "1001/INV-000123.pdf");

    private sealed class StubOrders(DocumentForRendering? document) : IOrderRepository
    {
        public DocumentAccess? LastAccess { get; private set; }

        public Task<DocumentForRendering?> FindDocumentAsync(
            long documentId, DocumentAccess access, CancellationToken ct = default)
        {
            LastAccess = access;
            return Task.FromResult(document);
        }

        public Task<Order?> GetByIdAsync(long orderId, CancellationToken ct = default) =>
            Task.FromResult<Order?>(null);

        public Task<PartnerOrderKey?> FindPartnerOrderKeyAsync(
            long partnerId, string externalOrderId, CancellationToken ct = default) =>
            Task.FromResult<PartnerOrderKey?>(null);

        public Task<bool> TryInsertPartnerOrderAsync(Order order, CancellationToken ct = default) =>
            Task.FromResult(true);

        public Task AddAsync(Order order, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubRenderer : IDocumentPdfRenderer
    {
        public int Renders { get; private set; }

        public byte[] Render(DocumentContent content)
        {
            Renders++;
            return [1, 2, 3];
        }
    }

    private sealed class StubStore : IDocumentStore
    {
        public HashSet<string> Existing { get; } = new(StringComparer.Ordinal);
        public List<string> Puts { get; } = [];

        public Task<bool> ExistsAsync(string key, CancellationToken ct = default) =>
            Task.FromResult(Existing.Contains(key));

        public Task PutPdfAsync(string key, byte[] pdf, CancellationToken ct = default)
        {
            Puts.Add(key);
            Existing.Add(key);

            return Task.CompletedTask;
        }

        public Task<Uri> GetDownloadUrlAsync(string key, CancellationToken ct = default) =>
            Task.FromResult(new Uri($"https://example.invalid/{key}"));
    }
}

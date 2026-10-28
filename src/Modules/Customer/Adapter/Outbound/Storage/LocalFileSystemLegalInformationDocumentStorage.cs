using EwaysVNext.Modules.Customer.Application.Domain.Enums;
using EwaysVNext.Modules.Customer.Application.Ports.Outbound;
using Microsoft.Extensions.Configuration;

namespace EwaysVNext.Modules.Customer.Adapter.Outbound.Storage;

internal sealed class LocalFileSystemLegalInformationDocumentStorage : ILegalInformationDocumentStorage
{
    private readonly string _rootPath;

    public LocalFileSystemLegalInformationDocumentStorage(IConfiguration configuration)
    {
        _rootPath = configuration["Customer:LegalInformationDocuments:RootPath"]
            ?? Path.Combine(AppContext.BaseDirectory, "App_Data", "legal-information-documents");
        Directory.CreateDirectory(_rootPath);
    }

    public async Task<string> SaveAsync(
        long customerId,
        long documentId,
        LegalInformationDocumentType type,
        Stream content,
        string fileName,
        CancellationToken ct = default)
    {
        var extension = Path.GetExtension(fileName);
        var relativeKey = Path.Combine(customerId.ToString(), type.ToString(), $"{documentId}{extension}");
        var fullPath = Path.Combine(_rootPath, relativeKey);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        if (content.CanSeek)
            content.Position = 0;

        await using var fileStream = File.Create(fullPath);
        await content.CopyToAsync(fileStream, ct);

        return relativeKey.Replace(Path.DirectorySeparatorChar, '/');
    }
}

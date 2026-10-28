using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Customer.Application.Domain.Enums;

namespace EwaysVNext.Modules.Customer.Application.Domain.Customers;

public sealed class LegalInformationDocument
{
    public long Id { get; private set; }
    public long LegalInformationId { get; private set; }
    public int Type { get; private set; }
    public string StorageKey { get; private set; } = string.Empty;
    public string FileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private LegalInformationDocument() { }

    internal static LegalInformationDocument Create(
        long id,
        long legalInformationId,
        LegalInformationDocumentType type,
        string storageKey,
        string fileName,
        string contentType,
        long sizeBytes,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
            throw new DomainException("LEGALINFO_DOCUMENT_STORAGEKEY_REQUIRED", "A storage reference is required.");

        return new LegalInformationDocument
        {
            Id = id,
            LegalInformationId = legalInformationId,
            Type = (int)type,
            StorageKey = storageKey,
            FileName = fileName,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            CreatedAt = now.UtcDateTime
        };
    }
}

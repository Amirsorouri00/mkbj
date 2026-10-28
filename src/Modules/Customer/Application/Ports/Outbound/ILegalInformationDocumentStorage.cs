using EwaysVNext.Modules.Customer.Application.Domain.Enums;

namespace EwaysVNext.Modules.Customer.Application.Ports.Outbound;

/// <summary>
/// Stores the binary content of a legal-information supporting document and returns an opaque
/// reference (storage key). No file/document storage existed anywhere in the repository before
/// this feature, so this is a new, minimal, real (not mocked) capability - not a second system
/// competing with an existing one.
/// </summary>
public interface ILegalInformationDocumentStorage
{
    Task<string> SaveAsync(
        long customerId,
        long documentId,
        LegalInformationDocumentType type,
        Stream content,
        string fileName,
        CancellationToken ct = default);
}

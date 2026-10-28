using EwaysVNext.Outcomes;
using EwaysVNext.Modules.Customer.Application.Domain.Enums;
using EwaysVNext.Modules.Customer.Application.Ports.Inbound.Dtos;

namespace EwaysVNext.Modules.Customer.Application.Ports.Inbound;

public interface ILegalInformationInboundPort
{
    Task<Result<LegalInformationResponse>> GetLegalInformationAsync(
        Guid identityUserId,
        CancellationToken ct = default);

    Task<Result<LegalInformationResponse>> SaveLegalInformationDraftAsync(
        Guid identityUserId,
        LegalInformationInput input,
        CancellationToken ct = default);

    Task<Result<LegalInformationResponse>> UploadLegalInformationDocumentAsync(
        Guid identityUserId,
        LegalInformationDocumentType type,
        Stream content,
        string fileName,
        string declaredContentType,
        long sizeBytes,
        CancellationToken ct = default);

    Task<Result<LegalInformationResponse>> SubmitLegalInformationAsync(
        Guid identityUserId,
        CancellationToken ct = default);

    // Expert-facing (Admin policy)
    Task<Result<LegalInformationResponse>> ApproveLegalInformationAsync(
        long customerId,
        Guid reviewingIdentityUserId,
        CancellationToken ct = default);

    Task<Result<LegalInformationResponse>> RejectLegalInformationAsync(
        long customerId,
        Guid reviewingIdentityUserId,
        string reason,
        LegalInformationEditableField editableFields,
        CancellationToken ct = default);
}

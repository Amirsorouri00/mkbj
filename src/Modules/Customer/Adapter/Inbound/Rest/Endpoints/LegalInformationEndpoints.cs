using EwaysVNext.Core.Application;
using EwaysVNext.Outcomes;
using EwaysVNext.Presentation.Endpoint;
using EwaysVNext.Modules.Customer.Adapter.Inbound.Rest.Dtos;
using EwaysVNext.Modules.Customer.Application.Domain.Enums;
using EwaysVNext.Modules.Customer.Application.Ports.Inbound;
using EwaysVNext.Modules.Customer.Application.Ports.Inbound.Dtos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace EwaysVNext.Modules.Customer.Adapter.Inbound.Rest.Endpoints;

public sealed class LegalInformationEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/customers").WithTags("LegalInformation");

        group.MapGet("/me/legal-information", GetLegalInformation)
            .WithName("get-legal-information")
            .WithDescription("get the authenticated customer's legal information, its status and which fields are currently editable")
            .Produces<Result<LegalInformationResponse>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .RequireAuthorization();

        group.MapPut("/me/legal-information", SaveLegalInformationDraft)
            .WithName("save-legal-information-draft")
            .WithDescription("create or update the authenticated customer's legal-information draft")
            .Produces<Result<LegalInformationResponse>>(StatusCodes.Status200OK)
            .Produces<Result<LegalInformationResponse>>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .RequireAuthorization();

        group.MapPost("/me/legal-information/documents/introduction-letter", UploadIntroductionLetter)
            .WithName("upload-legal-information-introduction-letter")
            .WithDescription("upload the introduction letter document (JPG/JPEG/PNG/PDF, max 5 MB)")
            .Produces<Result<LegalInformationResponse>>(StatusCodes.Status200OK)
            .RequireAuthorization();

        group.MapPost("/me/legal-information/documents/official-gazette", UploadOfficialGazette)
            .WithName("upload-legal-information-official-gazette")
            .WithDescription("upload the official gazette document (JPG/JPEG/PNG/PDF, max 5 MB)")
            .Produces<Result<LegalInformationResponse>>(StatusCodes.Status200OK)
            .RequireAuthorization();

        group.MapPost("/me/legal-information/submit", SubmitLegalInformation)
            .WithName("submit-legal-information")
            .WithDescription("validate, verify against the tax authority, and send legal information for expert review")
            .Produces<Result<LegalInformationResponse>>(StatusCodes.Status200OK)
            .Produces<Result<LegalInformationResponse>>(StatusCodes.Status400BadRequest)
            .Produces<Result<LegalInformationResponse>>(StatusCodes.Status409Conflict)
            .RequireAuthorization();
    }

    private static async Task<IResult> GetLegalInformation(
        ILegalInformationInboundPort svc,
        IUserProfileService userProfileService,
        CancellationToken ct)
    {
        var result = await svc.GetLegalInformationAsync(userProfileService.IdentityUserId, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> SaveLegalInformationDraft(
        LegalInformationRequest req,
        ILegalInformationInboundPort svc,
        IUserProfileService userProfileService,
        CancellationToken ct)
    {
        var input = new LegalInformationInput(
            req.companyName,
            req.nationalId,
            req.registrationDate,
            req.registrationNumber,
            req.ceoName,
            req.ceoMobileNumber,
            req.provinceId,
            req.cityId,
            req.districtId,
            req.address,
            req.postalCode,
            req.landlineNumber,
            req.fax);

        var result = await svc.SaveLegalInformationDraftAsync(userProfileService.IdentityUserId, input, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> UploadIntroductionLetter(
        IFormFile file,
        ILegalInformationInboundPort svc,
        IUserProfileService userProfileService,
        CancellationToken ct) =>
        await UploadDocument(file, LegalInformationDocumentType.IntroductionLetter, svc, userProfileService, ct);

    private static async Task<IResult> UploadOfficialGazette(
        IFormFile file,
        ILegalInformationInboundPort svc,
        IUserProfileService userProfileService,
        CancellationToken ct) =>
        await UploadDocument(file, LegalInformationDocumentType.OfficialGazette, svc, userProfileService, ct);

    private static async Task<IResult> UploadDocument(
        IFormFile file,
        LegalInformationDocumentType type,
        ILegalInformationInboundPort svc,
        IUserProfileService userProfileService,
        CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        var result = await svc.UploadLegalInformationDocumentAsync(
            userProfileService.IdentityUserId,
            type,
            stream,
            file.FileName,
            file.ContentType,
            file.Length,
            ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> SubmitLegalInformation(
        ILegalInformationInboundPort svc,
        IUserProfileService userProfileService,
        CancellationToken ct)
    {
        var result = await svc.SubmitLegalInformationAsync(userProfileService.IdentityUserId, ct);
        return result.ToHttpResult();
    }
}

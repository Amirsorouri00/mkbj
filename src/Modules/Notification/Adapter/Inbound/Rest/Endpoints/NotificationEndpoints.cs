using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using EwaysVNext.Presentation.Endpoint;
using EwaysVNext.Modules.Notification.Adapter.Inbound.Rest.Dtos;
using EwaysVNext.Modules.Notification.Application.Ports.Inbound;

namespace EwaysVNext.Modules.Notification.Adapter.Inbound.Rest.Endpoints;

public sealed class NotificationEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/notifications").WithTags("Notification");

        group.MapPost("/sms", async (INotificationService svc, SendSmsRequest req, CancellationToken ct) =>
        {
            var result = await svc.SendSmsAsync(req.Recipient, req.Body, ct);
            return result.ToHttpResult();
        }).AllowAnonymous();
    }
}

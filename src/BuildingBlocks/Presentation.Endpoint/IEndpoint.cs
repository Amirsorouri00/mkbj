using Microsoft.AspNetCore.Routing;

namespace EwaysVNext.Presentation.Endpoint;

public interface IEndpoint
{
    void MapEndpoint(IEndpointRouteBuilder app);
}

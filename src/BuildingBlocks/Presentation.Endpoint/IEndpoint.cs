using Microsoft.AspNetCore.Routing;

namespace Mkbj.Presentation.Endpoint;

public interface IEndpoint
{
    void MapEndpoint(IEndpointRouteBuilder app);
}

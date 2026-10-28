using System.Security.Claims;
using System.Text.Json;
using AuthorizationService.Application.Permissions;
using AuthorizationService.Application.Permissions.Requests;
using AuthorizationService.Application.Permissions.Responses;
using AuthorizationService.Application.Roles;
using AuthorizationService.Application.Roles.Requests;
using AuthorizationService.Application.Roles.Responses;
using AuthorizationService.Application.Scopes;
using AuthorizationService.Application.UserRoleScopes;
using AuthorizationService.Application.UserRoleScopes.Responses;
using AuthorizationService.Endpoints;
using AwesomeAssertions;
using EwaysVNext.Outcomes;
using EwaysVNext.Presentation.Endpoint;
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;
using Error = EwaysVNext.Outcomes.Error;

namespace EwaysVNext.UnitTests;

public class AuthorizationGraphQLMutationTests
{
    private static readonly Guid UserId = Guid.Parse("7a1f3c52-2b8e-4a55-9d1e-0c6f1f7b9e11");
    private static readonly Guid RoleId = Guid.Parse("1c7d0e8a-5f3b-4a2d-8c9e-6b4f2a1d3e50");
    private static readonly Guid ScopeId = Guid.Parse("9e2b4c6d-8f1a-4b3c-a5d7-e9f0a1b2c3d4");

    private readonly IPermissionService _permissions = Substitute.For<IPermissionService>();
    private readonly IRoleService _roles = Substitute.For<IRoleService>();
    private readonly IScopeService _scopes = Substitute.For<IScopeService>();
    private readonly IUserRoleScopeService _userRoleScopes = Substitute.For<IUserRoleScopeService>();

    public static TheoryData<string> Mutations => new()
    {
        $$"""mutation { createPermission(input: { code: "c", name: "n", rules: [] }) { permission { id } } }""",
        $$"""mutation { updatePermission(input: { id: "{{RoleId}}", name: "n", rules: [] }) { permission { id } } }""",
        $$"""mutation { deletePermission(input: { id: "{{RoleId}}" }) { success } }""",
        $$"""mutation { createRole(input: { code: "c", name: "n", rank: 1 }) { role { id } } }""",
        $$"""mutation { updateRole(input: { id: "{{RoleId}}", name: "n", rank: 1 }) { role { id } } }""",
        $$"""mutation { deleteRole(input: { id: "{{RoleId}}" }) { success } }""",
        $$"""mutation { createScope(input: { code: "c", name: "n" }) { scope { id } } }""",
        $$"""mutation { updateScope(input: { id: "{{ScopeId}}", name: "n" }) { scope { id } } }""",
        $$"""mutation { deleteScope(input: { id: "{{ScopeId}}" }) { success } }""",
        $$"""mutation { bootstrapAdmin(input: { scopeId: "{{ScopeId}}" }) { assignment { id } } }""",
        $$"""mutation { assignUserRoleScope(input: { userId: "{{UserId}}", roleId: "{{RoleId}}", scopeId: "{{ScopeId}}" }) { assignment { id } } }""",
        $$"""mutation { removeUserRoleScope(input: { id: "{{RoleId}}" }) { success } }""",
    };

    private async Task<JsonElement> ExecuteAsync(string document, ClaimsPrincipal? user = null)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton(_permissions).AddSingleton(_roles).AddSingleton(_scopes).AddSingleton(_userRoleScopes);
        var caller = user ?? new ClaimsPrincipal(new ClaimsIdentity());
        services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = caller } });
        services.AddAuthorizationGraphQL();

        var executor = await services.BuildServiceProvider().GetRequestExecutorAsync();
        var request = OperationRequestBuilder.New().SetDocument(document).SetUser(caller);

        var result = await executor.ExecuteAsync(request.Build());
        using var json = JsonDocument.Parse(result.ToJson());
        return json.RootElement.Clone();
    }

    private static ClaimsPrincipal SignedIn() =>
        new(new ClaimsIdentity([new Claim("sub", UserId.ToString())], authenticationType: "Test"));

    private static string FirstErrorCode(JsonElement result) =>
        result.GetProperty("errors")[0].GetProperty("extensions").GetProperty("code").GetString()!;

    [Fact]
    public void No_Rest_Endpoint_Is_Left_In_Authorization()
    {
        typeof(AuthorizationService.Endpoints.DependencyInjection).Assembly.GetTypes()
            .Where(t => typeof(IEndpoint).IsAssignableFrom(t))
            .Should().BeEmpty("Authorization is served over GraphQL only");
    }

    [Theory]
    [MemberData(nameof(Mutations))]
    public async Task An_Anonymous_Caller_Is_Rejected_Before_Any_Service_Runs(string mutation)
    {
        var result = await ExecuteAsync(mutation);

        FirstErrorCode(result).Should().Be("AUTH_NOT_AUTHENTICATED");
        _permissions.ReceivedCalls().Should().BeEmpty();
        _roles.ReceivedCalls().Should().BeEmpty();
        _scopes.ReceivedCalls().Should().BeEmpty();
        _userRoleScopes.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task CreateRole_Passes_The_Input_To_The_Service_And_Returns_The_Created_Role()
    {
        _roles.CreateAsync(Arg.Any<CreateRoleRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new RoleDto(RoleId, "admin", "Admin", 3)));

        var result = await ExecuteAsync(
            """mutation { createRole(input: { code: "admin", name: "Admin", rank: 3 }) { role { id code rank } } }""", SignedIn());

        result.GetProperty("data").GetProperty("createRole").GetProperty("role").GetProperty("code").GetString().Should().Be("admin");
        await _roles.Received(1).CreateAsync(new CreateRoleRequest("admin", "Admin", 3), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreatePermission_Maps_Every_Rule_Into_The_Service_Request()
    {
        _permissions.CreateAsync(Arg.Any<CreatePermissionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new PermissionDto(RoleId, "reports.read", "Read reports", null, [])));

        var result = await ExecuteAsync(
            $$"""mutation { createPermission(input: { code: "reports.read", name: "Read reports", rules: [{ roleId: "{{RoleId}}", scopeIds: ["{{ScopeId}}"] }] }) { permission { code } } }""",
            SignedIn());

        result.GetProperty("data").GetProperty("createPermission").GetProperty("permission").GetProperty("code").GetString()
            .Should().Be("reports.read");
        await _permissions.Received(1).CreateAsync(
            Arg.Is<CreatePermissionRequest>(r =>
                r.Code == "reports.read" && r.Rules.Count == 1 && r.Rules[0].RoleId == RoleId && r.Rules[0].ScopeIds!.Single() == ScopeId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BootstrapAdmin_Assigns_The_Callers_Own_User_Id()
    {
        _userRoleScopes.BootstrapAdminAsync(UserId, ScopeId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new UserRoleScopeDto(RoleId, UserId, RoleId, "admin", ScopeId, "shop", TestTime.Now.UtcDateTime)));

        var result = await ExecuteAsync(
            $$"""mutation { bootstrapAdmin(input: { scopeId: "{{ScopeId}}" }) { assignment { userId } } }""", SignedIn());

        result.GetProperty("data").GetProperty("bootstrapAdmin").GetProperty("assignment").GetProperty("userId").GetString()
            .Should().Be(UserId.ToString());
    }

    [Fact]
    public async Task BootstrapAdmin_Refuses_A_Token_Without_A_User_Id()
    {
        var withoutUserId = new ClaimsPrincipal(new ClaimsIdentity([new Claim("name", "someone")], authenticationType: "Test"));

        var result = await ExecuteAsync(
            $$"""mutation { bootstrapAdmin(input: { scopeId: "{{ScopeId}}" }) { assignment { id } } }""", withoutUserId);

        FirstErrorCode(result).Should().Be("AUTH_NOT_AUTHENTICATED");
        _userRoleScopes.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task A_Refusal_From_The_Service_Surfaces_With_Its_Own_Error_Code()
    {
        _roles.DeleteAsync(RoleId, Arg.Any<CancellationToken>())
            .Returns(Result.Failure(new Error("ROLE_IN_USE", "in use", 409)));

        var result = await ExecuteAsync($$"""mutation { deleteRole(input: { id: "{{RoleId}}" }) { success } }""", SignedIn());

        FirstErrorCode(result).Should().Be("ROLE_IN_USE");
    }
}

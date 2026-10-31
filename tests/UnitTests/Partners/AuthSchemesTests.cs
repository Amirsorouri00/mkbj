using AwesomeAssertions;
using EwaysVNext.Presentation.Endpoint.Security;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace EwaysVNext.UnitTests.Partners;

/// <summary>
/// The single decision that keeps a partner key away from the JWT reader.
/// <para>
/// It is covered here rather than through the HTTP suite because the integration fixture
/// substitutes its own scheme router — test JWTs are signed by an issuer no realm claims, so the
/// production router would reject them. Removing the partner branch from the production router
/// therefore left every integration test green. These assertions are what actually hold it.
/// </para>
/// </summary>
public sealed class AuthSchemesTests
{
    private static HttpRequest Request(string? authorization)
    {
        var context = new DefaultHttpContext();

        if (authorization is not null)
            context.Request.Headers.Authorization = authorization;

        return context.Request;
    }

    [Theory]
    [InlineData("Bearer pk_ABCDEFGH_0000000000000000000000000000000000000000000")]
    [InlineData("bearer pk_ABCDEFGH_0000000000000000000000000000000000000000000")]
    [InlineData("BEARER pk_ABCDEFGH_0000000000000000000000000000000000000000000")]
    [InlineData("Bearer   pk_short")]
    public void A_Bearer_Partner_Key_Is_Recognised(string header) =>
        AuthSchemes.IsPartnerKey(Request(header)).Should().BeTrue(
            "the scheme word is case-insensitive per HTTP, and extra spacing is legal");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bearer eyJhbGciOiJIUzI1NiJ9.e30.sig")]
    [InlineData("Bearer PK_ABCDEFGH_x")]
    [InlineData("Basic pk_ABCDEFGH_x")]
    [InlineData("pk_ABCDEFGH_x")]
    public void Anything_Else_Is_Not_A_Partner_Key(string? header) =>
        AuthSchemes.IsPartnerKey(Request(header)).Should().BeFalse();

    [Fact]
    public void The_Prefix_Match_Is_Case_Sensitive() =>
        AuthSchemes.IsPartnerKey(Request("Bearer Pk_ABCDEFGH_x")).Should().BeFalse(
            "the prefix is part of a credential, not a word: a near-miss must not be treated as "
            + "a partner key and then fail to parse");
}

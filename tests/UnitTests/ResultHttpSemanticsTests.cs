using AwesomeAssertions;
using EwaysVNext.Outcomes;
using Xunit;

namespace ModularMonolith.UnitTests;

public class ResultHttpSemanticsTests
{
    [Theory]
    [InlineData("ORDER_NOT_FOUND", 404)]
    [InlineData("CUSTOMER_NOT_FOUND", 404)]
    [InlineData("RESERVATION_NOT_FOUND", 404)]
    public void AdHoc_NotFound_Codes_Map_To_404(string code, int expected) =>
        new Error(code, "x").HttpStatus.Should().Be(expected);

    [Theory]
    [InlineData("SKU_TAKEN", 409)]
    [InlineData("BRAND_SLUG_CONFLICT", 409)]
    [InlineData("DUPLICATE_LINE", 409)]
    public void AdHoc_Duplicate_Codes_Map_To_409(string code, int expected) =>
        new Error(code, "x").HttpStatus.Should().Be(expected);

    [Theory]
    [InlineData("SUPPLIER_SUSPENDED", 403)]
    [InlineData("PAYMENT_FORBIDDEN", 403)]
    public void Suspension_And_Forbidden_Codes_Map_To_403(string code, int expected) =>
        new Error(code, "x").HttpStatus.Should().Be(expected);

    [Fact]
    public void Internal_Prefix_Maps_To_500() =>
        new Error("INTERNAL_STORAGE_FAILURE", "x").HttpStatus.Should().Be(500);

    [Theory]
    [InlineData("VALIDATION_FAILED")]
    [InlineData("CARD_EXPIRED")]
    [InlineData("ORDER_STATUS_UNSUPPORTED")]
    [InlineData("TOTALLY_UNKNOWN_CODE")]
    public void Unrecognised_Codes_Deliberately_Remain_400(string code) =>
        new Error(code, "x").HttpStatus.Should().Be(400);

    [Fact]
    public void Named_Presets_Carry_Explicit_Statuses()
    {
        Error.NotFound.HttpStatus.Should().Be(404);
        Error.Conflict.HttpStatus.Should().Be(409);
        Error.Unauthorized.HttpStatus.Should().Be(401);
        Error.Forbidden.HttpStatus.Should().Be(403);
        Error.Internal.HttpStatus.Should().Be(500);
        Error.ConcurrencyConflict.HttpStatus.Should().Be(409);
        Error.Validation.HttpStatus.Should().Be(400);
    }

    [Fact]
    public void Explicit_Override_Always_Wins_Over_Heuristic()
    {
        new Error("WEIRD_NOT_FOUND", "x", 409).HttpStatus.Should().Be(409);
    }

    [Fact]
    public void None_Preserves_Default_Behaviour()
    {
        Error.None.Code.Should().BeEmpty();
        Error.None.HttpStatus.Should().Be(400);
    }

    [Theory]
    [InlineData("SAGA_STEP_FAILED")]
    [InlineData("SAGA_NULL_RESULT")]
    [InlineData("RESERVATION_FAILED")]
    public void Server_Fault_Codes_Are_Declared_500_At_Their_Production_Site(string code)
    {
        Error.SuggestHttpStatus(code).Should().Be(400,
            "the heuristic must stay conservative; a blanket _FAILED → 500 rule would " +
            "misclassify business outcomes like a declined payment");

        new Error(code, "x", 500).HttpStatus.Should().Be(500);
    }

    [Theory]
    [InlineData("ADMIN_PAGE_INVALID", 400)]
    [InlineData("ADMIN_PAGE_SIZE_INVALID", 400)]
    [InlineData("REPORT_PAGE_INVALID", 400)]
    [InlineData("REPORT_PAGE_SIZE_INVALID", 400)]
    public void Paging_Refusals_Are_Client_Errors(string code, int expected) =>
        new Error(code, "x").HttpStatus.Should().Be(expected);
}

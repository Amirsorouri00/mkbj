using AwesomeAssertions;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Payment.Application.Domain.ValueObjects;
using Xunit;

namespace EwaysVNext.PaymentTests;

public class GatewayAttemptTests
{
    private static readonly DateTimeOffset RequestedAt = new(2026, 9, 29, 12, 30, 0, TimeSpan.FromHours(3.5));

    [Fact]
    public void Create_requires_the_request_time()
    {
        var act = () => GatewayAttempt.Create("parsian", 1001, "123", default(DateTimeOffset));

        act.Should().Throw<DomainException>().Which.Code.Should().Be("GATEWAY_ATTEMPT_REQUESTED_AT_REQUIRED");
    }

    [Fact]
    public void Request_time_is_part_of_identity_and_survives_transitions()
    {
        var attempt = GatewayAttempt.Create("parsian", 1001, "123", RequestedAt);

        attempt.RequestedAt.Should().Be(RequestedAt);
        attempt.WithProviderReference("456").RequestedAt.Should().Be(RequestedAt);
        attempt.WithConfirmation("456").RequestedAt.Should().Be(RequestedAt);
        attempt.Should().NotBe(GatewayAttempt.Create("parsian", 1001, "123", RequestedAt.AddSeconds(1)));
    }

    [Fact]
    public void Create_retains_the_provider_neutral_attempt()
    {
        var attempt = GatewayAttempt.Create("parsian", 1001, "123", RequestedAt);

        attempt.ProviderName.Should().Be("parsian");
        attempt.RequestId.Should().Be(1001);
        attempt.Token.Should().Be("123");
        attempt.ProviderReference.Should().BeNull();
    }

    [Theory]
    [InlineData("", 1001, "123", null, "GATEWAY_ATTEMPT_PROVIDER_REQUIRED")]
    [InlineData("  ", 1001, "123", null, "GATEWAY_ATTEMPT_PROVIDER_REQUIRED")]
    [InlineData(null, 1001, "123", null, "GATEWAY_ATTEMPT_PROVIDER_REQUIRED")]
    [InlineData("parsian", 0, "123", null, "GATEWAY_ATTEMPT_REQUEST_ID_INVALID")]
    [InlineData("parsian", -1, "123", null, "GATEWAY_ATTEMPT_REQUEST_ID_INVALID")]
    [InlineData("parsian", 1001, "", null, "GATEWAY_ATTEMPT_TOKEN_REQUIRED")]
    [InlineData("parsian", 1001, " ", null, "GATEWAY_ATTEMPT_TOKEN_REQUIRED")]
    [InlineData("parsian", 1001, null, null, "GATEWAY_ATTEMPT_TOKEN_REQUIRED")]
    [InlineData("parsian", 1001, "123", "", "GATEWAY_ATTEMPT_REFERENCE_INVALID")]
    [InlineData("parsian", 1001, "123", " ", "GATEWAY_ATTEMPT_REFERENCE_INVALID")]
    public void Create_refuses_an_invalid_attempt(string? provider, long requestId, string? token, string? reference, string code)
    {
        var act = () => GatewayAttempt.Create(provider!, requestId, token!, RequestedAt, reference);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(code);
    }

    [Fact]
    public void WithProviderReference_keeps_the_original_identity()
    {
        var attempt = GatewayAttempt.Create("mellat", 1001, "RefId", RequestedAt);

        var referenced = attempt.WithProviderReference("456");

        referenced.Should().Be(GatewayAttempt.Create("mellat", 1001, "RefId", RequestedAt, "456"));
        attempt.ProviderReference.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void WithProviderReference_refuses_a_blank_reference(string? reference)
    {
        var act = () => GatewayAttempt.Create("mellat", 1001, "RefId", RequestedAt).WithProviderReference(reference!);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("GATEWAY_ATTEMPT_REFERENCE_INVALID");
    }

    [Fact]
    public void WithConfirmation_marks_the_attempt_confirmed_with_the_bank_reference()
    {
        var attempt = GatewayAttempt.Create("parsian", 1001, "123", RequestedAt);

        var confirmed = attempt.WithConfirmation("456");

        confirmed.IsConfirmed.Should().BeTrue();
        confirmed.ProviderReference.Should().Be("456");
        confirmed.ProviderName.Should().Be("parsian");
        confirmed.RequestId.Should().Be(1001);
        confirmed.Token.Should().Be("123");
        attempt.IsConfirmed.Should().BeFalse();
        GatewayAttempt.Create("parsian", 1001, "123", RequestedAt, "456").IsConfirmed.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void WithConfirmation_requires_a_bank_reference(string? reference)
    {
        var act = () => GatewayAttempt.Create("parsian", 1001, "123", RequestedAt).WithConfirmation(reference!);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("GATEWAY_ATTEMPT_REFERENCE_INVALID");
    }

    [Fact]
    public void WithProviderReference_keeps_the_confirmation_state()
    {
        GatewayAttempt.Create("mellat", 1001, "RefId", RequestedAt).WithConfirmation("456").WithProviderReference("457")
            .IsConfirmed.Should().BeTrue();
    }

    [Fact]
    public void Equality_distinguishes_a_confirmed_attempt()
    {
        GatewayAttempt.Create("parsian", 1001, "123", RequestedAt, "456")
            .Should().NotBe(GatewayAttempt.Create("parsian", 1001, "123", RequestedAt).WithConfirmation("456"));
    }

    [Fact]
    public void Equality_uses_every_identity_component()
    {
        var attempt = GatewayAttempt.Create("parsian", 1001, "123", RequestedAt, "456");

        attempt.Should().Be(GatewayAttempt.Create("parsian", 1001, "123", RequestedAt, "456"));
        attempt.Should().NotBe(GatewayAttempt.Create("mellat", 1001, "123", RequestedAt, "456"));
        attempt.Should().NotBe(GatewayAttempt.Create("parsian", 1002, "123", RequestedAt, "456"));
        attempt.Should().NotBe(GatewayAttempt.Create("parsian", 1001, "124", RequestedAt, "456"));
        attempt.Should().NotBe(GatewayAttempt.Create("parsian", 1001, "123", RequestedAt));
    }
}

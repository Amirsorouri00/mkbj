using AwesomeAssertions;
using EwaysVNext.Modules.Notification.Application.Ports.Outbound;
using EwaysVNext.Modules.Notification.Application.Service;
using EwaysVNext.Outcomes;
using NSubstitute;
using Xunit;

namespace EwaysVNext.UnitTests;

public class NotificationServiceTests
{
    private static NotificationService CreateSut(ISmsProviderResolver? resolver = null) =>
        new(resolver ?? Substitute.For<ISmsProviderResolver>());

    private static ISmsProvider StubProvider(string name = "PayamSms")
    {
        var provider = Substitute.For<ISmsProvider>();
        provider.Name.Returns(name);
        return provider;
    }

    [Fact]
    public async Task SendSmsAsync_Returns_Snapshot_When_Provider_Accepts_It()
    {
        var provider = StubProvider();
        provider.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new SmsSendResult("214159974")));

        var resolver = Substitute.For<ISmsProviderResolver>();
        resolver.Resolve(Arg.Any<string?>()).Returns(Result.Success(provider));

        var sut = CreateSut(resolver);

        var result = await sut.SendSmsAsync("98912XXX3245", "test");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Recipient.Should().Be("98912XXX3245");
        result.Value.Body.Should().Be("test");
        result.Value.ProviderName.Should().Be("PayamSms");
        result.Value.ProviderMessageId.Should().Be("214159974");
    }

    [Fact]
    public async Task SendSmsAsync_Fails_When_Provider_Fails()
    {
        var provider = StubProvider();
        var error = new Error("PAYAMSMS_E6", "Not Enough Credit");
        provider.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<SmsSendResult>(error));

        var resolver = Substitute.For<ISmsProviderResolver>();
        resolver.Resolve(Arg.Any<string?>()).Returns(Result.Success(provider));

        var sut = CreateSut(resolver);

        var result = await sut.SendSmsAsync("98912XXX3245", "test");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    [Fact]
    public async Task SendSmsAsync_Fails_Fast_When_No_Provider_Is_Configured()
    {
        var resolver = Substitute.For<ISmsProviderResolver>();
        var error = new Error("SMS_PROVIDER_NOT_CONFIGURED", "No SMS provider is registered.");
        resolver.Resolve(Arg.Any<string?>()).Returns(Result.Failure<ISmsProvider>(error));

        var sut = CreateSut(resolver);

        var result = await sut.SendSmsAsync("98912XXX3245", "test");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    [Fact]
    public async Task SendSmsAsync_Rejects_Empty_Recipient()
    {
        var sut = CreateSut();

        var result = await sut.SendSmsAsync("", "test");

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("NOTIFICATION_RECIPIENT_EMPTY");
    }

    [Fact]
    public async Task SendSmsAsync_Rejects_Empty_Body()
    {
        var sut = CreateSut();

        var result = await sut.SendSmsAsync("98912XXX3245", " ");

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("NOTIFICATION_BODY_EMPTY");
    }
}

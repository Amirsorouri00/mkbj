using AwesomeAssertions;
using EwaysVNext.Storage.Implementation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace EwaysVNext.UnitTests;

/// <summary>
/// An unconfigured object store used to surface as a 500 from <c>/health</c>: the health check's
/// constructor threw <see cref="UriFormatException"/> while the health middleware was resolving
/// it, so the exception escaped past the middleware entirely. A probe that reports "internal
/// server error" instead of "unhealthy" tells an operator the wrong thing, and the message named
/// no setting. Validation moves the failure to startup, where it names the key.
/// </summary>
public sealed class ObjectStorageOptionsTests
{
    private static ServiceProvider Build(params (string Key, string? Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s =>
                new KeyValuePair<string, string?>($"{MinioOptions.SectionName}:{s.Key}", s.Value)))
            .Build();

        return new ServiceCollection().AddObjectStorage(configuration).BuildServiceProvider();
    }

    private static Action Resolve(IServiceProvider services) =>
        () => services.GetRequiredService<IOptions<MinioOptions>>().Value.ToString();

    [Fact]
    public void A_Complete_Configuration_Is_Accepted() =>
        Resolve(Build(
            ("Endpoint", "http://minio:9000"),
            ("AccessKey", "key"),
            ("SecretKey", "secret"))).Should().NotThrow();

    [Theory]
    [InlineData("Endpoint")]
    [InlineData("AccessKey")]
    [InlineData("SecretKey")]
    public void A_Missing_Setting_Is_Reported_By_Name(string missing)
    {
        var settings = new List<(string, string?)>
        {
            ("Endpoint", "http://minio:9000"),
            ("AccessKey", "key"),
            ("SecretKey", "secret"),
        };
        settings.RemoveAll(s => s.Item1 == missing);

        Resolve(Build([.. settings])).Should().Throw<OptionsValidationException>()
            .WithMessage($"*{MinioOptions.SectionName}:{missing}*",
                "an operator fixes a setting they can find");
    }

    [Theory]
    [InlineData("minio:9000", "no scheme")]
    [InlineData("ftp://minio:9000", "wrong scheme")]
    [InlineData("  ", "whitespace reads as absent")]
    public void An_Endpoint_That_Is_Not_An_Http_Url_Is_Rejected(string endpoint, string why)
    {
        Resolve(Build(
            ("Endpoint", endpoint),
            ("AccessKey", "key"),
            ("SecretKey", "secret")))
            .Should().Throw<OptionsValidationException>(why);
    }

    [Fact]
    public void A_Public_Endpoint_That_Is_Not_An_Http_Url_Is_Rejected() =>
        Resolve(Build(
            ("Endpoint", "http://minio:9000"),
            ("PublicEndpoint", "not-a-url"),
            ("AccessKey", "key"),
            ("SecretKey", "secret")))
            .Should().Throw<OptionsValidationException>(
                "presigned URLs are signed for this host, so a malformed one is signed for nothing");
}

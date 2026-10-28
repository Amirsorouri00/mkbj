using AwesomeAssertions;
using EwaysVNext.Jobs.Implementation;
using Xunit;

namespace EwaysVNext.UnitTests;

public sealed class JobBackoffTests
{
    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 10)]
    [InlineData(3, 20)]
    [InlineData(20, 3600)]
    public void Backoff_Doubles_And_Caps(int attempt, int seconds) =>
        JobBackoff.For(attempt, TimeSpan.FromSeconds(5), TimeSpan.FromHours(1))
            .Should().Be(TimeSpan.FromSeconds(seconds));
}

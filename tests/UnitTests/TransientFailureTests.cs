using System.Net.Sockets;
using AwesomeAssertions;
using EwaysVNext.Persistence;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Xunit;

namespace EwaysVNext.UnitTests;

/// <summary>
/// A transient failure becomes a retryable 503, not an opaque 500: the caller is told that trying
/// again may work. Anything else must stay non-retryable, or clients will hammer a broken endpoint.
/// </summary>
public sealed class TransientFailureTests
{
    [Fact]
    public void Transient_Detection()
    {
        TransientFailure.Is(new RetryLimitExceededException("x", new TimeoutException())).Should().BeTrue();
        TransientFailure.Is(new TimeoutException()).Should().BeTrue();
        TransientFailure.Is(new NpgsqlException("x", new SocketException())).Should().BeTrue();
        TransientFailure.Is(new InvalidOperationException()).Should().BeFalse();
    }
}

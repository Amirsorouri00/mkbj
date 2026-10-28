using System.Data.Common;
using System.Net.Sockets;
using Microsoft.EntityFrameworkCore.Storage;

namespace EwaysVNext.Persistence;

/// <summary>
/// Whether a failure is worth retrying. Used to turn an infrastructure blip into a retryable 503
/// instead of an opaque 500 — the difference matters to a partner deciding whether to resend.
/// </summary>
public static class TransientFailure
{
    public static bool Is(Exception? exception) => exception switch
    {
        null => false,
        RetryLimitExceededException => true,
        TimeoutException => true,
        SocketException => true,
        // Npgsql marks the faults it considers retryable (connection loss, deadlock, serialization
        // failure); trust the provider rather than maintaining a SQLSTATE list here.
        DbException { IsTransient: true } => true,
        _ => Is(exception.InnerException),
    };
}

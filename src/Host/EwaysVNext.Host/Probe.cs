namespace EwaysVNext.Host;

/// <summary>
/// A health probe that runs as the container's own process, so an image can declare its
/// healthcheck without needing curl or wget installed — a chiselled base image has neither.
/// </summary>
public static class Probe
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    /// <summary>0 = healthy, 1 = unhealthy, 64 = usage error (EX_USAGE).</summary>
    public static async Task<int> RunAsync(string[] args)
    {
        var index = Array.IndexOf(args, "--probe");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.Error.WriteLine(ProgramMessages.ProbeUsage);
            return 64;
        }

        using var client = new HttpClient { Timeout = Timeout };

        try
        {
            using var response = await client.GetAsync(args[index + 1]);
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
        {
            // Unreachable or too slow is exactly what the probe exists to report, not an error to
            // surface as a stack trace.
            return 1;
        }
    }
}

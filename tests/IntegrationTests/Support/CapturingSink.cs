using System.Collections.Concurrent;
using System.Globalization;
using Serilog.Core;
using Serilog.Events;

namespace EwaysVNext.IntegrationTests.Support;

/// <summary>
/// Keeps every log event the application emits so a test can search all of it — rendered message,
/// every property value, and any exception text. A secret can leak through any of the three, so
/// asserting only on the message would prove very little.
/// </summary>
public sealed class CapturingSink : ILogEventSink
{
    private readonly ConcurrentQueue<LogEvent> _events = new();

    public IReadOnlyCollection<LogEvent> Events => _events;

    public void Emit(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        _events.Enqueue(logEvent);
    }

    public string AllText()
    {
        var builder = new System.Text.StringBuilder();

        foreach (var logEvent in _events)
        {
            builder.AppendLine(logEvent.RenderMessage(CultureInfo.InvariantCulture));

            foreach (var property in logEvent.Properties)
            {
                builder.Append(property.Key).Append('=');
                builder.AppendLine(property.Value.ToString());
            }

            if (logEvent.Exception is not null)
                builder.AppendLine(logEvent.Exception.ToString());
        }

        return builder.ToString();
    }
}

using Microsoft.Extensions.Options;

namespace EwaysVNext.Storage.Implementation;

/// <summary>
/// Without this, a missing setting first surfaced as a <see cref="UriFormatException"/> thrown by
/// the <see cref="S3ObjectStorage"/> constructor while the health middleware was resolving the
/// storage health check — which escaped the middleware and turned <c>/health</c> into a 500. An
/// operator reading "internal server error" looks for a fault in the application; the fault was a
/// blank configuration key nobody named. Failing at startup instead is both earlier and specific.
/// </summary>
internal sealed class MinioOptionsValidator : IValidateOptions<MinioOptions>
{
    public ValidateOptionsResult Validate(string? name, MinioOptions options)
    {
        var failures = new List<string>();

        Require(options.Endpoint, nameof(options.Endpoint));
        Require(options.AccessKey, nameof(options.AccessKey));
        Require(options.SecretKey, nameof(options.SecretKey));

        // PostConfigure copies Endpoint into a blank PublicEndpoint, so by now an invalid value
        // here is one that was set explicitly.
        RequireHttpUrl(options.Endpoint, nameof(options.Endpoint));
        RequireHttpUrl(options.PublicEndpoint, nameof(options.PublicEndpoint));

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);

        void Require(string? value, string key)
        {
            if (string.IsNullOrWhiteSpace(value))
                failures.Add($"{MinioOptions.SectionName}:{key} is required.");
        }

        void RequireHttpUrl(string? value, string key)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                failures.Add(
                    $"{MinioOptions.SectionName}:{key} must be an absolute http or https URL, not '{value}'.");
            }
        }
    }
}
